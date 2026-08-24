using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Duckburg.Identity.Services;

/// <summary>
/// SPID/CIE OIDC authorization-code flow: PKCE, signed request object (JAR),
/// private_key_jwt client auth, encrypted UserInfo, refresh and RP-initiated logout.
/// </summary>
public sealed class OidcClient(
    RpConfig rp,
    JwtService jwt,
    TrustChainResolver resolver,
    AuthSessionStore sessions,
    IHttpClientFactory httpFactory,
    ILogger<OidcClient> log)
{
    // acr_values MUST be a list. Including SpidL1 enables refresh tokens (the OP issues a
    // refresh_token only when offline_access + prompt=consent + an L1 acr are present).
    private static readonly string[] Acr =
        ["https://www.spid.gov.it/SpidL2", "https://www.spid.gov.it/SpidL1"];
    private const int RequestExp = 60;
    private const string FiscalNumberClaim = "https://attributes.eid.gov.it/fiscal_number";
    private const string Scope = "openid offline_access";

    public async Task<string> BeginAsync(string providerId, string profile, string trustAnchorId, string? returnUrl, CancellationToken ct)
    {
        var provider = await resolver.ResolveAsync(providerId, trustAnchorId, ct);

        var state = RandomString(32);
        var nonce = RandomString(32);
        var (verifier, challenge) = Pkce();
        var redirectUri = rp.RedirectUris[0];
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var requestObject = new Dictionary<string, object>
        {
            ["iss"] = rp.Sub,
            ["scope"] = Scope,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["nonce"] = nonce,
            ["state"] = state,
            ["client_id"] = rp.Sub,
            ["endpoint"] = provider.AuthorizationEndpoint,
            ["acr_values"] = Acr,
            ["iat"] = now,
            ["exp"] = now + RequestExp,
            ["jti"] = Guid.NewGuid().ToString(),
            ["aud"] = new[] { provider.Issuer, provider.AuthorizationEndpoint },
            ["claims"] = RequestedClaims(profile),
            ["prompt"] = "consent login",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        };

        var requestJws = jwt.Sign(requestObject, rp.CoreSigningKey);

        sessions.Save(new AuthSession
        {
            State = state,
            Nonce = nonce,
            CodeVerifier = verifier,
            RedirectUri = redirectUri,
            ReturnUrl = returnUrl,
            ProviderId = provider.Issuer,
            Provider = provider
        });

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = rp.Sub,
            ["scope"] = Scope,
            ["response_type"] = "code",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["request"] = requestJws
        };
        var sep = provider.AuthorizationEndpoint.Contains('?') ? "&" : "?";
        var qs = string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));
        var url = provider.AuthorizationEndpoint + sep + qs;
        log.LogInformation("Authz request -> {Url}", provider.AuthorizationEndpoint);
        return url;
    }

    public async Task<AuthSession> CompleteAsync(string state, string code, string? iss, CancellationToken ct)
    {
        var session = sessions.Get(state) ?? throw new InvalidOperationException("Unknown state (session not found)");

        if (!string.IsNullOrEmpty(iss) && iss != session.ProviderId)
            throw new InvalidOperationException("Mix-up attack prevention: iss mismatch");

        var http = httpFactory.CreateClient("oidc");

        var tokenEndpoint = session.Provider.TokenEndpoint;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = session.RedirectUri,
            ["client_id"] = rp.Sub,
            ["state"] = state,
            ["code"] = code,
            ["code_verifier"] = session.CodeVerifier,
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = ClientAssertion(tokenEndpoint)
        };

        var tokenResp = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), ct);
        var tokenBody = await tokenResp.Content.ReadAsStringAsync(ct);
        if (!tokenResp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token endpoint {(int)tokenResp.StatusCode}: {tokenBody}");

        var token = JsonDocument.Parse(tokenBody).RootElement;
        var accessToken = token.GetProperty("access_token").GetString()!;
        var idToken = token.GetProperty("id_token").GetString()!;

        var idKid = JwtService.ReadKid(idToken);
        var opKey = session.Provider.SigningKeys.ByKid(idKid)
            ?? throw new InvalidOperationException("id_token signed with unknown OP kid");
        var idClaims = jwt.Verify(idToken, opKey);

        if (idClaims.GetProperty("iss").GetString() != session.ProviderId)
            throw new InvalidOperationException("id_token iss mismatch");
        if (!AudienceContains(idClaims, rp.Sub))
            throw new InvalidOperationException("id_token aud does not contain client_id");
        if (idClaims.TryGetProperty("nonce", out var n) && n.GetString() != session.Nonce)
            throw new InvalidOperationException("id_token nonce mismatch");

        session.AccessToken = accessToken;
        session.IdToken = idToken;
        session.AccessTokenExp = ReadExp(accessToken);
        if (token.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String)
        {
            session.RefreshToken = rt.GetString();
            session.RefreshTokenExp = ReadExp(session.RefreshToken!);
        }

        var profile = await GetUserInfoAsync(http, session, accessToken, ct);
        session.Profile = profile;
        log.LogInformation("Login complete for sub {Sub} (refresh_token={HasRt})",
            profile.Sub, session.RefreshToken is not null);
        return session;
    }

    public async Task<AuthSession> RefreshAsync(string state, CancellationToken ct)
    {
        var session = sessions.Get(state) ?? throw new InvalidOperationException("Unknown state (session not found)");
        if (string.IsNullOrEmpty(session.RefreshToken))
            throw new InvalidOperationException("No refresh_token available (OP did not issue one)");

        var http = httpFactory.CreateClient("oidc");
        var tokenEndpoint = session.Provider.TokenEndpoint;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = session.RefreshToken,
            ["client_id"] = rp.Sub,
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = ClientAssertion(tokenEndpoint)
        };

        var resp = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Refresh failed {(int)resp.StatusCode}: {body}");

        var token = JsonDocument.Parse(body).RootElement;
        session.AccessToken = token.GetProperty("access_token").GetString()!;
        session.AccessTokenExp = ReadExp(session.AccessToken);
        if (token.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String)
        {
            session.RefreshToken = rt.GetString();
            session.RefreshTokenExp = ReadExp(session.RefreshToken!);
        }
        if (token.TryGetProperty("id_token", out var it) && it.ValueKind == JsonValueKind.String)
            session.IdToken = it.GetString();
        log.LogInformation("Refreshed tokens for state {State}", state);
        return session;
    }

    public async Task LogoutAsync(string state, CancellationToken ct)
    {
        var session = sessions.Get(state);
        if (session is null) return;
        var endpoint = session.Provider.RevocationEndpoint;
        if (!string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(session.AccessToken))
        {
            try
            {
                var http = httpFactory.CreateClient("oidc");
                var form = new Dictionary<string, string>
                {
                    ["token"] = session.AccessToken!,
                    ["client_id"] = rp.Sub,
                    ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                    ["client_assertion"] = ClientAssertion(endpoint!)
                };
                var resp = await http.PostAsync(endpoint, new FormUrlEncodedContent(form), ct);
                log.LogInformation("Revocation at {Ep} -> {Code}", endpoint, (int)resp.StatusCode);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Token revocation failed (continuing logout)");
            }
        }
        sessions.Remove(state);
    }

    private string ClientAssertion(string audience) => jwt.Sign(new Dictionary<string, object>
    {
        ["iss"] = rp.Sub,
        ["sub"] = rp.Sub,
        ["aud"] = new[] { audience },
        ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ["exp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60,
        ["jti"] = Guid.NewGuid().ToString()
    }, rp.CoreSigningKey);

    private static long ReadExp(string jwt)
    {
        var p = JwtService.ReadPayload(jwt);
        return p.TryGetProperty("exp", out var e) && e.TryGetInt64(out var v) ? v : 0;
    }

    private async Task<UserProfile> GetUserInfoAsync(HttpClient http, AuthSession session, string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, session.Provider.UserinfoEndpoint);
        req.Headers.Add("Authorization", $"Bearer {accessToken}");
        var resp = await http.SendAsync(req, ct);
        var body = (await resp.Content.ReadAsStringAsync(ct)).Trim();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"UserInfo {(int)resp.StatusCode}: {body}");

        var jwe = body;
        var encKid = JwtService.ReadKid(jwe);
        var encKey = rp.JwksCore.ByKid(encKid) ?? rp.CoreEncKey;
        var innerJws = jwt.DecryptJwe(jwe, encKey);

        var sigKid = JwtService.ReadKid(innerJws);
        var opKey = session.Provider.SigningKeys.ByKid(sigKid)
            ?? throw new InvalidOperationException("userinfo signed with unknown OP kid");
        var claims = jwt.Verify(innerJws, opKey);

        return MapProfile(claims);
    }

    private static UserProfile MapProfile(JsonElement c)
    {
        string? S(string k) => c.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var raw = new Dictionary<string, string>();
        foreach (var p in c.EnumerateObject())
            raw[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText();
        return new UserProfile(
            GivenName: S("given_name"),
            FamilyName: S("family_name"),
            Email: S("email"),
            FiscalNumber: S(FiscalNumberClaim),
            Sub: S("sub") ?? "",
            Raw: raw);
    }

    private static bool AudienceContains(JsonElement claims, string value)
    {
        if (!claims.TryGetProperty("aud", out var aud)) return false;
        return aud.ValueKind == JsonValueKind.String
            ? aud.GetString() == value
            : aud.EnumerateArray().Any(a => a.GetString() == value);
    }

    private static object RequestedClaims(string profile)
    {
        var userinfo = new Dictionary<string, object?>
        {
            ["given_name"] = null,
            ["family_name"] = null,
            ["email"] = null,
            [FiscalNumberClaim] = null
        };
        return profile == "cie"
            ? new Dictionary<string, object>
            {
                ["id_token"] = new Dictionary<string, object>
                {
                    ["family_name"] = new { essential = true },
                    ["given_name"] = new { essential = true }
                },
                ["userinfo"] = userinfo
            }
            : new Dictionary<string, object>
            {
                ["id_token"] = new Dictionary<string, object>
                {
                    ["given_name"] = new { essential = true },
                    ["email"] = new { essential = true }
                },
                ["userinfo"] = userinfo
            };
    }

    private static (string verifier, string challenge) Pkce()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        var verifier = Base64Url(bytes);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string RandomString(int len)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var data = RandomNumberGenerator.GetBytes(len);
        var sb = new StringBuilder(len);
        foreach (var b in data) sb.Append(chars[b % chars.Length]);
        return sb.ToString();
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

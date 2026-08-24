using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Jose;

namespace Duckburg.ServiziOnline.Services;

/// <summary>
/// Verifica il token di handoff firmato da Duckburg.Identity dopo il login SPID/CIE.
/// La chiave pubblica arriva dall'endpoint JWKS del sistema di accesso (cache in memoria);
/// il token è un JWS RS256 con iss/aud/exp controllati qui.
/// </summary>
public sealed class SsoTokenValidator(IHttpClientFactory httpFactory, IConfiguration cfg, ILogger<SsoTokenValidator> log)
{
    private JsonElement? _jwks;
    private DateTimeOffset _jwksFetchedAt;
    private static readonly TimeSpan JwksTtl = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string IdentityBaseUrl => cfg["Sso:IdentityBaseUrl"] ?? "http://identity.paperopoli.derrigo.it:8001";
    private string Issuer => cfg["Sso:Issuer"] ?? IdentityBaseUrl;
    private string Audience => cfg["Sso:Audience"] ?? "duckburg-servizionline";

    public async Task<ClaimsPrincipal> ValidateAsync(string token, CancellationToken ct)
    {
        var header = JsonDocument.Parse(DecodePart(token, 0)).RootElement;
        var kid = header.TryGetProperty("kid", out var k) ? k.GetString() : null;

        using var rsa = await GetSigningKeyAsync(kid, ct);
        var payloadJson = JWT.Decode(token, rsa, JwsAlgorithm.RS256);
        var c = JsonDocument.Parse(payloadJson).RootElement;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!c.TryGetProperty("exp", out var exp) || exp.GetInt64() < now)
            throw new InvalidOperationException("token SSO scaduto");
        if (c.GetProperty("iss").GetString() != Issuer)
            throw new InvalidOperationException("iss del token SSO non valido");
        if (c.GetProperty("aud").GetString() != Audience)
            throw new InvalidOperationException("aud del token SSO non valido");

        string? S(string name) => c.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, S("sub") ?? "") };
        void Add(string type, string? value) { if (!string.IsNullOrEmpty(value)) claims.Add(new Claim(type, value)); }
        Add(ClaimTypes.GivenName, S("given_name"));
        Add(ClaimTypes.Surname, S("family_name"));
        Add(ClaimTypes.Email, S("email"));
        Add("fiscal_number", S("fiscal_number"));
        Add(ClaimTypes.Name, $"{S("given_name")} {S("family_name")}".Trim());

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "spid-cie-sso"));
    }

    private async Task<RSA> GetSigningKeyAsync(string? kid, CancellationToken ct)
    {
        var jwks = await GetJwksAsync(ct);
        foreach (var key in jwks.GetProperty("keys").EnumerateArray())
        {
            var keyKid = key.TryGetProperty("kid", out var kk) ? kk.GetString() : null;
            var use = key.TryGetProperty("use", out var u) ? u.GetString() : null;
            if (use == "enc") continue;
            if (kid is not null && keyKid != kid) continue;
            var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Base64Url(key.GetProperty("n").GetString()!),
                Exponent = Base64Url(key.GetProperty("e").GetString()!)
            });
            return rsa;
        }
        throw new InvalidOperationException($"chiave di firma '{kid}' non trovata nel JWKS di Identity");
    }

    private async Task<JsonElement> GetJwksAsync(CancellationToken ct)
    {
        if (_jwks is { } cached && DateTimeOffset.UtcNow - _jwksFetchedAt < JwksTtl)
            return cached;
        await _lock.WaitAsync(ct);
        try
        {
            if (_jwks is { } c2 && DateTimeOffset.UtcNow - _jwksFetchedAt < JwksTtl)
                return c2;
            var url = cfg["Sso:IdentityJwksUrl"] ?? $"{IdentityBaseUrl}/oidc/rp/openid_relying_party/jwks.json";
            var http = httpFactory.CreateClient("sso");
            var body = await http.GetStringAsync(url, ct);
            _jwks = JsonDocument.Parse(body).RootElement.Clone();
            _jwksFetchedAt = DateTimeOffset.UtcNow;
            log.LogInformation("JWKS di Identity aggiornato da {Url}", url);
            return _jwks.Value;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static byte[] Base64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }

    private static byte[] DecodePart(string jwt, int index) => Base64Url(jwt.Split('.')[index]);
}

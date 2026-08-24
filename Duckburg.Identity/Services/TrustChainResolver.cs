using System.Text.Json;

namespace Duckburg.Identity.Services;

public sealed record ResolvedProvider(
    string Issuer,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string UserinfoEndpoint,
    string? RevocationEndpoint,
    JwkSet SigningKeys,
    JsonElement OpenidProvider);

/// <summary>
/// Resolves an OpenID Provider's metadata via OpenID Connect Federation and validates
/// its trust chain up to the configured Trust Anchor:
///   OP entity config (self-signed) -> TA subordinate statement about OP -> TA entity config.
/// </summary>
public sealed class TrustChainResolver(HttpClient http, JwtService jwt, ILogger<TrustChainResolver> log)
{
    private const string WellKnown = ".well-known/openid-federation";

    public async Task<ResolvedProvider> ResolveAsync(string opEntityId, string trustAnchorId, CancellationToken ct)
    {
        var opEc = await GetEntityConfigurationAsync(opEntityId, ct);
        var opPayload = SelfVerify(opEc, opEntityId);

        await ValidateTrustChainAsync(opEntityId, trustAnchorId, opEc, ct);

        var op = opPayload.GetProperty("metadata").GetProperty("openid_provider");
        var signingKeys = JwkSet.Parse(op.GetProperty("jwks").GetProperty("keys"));

        return new ResolvedProvider(
            Issuer: op.GetProperty("issuer").GetString()!,
            AuthorizationEndpoint: op.GetProperty("authorization_endpoint").GetString()!,
            TokenEndpoint: op.GetProperty("token_endpoint").GetString()!,
            UserinfoEndpoint: op.GetProperty("userinfo_endpoint").GetString()!,
            RevocationEndpoint: op.TryGetProperty("revocation_endpoint", out var rev) ? rev.GetString() : null,
            SigningKeys: signingKeys,
            OpenidProvider: op.Clone());
    }

    private async Task ValidateTrustChainAsync(string opEntityId, string trustAnchorId, string opEc, CancellationToken ct)
    {
        try
        {
            var taEc = await GetEntityConfigurationAsync(trustAnchorId, ct);
            var taPayload = SelfVerify(taEc, trustAnchorId);
            var fetchEndpoint = taPayload
                .GetProperty("metadata").GetProperty("federation_entity")
                .GetProperty("federation_fetch_endpoint").GetString()!;

            var url = $"{fetchEndpoint}?sub={Uri.EscapeDataString(opEntityId)}";
            var subStmt = await http.GetStringAsync(url, ct);
            var subKid = JwtService.ReadKid(subStmt);
            var taKeys = JwkSet.Parse(taPayload.GetProperty("jwks").GetProperty("keys"));
            jwt.Verify(subStmt, taKeys.ByKid(subKid) ?? throw new InvalidOperationException("TA kid not found"));

            var subPayload = JwtService.ReadPayload(subStmt);
            var attested = JwkSet.Parse(subPayload.GetProperty("jwks").GetProperty("keys"));
            var opKid = JwtService.ReadKid(opEc);
            jwt.Verify(opEc, attested.ByKid(opKid)
                ?? throw new InvalidOperationException("OP key not attested by Trust Anchor"));

            log.LogInformation("Trust chain validated: {Op} <- {Ta}", opEntityId, trustAnchorId);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Trust chain validation failed for {Op}", opEntityId);
            throw new InvalidOperationException($"Trust chain validation failed: {ex.Message}", ex);
        }
    }

    private async Task<string> GetEntityConfigurationAsync(string entityId, CancellationToken ct)
    {
        var baseUrl = entityId.EndsWith('/') ? entityId : entityId + "/";
        return await http.GetStringAsync(baseUrl + WellKnown, ct);
    }

    private JsonElement SelfVerify(string ec, string expectedSub)
    {
        var kid = JwtService.ReadKid(ec);
        var payload = JwtService.ReadPayload(ec);
        var keys = JwkSet.Parse(payload.GetProperty("jwks").GetProperty("keys"));
        var key = keys.ByKid(kid) ?? throw new InvalidOperationException($"kid {kid} not in entity jwks");
        var verified = jwt.Verify(ec, key);
        if (verified.GetProperty("sub").GetString() != expectedSub)
            throw new InvalidOperationException($"entity config sub mismatch for {expectedSub}");
        return verified;
    }
}

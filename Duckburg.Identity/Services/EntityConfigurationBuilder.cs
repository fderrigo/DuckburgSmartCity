using System.Text.Json;

namespace Duckburg.Identity.Services;

/// <summary>
/// Builds and signs the RP's federation Entity Configuration JWT, served at
/// {entity_id}/.well-known/openid-federation. Signed with the federation key.
/// </summary>
public sealed class EntityConfigurationBuilder(RpConfig rp, JwtService jwt)
{
    private const int DefaultExp = 2880 * 60; // FEDERATION_DEFAULT_EXP minutes -> seconds

    public string Build()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new Dictionary<string, object>
        {
            ["iss"] = rp.Sub,
            ["sub"] = rp.Sub,
            ["iat"] = now,
            ["exp"] = now + DefaultExp,
            ["jwks"] = rp.JwksFed.ToPublicJwks(),
            ["metadata"] = JsonToObject(rp.Metadata),
            ["authority_hints"] = rp.AuthorityHints,
            ["trust_marks"] = JsonToObject(rp.TrustMarks)
        };
        return jwt.Sign(payload, rp.FedSigningKey, typ: "entity-statement+jwt");
    }

    private static object JsonToObject(JsonElement el) =>
        JsonSerializer.Deserialize<object>(el.GetRawText())!;
}

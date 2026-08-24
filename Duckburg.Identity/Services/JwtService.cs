using System.Text.Json;
using Jose;

namespace Duckburg.Identity.Services;

/// <summary>
/// RS256 JWS sign/verify and RSA-OAEP JWE decrypt over jose-jwt — the primitives
/// SPID/CIE OIDC needs. Mirrors spid_cie_oidc.entity.jwtse.
/// </summary>
public sealed class JwtService
{
    public string Sign(object payload, Jwk signingKey, string typ = "JWT")
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts.Default);
        var headers = new Dictionary<string, object>
        {
            ["kid"] = signingKey.Kid ?? throw new InvalidOperationException("signing key has no kid"),
            ["typ"] = typ
        };
        using var rsa = signingKey.ToRsa();
        return JWT.Encode(json, rsa, JwsAlgorithm.RS256, extraHeaders: headers);
    }

    public JsonElement Verify(string jws, Jwk publicKey)
    {
        using var rsa = publicKey.ToRsa();
        var payload = JWT.Decode(jws, rsa, JwsAlgorithm.RS256);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    public string DecryptJwe(string jwe, Jwk privateEncKey)
    {
        var header = ReadHeader(jwe);
        var enc = header.TryGetProperty("enc", out var e) ? e.GetString() : "A128CBC-HS256";
        using var rsa = privateEncKey.ToRsa();
        var jweEnc = enc switch
        {
            "A128CBC-HS256" => JweEncryption.A128CBC_HS256,
            "A192CBC-HS384" => JweEncryption.A192CBC_HS384,
            "A256CBC-HS512" => JweEncryption.A256CBC_HS512,
            "A128GCM" => JweEncryption.A128GCM,
            "A256GCM" => JweEncryption.A256GCM,
            _ => JweEncryption.A128CBC_HS256
        };
        return JWT.Decode(jwe, rsa, JweAlgorithm.RSA_OAEP, jweEnc);
    }

    public static JsonElement ReadHeader(string jwt) => DecodePart(jwt, 0);
    public static JsonElement ReadPayload(string jwt) => DecodePart(jwt, 1);

    private static JsonElement DecodePart(string jwt, int index)
    {
        var part = jwt.Split('.')[index];
        part = part.Replace('-', '+').Replace('_', '/');
        switch (part.Length % 4) { case 2: part += "=="; break; case 3: part += "="; break; }
        var bytes = Convert.FromBase64String(part);
        return JsonDocument.Parse(bytes).RootElement.Clone();
    }

    public static string ReadKid(string jwt)
    {
        var h = ReadHeader(jwt);
        return h.TryGetProperty("kid", out var k) ? k.GetString() ?? "" : "";
    }
}

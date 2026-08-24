using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duckburg.Identity.Services;

/// <summary>
/// JSON Web Key (RSA). Carries the raw base64url components plus the original
/// JSON so we can re-publish a public JWK byte-for-byte.
/// </summary>
public sealed class Jwk
{
    [JsonPropertyName("kty")] public string Kty { get; set; } = "RSA";
    [JsonPropertyName("use")] public string? Use { get; set; }
    [JsonPropertyName("alg")] public string? Alg { get; set; }
    [JsonPropertyName("kid")] public string? Kid { get; set; }

    [JsonPropertyName("n")] public string? N { get; set; }
    [JsonPropertyName("e")] public string? E { get; set; }
    [JsonPropertyName("d")] public string? D { get; set; }
    [JsonPropertyName("p")] public string? P { get; set; }
    [JsonPropertyName("q")] public string? Q { get; set; }
    [JsonPropertyName("dp")] public string? Dp { get; set; }
    [JsonPropertyName("dq")] public string? Dq { get; set; }
    [JsonPropertyName("qi")] public string? Qi { get; set; }

    private static byte[] B64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }

    public bool HasPrivate => !string.IsNullOrEmpty(D);

    private static BigInteger ToBig(byte[] b) => new(b, isUnsigned: true, isBigEndian: true);

    // Big-endian, left-padded to exactly `len` bytes (RSAParameters is length-strict).
    private static byte[] Fixed(BigInteger v, int len)
    {
        var raw = v.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length == len) return raw;
        if (raw.Length > len) return raw[(raw.Length - len)..];
        var padded = new byte[len];
        Array.Copy(raw, 0, padded, len - raw.Length, raw.Length);
        return padded;
    }

    /// <summary>Build an RSA instance from this JWK (private if components present, else public).</summary>
    public RSA ToRsa()
    {
        if (N is null || E is null)
            throw new InvalidOperationException("JWK missing RSA modulus/exponent");

        var modulus = B64Url(N);
        var p = new RSAParameters { Modulus = modulus, Exponent = B64Url(E) };

        if (HasPrivate)
        {
            int half = modulus.Length / 2;
            var d = ToBig(B64Url(D!));
            var pBig = ToBig(B64Url(P ?? throw new InvalidOperationException("JWK missing p")));
            var qBig = ToBig(B64Url(Q ?? throw new InvalidOperationException("JWK missing q")));
            var dp = Dp is not null ? ToBig(B64Url(Dp)) : d % (pBig - 1);
            var dq = Dq is not null ? ToBig(B64Url(Dq)) : d % (qBig - 1);
            var qi = Qi is not null ? ToBig(B64Url(Qi)) : ModInverse(qBig, pBig);

            p.D = Fixed(d, modulus.Length);
            p.P = Fixed(pBig, half);
            p.Q = Fixed(qBig, half);
            p.DP = Fixed(dp, half);
            p.DQ = Fixed(dq, half);
            p.InverseQ = Fixed(qi, half);
        }
        var rsa = RSA.Create();
        rsa.ImportParameters(p);
        return rsa;
    }

    // Modular inverse via extended Euclidean algorithm (a^-1 mod n).
    private static BigInteger ModInverse(BigInteger a, BigInteger n)
    {
        BigInteger t = 0, newt = 1, r = n, newr = a % n;
        while (newr != 0)
        {
            var q = r / newr;
            (t, newt) = (newt, t - q * newt);
            (r, newr) = (newr, r - q * newr);
        }
        if (t < 0) t += n;
        return t;
    }

    public Dictionary<string, object> ToPublicJson()
    {
        var d = new Dictionary<string, object> { ["kty"] = "RSA", ["n"] = N!, ["e"] = E! };
        if (Use is not null) d["use"] = Use;
        if (Alg is not null) d["alg"] = Alg;
        if (Kid is not null) d["kid"] = Kid;
        return d;
    }
}

public sealed class JwkSet
{
    [JsonPropertyName("keys")] public List<Jwk> Keys { get; set; } = new();

    public Jwk? BySig() => Keys.FirstOrDefault(k => k.Use == "sig") ?? Keys.FirstOrDefault();
    public Jwk? ByEnc() => Keys.FirstOrDefault(k => k.Use == "enc");
    public Jwk? ByKid(string kid) => Keys.FirstOrDefault(k => k.Kid == kid);

    public object ToPublicJwks() => new { keys = Keys.Select(k => k.ToPublicJson()).ToArray() };

    public static JwkSet Parse(JsonElement keysArray)
    {
        var set = new JwkSet();
        foreach (var el in keysArray.EnumerateArray())
            set.Keys.Add(el.Deserialize<Jwk>(JsonOpts.Default)!);
        return set;
    }
}

internal static class JsonOpts
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

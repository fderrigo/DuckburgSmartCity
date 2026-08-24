using System.Text.Json;

namespace Duckburg.Identity.Services;

/// <summary>
/// Relying Party identity. Split for production secret hygiene:
///   - PUBLIC part (sub, authority_hints, metadata with public JWKS, trust marks) shipped
///     in the image via rp_public.json.
///   - PRIVATE keys (federation + core, with the RSA private params) loaded ONLY from a
///     secret source: a mounted file referenced by Rp:PrivateKeysFile, or the configuration
///     key Rp:PrivateKeys (env Rp__PrivateKeys / Azure Key Vault). Never committed.
/// </summary>
public sealed class RpConfig
{
    public required string Sub { get; init; }
    public required string[] AuthorityHints { get; init; }
    public required string DefaultSignatureAlg { get; init; }
    public required JwkSet JwksFed { get; init; }
    public required JwkSet JwksCore { get; init; }
    public required JsonElement TrustMarks { get; init; }
    public required JsonElement Metadata { get; init; }

    public Jwk FedSigningKey => JwksFed.BySig() ?? JwksFed.Keys[0];
    public Jwk CoreSigningKey => JwksCore.BySig() ?? JwksCore.Keys[0];
    public Jwk CoreEncKey => JwksCore.ByEnc()
        ?? throw new InvalidOperationException("core jwks has no enc key");

    public JsonElement RpMetadata => Metadata.GetProperty("openid_relying_party");
    public string[] RedirectUris =>
        RpMetadata.GetProperty("redirect_uris").EnumerateArray().Select(x => x.GetString()!).ToArray();

    public static RpConfig Load(IConfiguration cfg, IWebHostEnvironment env)
    {
        // --- public identity ---
        var publicFile = cfg["Rp:PublicConfigFile"] ?? "rp_public.json";
        var publicPath = Path.IsPathRooted(publicFile) ? publicFile : Path.Combine(env.ContentRootPath, publicFile);
        if (!File.Exists(publicPath))
            throw new FileNotFoundException($"Public RP config not found: {publicPath}");
        using var pub = JsonDocument.Parse(File.ReadAllText(publicPath));
        var root = pub.RootElement;

        // --- private keys (secret) ---
        var keysJson = ResolvePrivateKeys(cfg, env);
        using var keys = JsonDocument.Parse(keysJson);
        var k = keys.RootElement;

        return new RpConfig
        {
            Sub = root.GetProperty("sub").GetString()!,
            AuthorityHints = root.GetProperty("authority_hints").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            DefaultSignatureAlg = root.GetProperty("default_signature_alg").GetString()!,
            TrustMarks = root.GetProperty("trust_marks").Clone(),
            Metadata = root.GetProperty("metadata").Clone(),
            JwksFed = JwkSet.Parse(k.GetProperty("jwks_fed").GetProperty("keys")),
            JwksCore = JwkSet.Parse(k.GetProperty("jwks_core").GetProperty("keys"))
        };
    }

    // Secret resolution order: inline config value > mounted file > hard failure.
    private static string ResolvePrivateKeys(IConfiguration cfg, IWebHostEnvironment env)
    {
        var file = cfg["Rp:PrivateKeysFile"];
        if (!string.IsNullOrWhiteSpace(file))
        {
            var path = Path.IsPathRooted(file) ? file : Path.Combine(env.ContentRootPath, file);
            if (File.Exists(path)) return File.ReadAllText(path);
            throw new FileNotFoundException($"Rp:PrivateKeysFile not found: {path}");
        }

        var inline = cfg["Rp:PrivateKeys"];
        if (!string.IsNullOrWhiteSpace(inline))
            return inline; // from env Rp__PrivateKeys or Azure Key Vault

        throw new InvalidOperationException(
            "RP private keys not configured. Provide Rp:PrivateKeysFile (mounted secret) " +
            "or Rp:PrivateKeys (env Rp__PrivateKeys / Key Vault). See README \"Secret\".");
    }
}

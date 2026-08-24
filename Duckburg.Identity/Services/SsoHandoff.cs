using System.Text.Json;

namespace Duckburg.Identity.Services;

/// <summary>
/// SSO verso i portali del Comune (es. Duckburg.ServiziOnline): dopo il login SPID/CIE
/// l'utente torna al portale con un JWT firmato dalla chiave core del RP. Il portale
/// verifica la firma tramite l'endpoint pubblico jwks.json e crea la propria sessione.
/// </summary>
public sealed class SsoHandoff(RpConfig rp, JwtService jwt, IConfiguration cfg)
{
    private const string FiscalNumberClaim = "https://attributes.eid.gov.it/fiscal_number";

    public bool IsAllowedReturnUrl(string url) =>
        Matches(url, cfg.GetSection("Sso:AllowedReturnUrls").Get<string[]>());

    public bool IsAllowedPostLogoutUrl(string url) =>
        Matches(url, cfg.GetSection("Sso:AllowedPostLogoutUrls").Get<string[]>());

    private static bool Matches(string url, string[]? allowed) =>
        allowed is not null && allowed.Any(a => string.Equals(a, url, StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds the redirect URL back to the portal with the signed handoff token.</summary>
    public string BuildRedirect(string returnUrl, AuthSession session)
    {
        var p = session.Profile ?? throw new InvalidOperationException("session has no user profile");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var lifetime = cfg.GetValue("Sso:TokenLifetimeSeconds", 120);
        var claims = new Dictionary<string, object?>
        {
            ["iss"] = rp.Sub,
            ["aud"] = cfg["Sso:TokenAudience"] ?? "duckburg-servizionline",
            ["iat"] = now,
            ["exp"] = now + lifetime,
            ["jti"] = Guid.NewGuid().ToString(),
            ["sub"] = p.Sub,
            ["given_name"] = p.GivenName,
            ["family_name"] = p.FamilyName,
            ["email"] = p.Email,
            ["fiscal_number"] = p.FiscalNumber
        };
        var token = jwt.Sign(claims.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!), rp.CoreSigningKey);
        var sep = returnUrl.Contains('?') ? "&" : "?";
        return $"{returnUrl}{sep}token={Uri.EscapeDataString(token)}";
    }
}

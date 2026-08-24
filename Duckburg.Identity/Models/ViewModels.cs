using Duckburg.Identity.Services;

namespace Duckburg.Identity.Models;

/// <summary>An OpenID Provider button binds from Oidc:ProductionProviders / Oidc:LocalProviders.</summary>
public sealed class ProviderOption
{
    public string Profile { get; set; } = "";   // "spid" | "cie"
    public string Name { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? Logo { get; set; }            // official IdP logo file name (SPID)
}

public sealed record ProviderEntry(string Profile, string Name, string EntityId, bool Local, string? Logo);

public sealed record LandingViewModel(
    string ClientId,
    IReadOnlyList<ProviderEntry> Spid,
    IReadOnlyList<ProviderEntry> Cie,
    string? ReturnUrl = null)
{
    /// <summary>Query-string suffix to propagate the SSO return_url on the login buttons.</summary>
    public string ReturnSuffix =>
        string.IsNullOrEmpty(ReturnUrl) ? "" : "&return_url=" + Uri.EscapeDataString(ReturnUrl);
}

public sealed record ProfileViewModel(
    UserProfile Profile,
    string? AccessToken,
    long AccessTokenExp,
    string? RefreshToken,
    long RefreshTokenExp,
    bool Refreshed)
{
    public bool HasRefreshToken => !string.IsNullOrEmpty(RefreshToken);

    public static string Short(string? jwt) =>
        string.IsNullOrEmpty(jwt) ? "—" : jwt.Length <= 24 ? jwt : jwt[..12] + "…" + jwt[^8..];

    public static string Ttl(long exp)
    {
        if (exp <= 0) return "n/d";
        var s = exp - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return s <= 0 ? "scaduto" : $"{s}s";
    }
}

public sealed record ErrorViewModel(string Title, string Detail);

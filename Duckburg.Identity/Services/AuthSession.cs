using System.Collections.Concurrent;

namespace Duckburg.Identity.Services;

/// <summary>One in-flight / completed authorization, keyed by state.</summary>
public sealed class AuthSession
{
    public required string State { get; init; }
    public required string Nonce { get; init; }
    public required string CodeVerifier { get; init; }
    public required string RedirectUri { get; init; }
    public required string ProviderId { get; init; }
    public required ResolvedProvider Provider { get; init; }
    /// <summary>Whitelisted URL of the portal to hand the user back to after login (SSO).</summary>
    public string? ReturnUrl { get; init; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public UserProfile? Profile { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? IdToken { get; set; }
    public long AccessTokenExp { get; set; }
    public long RefreshTokenExp { get; set; }
}

public sealed record UserProfile(
    string? GivenName,
    string? FamilyName,
    string? Email,
    string? FiscalNumber,
    string Sub,
    IReadOnlyDictionary<string, string> Raw);

/// <summary>
/// In-memory session store. For multi-instance production use a distributed cache
/// (e.g. Redis via IDistributedCache) instead — see README.
/// </summary>
public sealed class AuthSessionStore
{
    private readonly ConcurrentDictionary<string, AuthSession> _byState = new();

    public void Save(AuthSession s) => _byState[s.State] = s;
    public AuthSession? Get(string state) => _byState.TryGetValue(state, out var s) ? s : null;
    public void Remove(string state) => _byState.TryRemove(state, out _);
}

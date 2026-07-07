using System.Collections.Concurrent;

namespace ChattyDuck.Models;

public sealed record UsageLimits(
    int? RequestsPerMinute,
    long? InputTokensPerMinute,
    long? OutputTokensPerMinute,
    int? RequestsPerDay);

/// <summary>
/// Stato rate-limit riportato dal provider (header anthropic-ratelimit-* sulle risposte):
/// valori autoritativi, non stime locali.
/// </summary>
public sealed record ProviderRateStatus(
    long? RequestsLimit,
    long? RequestsRemaining,
    long? InputTokensLimit,
    long? InputTokensRemaining,
    long? OutputTokensLimit,
    long? OutputTokensRemaining,
    DateTimeOffset RetrievedAt);

public sealed record UsageSnapshot(
    string Model,
    int RequestsLastMinute,
    long InputTokensLastMinute,
    long OutputTokensLastMinute,
    int RequestsToday,
    UsageLimits Limits,
    ProviderRateStatus? Provider);

/// <summary>
/// Contatore in-memory dell'uso dei modelli: finestra mobile di 60 secondi piu'
/// totale giornaliero (UTC). Si azzera al riavvio dell'applicazione — indicativo,
/// non e' la contabilita' ufficiale dei provider.
/// </summary>
public sealed class ModelUsageTracker
{
    private sealed record Entry(DateTimeOffset At, long InputTokens, long OutputTokens);

    // Limiti noti: Claude Haiku 4.5 tier Scale (dagli header reali dell'account),
    // Gemini 2.5 Flash free tier (Google non li pubblica piu': indicativi, verifica in AI Studio).
    private static readonly Dictionary<string, UsageLimits> LimitiNoti = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = new(10_000, 10_000_000, 2_000_000, null),
        ["gemini"] = new(10, 250_000, null, 250),
    };

    private readonly ConcurrentDictionary<string, ConcurrentQueue<Entry>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (DateOnly Day, int Count)> _daily = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProviderRateStatus> _provider = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registra lo stato rate-limit riportato dal provider (dato reale).</summary>
    public void RecordProvider(string model, ProviderRateStatus status) => _provider[model] = status;

    public void Record(string model, long inputTokens, long outputTokens)
    {
        _entries.GetOrAdd(model, _ => new ConcurrentQueue<Entry>())
                .Enqueue(new Entry(DateTimeOffset.UtcNow, inputTokens, outputTokens));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _daily.AddOrUpdate(model, (today, 1),
            (_, cur) => cur.Day == today ? (today, cur.Count + 1) : (today, 1));
    }

    public UsageSnapshot Snapshot(string model)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
        var queue = _entries.GetOrAdd(model, _ => new ConcurrentQueue<Entry>());
        while (queue.TryPeek(out var oldest) && oldest.At < cutoff)
            queue.TryDequeue(out _);

        var recent = queue.Where(e => e.At >= cutoff).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daily = _daily.TryGetValue(model, out var d) && d.Day == today ? d.Count : 0;
        var limits = LimitiNoti.TryGetValue(model, out var l) ? l : new UsageLimits(null, null, null, null);

        _provider.TryGetValue(model, out var provider);

        return new UsageSnapshot(model, recent.Count,
            recent.Sum(e => e.InputTokens), recent.Sum(e => e.OutputTokens), daily, limits, provider);
    }
}

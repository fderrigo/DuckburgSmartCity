namespace ChattyDuck.Models;

/// <summary>
/// Intercetta le risposte HTTP verso l'API Anthropic e registra nel tracker gli header
/// anthropic-ratelimit-* (limite, rimanenti): sono i valori reali lato provider.
/// </summary>
public sealed class AnthropicRateLimitHandler(ModelUsageTracker tracker) : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);

        long? Header(string name) =>
            response.Headers.TryGetValues(name, out var values) && long.TryParse(values.FirstOrDefault(), out var n)
                ? n : null;

        if (response.Headers.Contains("anthropic-ratelimit-requests-limit"))
        {
            tracker.RecordProvider("claude", new ProviderRateStatus(
                RequestsLimit: Header("anthropic-ratelimit-requests-limit"),
                RequestsRemaining: Header("anthropic-ratelimit-requests-remaining"),
                InputTokensLimit: Header("anthropic-ratelimit-input-tokens-limit"),
                InputTokensRemaining: Header("anthropic-ratelimit-input-tokens-remaining"),
                OutputTokensLimit: Header("anthropic-ratelimit-output-tokens-limit"),
                OutputTokensRemaining: Header("anthropic-ratelimit-output-tokens-remaining"),
                RetrievedAt: DateTimeOffset.UtcNow));
        }

        return response;
    }
}

namespace Duckburg.Registry.Corpus;

/// <summary>
/// Riallinea periodicamente il corpus alle sue sorgenti (<c>Corpus:RefreshMinutes</c>,
/// 0 per disattivare). Serve a far arrivare all'assistente cio' che la redazione
/// pubblica nel CMS senza riavviare il server MCP.
/// Il ricarico manuale resta disponibile su <c>POST /corpus/reload</c>.
/// </summary>
public sealed class CorpusRefreshService : BackgroundService
{
    private readonly CorpusService _corpus;
    private readonly ILogger<CorpusRefreshService> _logger;
    private readonly TimeSpan _intervallo;

    public CorpusRefreshService(CorpusService corpus, IConfiguration configuration, ILogger<CorpusRefreshService> logger)
    {
        _corpus = corpus;
        _logger = logger;
        var minuti = configuration.GetValue<int?>("Corpus:RefreshMinutes") ?? 0;
        _intervallo = minuti > 0 ? TimeSpan.FromMinutes(minuti) : TimeSpan.Zero;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_intervallo == TimeSpan.Zero)
        {
            _logger.LogInformation("Aggiornamento periodico del corpus disattivato (Corpus:RefreshMinutes = 0).");
            return;
        }

        _logger.LogInformation("Aggiornamento periodico del corpus ogni {Minuti} minuti.", _intervallo.TotalMinutes);
        using var timer = new PeriodicTimer(_intervallo);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _corpus.ReloadAsync(obbligatorio: false, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un aggiornamento fallito non deve spegnere il servizio: si riprova al giro dopo.
                _logger.LogError(ex, "Aggiornamento del corpus fallito.");
            }
        }
    }
}

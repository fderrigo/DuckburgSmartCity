using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Duckburg.Valutazione;

public enum ScanStatus { InCorso, Completata, Errore }

/// <summary>Una scansione dell'App di valutazione (processo Node del validatore ufficiale).</summary>
public sealed class ScanJob
{
    public required string Id { get; init; }
    public required string Website { get; init; }
    public required string Accuracy { get; init; }
    public DateTime StartedAt { get; } = DateTime.Now;
    public ScanStatus Status { get; set; } = ScanStatus.InCorso;
    public StringBuilder Log { get; } = new();
    public string? ReportFile { get; set; }
}

/// <summary>
/// Esegue il validatore ufficiale pa-website-validator-ng (italia/GitHub) come processo
/// Node e ne raccoglie log e report HTML.
/// </summary>
public sealed class ScanService
{
    private readonly ConcurrentDictionary<string, ScanJob> _jobs = new();
    private readonly IConfiguration _config;
    private readonly ILogger<ScanService> _logger;
    private readonly string _contentRoot;

    public ScanService(IConfiguration config, ILogger<ScanService> logger, IWebHostEnvironment env)
    {
        _config = config;
        _logger = logger;
        _contentRoot = env.ContentRootPath;
    }

    /// <summary>I percorsi relativi in configurazione sono risolti rispetto alla cartella del progetto.</summary>
    private string Resolve(string relative) =>
        Path.IsPathRooted(relative) ? relative : Path.GetFullPath(Path.Combine(_contentRoot, relative));

    public string ToolDir => Resolve(_config["Valutazione:ToolDir"] ?? Path.Combine("tool", "pa-website-validator-ng"));

    public string ReportsDir
    {
        get
        {
            var dir = Resolve(_config["Valutazione:ReportsDir"] ?? "reports");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public bool ToolInstallato => File.Exists(Path.Combine(ToolDir, "dist", "index.js"));

    public ScanJob? Get(string id) => _jobs.TryGetValue(id, out var j) ? j : null;

    public IEnumerable<ScanJob> JobsAttivi => _jobs.Values.OrderByDescending(j => j.StartedAt);

    /// <summary>Report completati presenti su disco (nome cartella, file html, data).</summary>
    public List<(string Nome, string File, DateTime Data)> ReportSalvati()
    {
        var list = new List<(string, string, DateTime)>();
        foreach (var dir in Directory.EnumerateDirectories(ReportsDir))
        {
            var html = Directory.EnumerateFiles(dir, "*.html").FirstOrDefault();
            if (html != null)
                list.Add((Path.GetFileName(dir), Path.GetFileName(html), File.GetLastWriteTime(html)));
        }
        return list.OrderByDescending(x => x.Item3).ToList();
    }

    public ScanJob Start(string website, string accuracy)
    {
        var id = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var job = new ScanJob { Id = id, Website = website, Accuracy = accuracy };
        _jobs[id] = job;

        var dest = Path.Combine(ReportsDir, id);
        Directory.CreateDirectory(dest);

        var psi = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = ToolDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[]
        {
            "--max-old-space-size=8192", "dist",
            "--type", "municipality",
            "--destination", dest,
            "--report", "report",
            "--website", website,
            // "online" naviga l'intera alberatura (first/second level, eventi, prenotazioni);
            // "local" si limita a homepage e servizi.
            "--scope", "online",
            "--view", "false",
            "--accuracy", accuracy,
        }) psi.ArgumentList.Add(a);

        job.Log.AppendLine($"Avvio scansione di {website} (accuratezza: {accuracy})…");
        job.Log.AppendLine("La scansione usa Lighthouse e Puppeteer: possono servire diversi minuti.");

        _ = Task.Run(async () =>
        {
            try
            {
                using var proc = Process.Start(psi)!;
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (job.Log) job.Log.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (job.Log) job.Log.AppendLine(e.Data); };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                await proc.WaitForExitAsync();

                var html = Directory.EnumerateFiles(dest, "*.html").FirstOrDefault();
                if (proc.ExitCode == 0 || html != null)
                {
                    job.ReportFile = html != null ? Path.GetFileName(html) : null;
                    job.Status = html != null ? ScanStatus.Completata : ScanStatus.Errore;
                    lock (job.Log) job.Log.AppendLine(html != null
                        ? $"Scansione completata. Report: {job.ReportFile}"
                        : "Processo terminato ma nessun report trovato.");
                }
                else
                {
                    job.Status = ScanStatus.Errore;
                    lock (job.Log) job.Log.AppendLine($"Processo terminato con codice {proc.ExitCode}.");
                }
            }
            catch (Exception ex)
            {
                job.Status = ScanStatus.Errore;
                lock (job.Log) job.Log.AppendLine($"Errore: {ex.Message}");
                _logger.LogError(ex, "Errore scansione {Id}", id);
            }
        });

        return job;
    }
}

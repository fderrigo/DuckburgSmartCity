using System.Diagnostics;

// Helper di avvio multi-progetto (DuckburgSmartCity.slnLaunch): porta su la federazione
// SPID/CIE (Trust Anchor + OP CIE + Duckburg.Identity) in Docker prima/insieme agli altri
// servizi .NET. "docker compose up -d" e' idempotente: se e' gia' su non fa nulla.

var repoRoot = FindRepoRoot(AppContext.BaseDirectory)
    ?? throw new InvalidOperationException("docker-compose.yml non trovato risalendo da " + AppContext.BaseDirectory);

Console.WriteLine($"[Duckburg.DockerLaunch] Avvio federazione SPID/CIE (docker compose) in {repoRoot}...");

var psi = new ProcessStartInfo("docker", "compose up -d --build")
{
    WorkingDirectory = repoRoot,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false
};

try
{
    using var proc = Process.Start(psi)
        ?? throw new InvalidOperationException("impossibile avviare il processo docker");
    proc.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
    proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
    proc.BeginOutputReadLine();
    proc.BeginErrorReadLine();
    proc.WaitForExit();

    Console.WriteLine(proc.ExitCode == 0
        ? "[Duckburg.DockerLaunch] Federazione avviata (trust-anchor:8000, cie-provider:8002, identity.paperopoli.derrigo.it:8001)."
        : $"[Duckburg.DockerLaunch] docker compose ha restituito il codice {proc.ExitCode}. Controlla che Docker Desktop sia avviato.");
}
catch (Exception ex)
{
    // Non bloccare l'avvio degli altri progetti se Docker non è disponibile:
    // e' un helper di comodo, non un requisito per lavorare sul resto della soluzione.
    Console.WriteLine($"[Duckburg.DockerLaunch] Docker non disponibile ({ex.Message}). Salta l'avvio della federazione.");
}

static string? FindRepoRoot(string startDir)
{
    var dir = new DirectoryInfo(startDir);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}

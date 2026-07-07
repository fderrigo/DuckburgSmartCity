using ChattyDuck.Mcp;

namespace ChattyDuck.Models;

public sealed record ChatResult(string Reply, IReadOnlyList<SearchHit> Passages);

/// <summary>
/// Servizio modello intercambiabile: stessa interfaccia, dietro cambia solo il modello.
/// </summary>
public interface IModelService
{
    /// <summary>Nome del modello selezionabile dalla UI ("gemini" o "claude").</summary>
    string Name { get; }

    Task<ChatResult> AskAsync(string message, CancellationToken ct);
}

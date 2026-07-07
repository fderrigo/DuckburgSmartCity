using ChattyDuck.Models;

namespace ChattyDuck.Quack;

/// <summary>
/// Instrada il messaggio al servizio del modello scelto e ritorna risposta e passaggi citati.
/// </summary>
public sealed class ChatOrchestrator(IEnumerable<IModelService> models)
{
    private readonly Dictionary<string, IModelService> _models =
        models.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> AvailableModels => _models.Keys;

    public Task<ChatResult> AskAsync(string model, string message, CancellationToken ct)
    {
        if (!_models.TryGetValue(model, out var service))
            throw new ArgumentException($"Modello sconosciuto: '{model}'. Disponibili: {string.Join(", ", _models.Keys)}");
        return service.AskAsync(message, ct);
    }
}

using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class PrenotaAppuntamentoModel : PageModel
{
    private readonly ContentService _cms;

    public PrenotaAppuntamentoModel(ContentService cms) => _cms = cms;

    // Parametri scelti (querystring al primo passo, form al secondo).
    [BindProperty(SupportsGet = true)] public string? Ufficio { get; set; }
    [BindProperty(SupportsGet = true)] public string? Data { get; set; }
    [BindProperty(SupportsGet = true)] public string? Servizio { get; set; }

    [BindProperty] public string? Ora { get; set; }
    [BindProperty] public string Argomento { get; set; } = "";
    [BindProperty] public string Motivo { get; set; } = "";
    [BindProperty] public string Nome { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Telefono { get; set; } = "";

    public List<UnitaOrganizzativa> Uffici { get; private set; } = new();
    public UnitaOrganizzativa? UfficioScelto { get; private set; }
    public Servizio? ServizioContesto { get; private set; }
    public List<TimeOnly> Slot { get; private set; } = new();
    public DateOnly? DataScelta { get; private set; }
    public Appuntamento? Confermato { get; private set; }
    public string? Errore { get; private set; }

    public DateOnly PrimaDataUtile => ProssimoGiornoFeriale(DateOnly.FromDateTime(DateTime.Today).AddDays(1));

    private static DateOnly ProssimoGiornoFeriale(DateOnly d)
    {
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    private async Task CaricaContesto()
    {
        // Se si arriva da una scheda servizio, la scelta è circoscritta all'ufficio
        // competente e l'argomento è preselezionato con il titolo del servizio (C.SI.2.1).
        if (!string.IsNullOrEmpty(Servizio))
            ServizioContesto = await _cms.ServizioBySlug(Servizio);

        if (ServizioContesto?.UnitaOrganizzativa != null)
        {
            Uffici = new List<UnitaOrganizzativa> { ServizioContesto.UnitaOrganizzativa };
            Ufficio ??= ServizioContesto.UnitaOrganizzativa.Slug;
            if (string.IsNullOrEmpty(Argomento)) Argomento = ServizioContesto.Titolo;
        }
        else
        {
            Uffici = await _cms.UfficiPrenotabili();
        }

        if (!string.IsNullOrEmpty(Ufficio))
            UfficioScelto = Uffici.FirstOrDefault(u => u.Slug == Ufficio) ?? await _cms.UfficioBySlug(Ufficio);

        if (UfficioScelto != null && DateOnly.TryParse(Data, out var d))
        {
            d = ProssimoGiornoFeriale(d < PrimaDataUtile ? PrimaDataUtile : d);
            DataScelta = d;
            Slot = await _cms.SlotDisponibili(UfficioScelto.Id, d);
        }
    }

    public async Task OnGetAsync() => await CaricaContesto();

    public async Task<IActionResult> OnPostAsync()
    {
        await CaricaContesto();

        if (UfficioScelto == null || DataScelta == null || !TimeOnly.TryParse(Ora, out var ora) ||
            string.IsNullOrWhiteSpace(Argomento) || string.IsNullOrWhiteSpace(Nome) ||
            (string.IsNullOrWhiteSpace(Email) && string.IsNullOrWhiteSpace(Telefono)))
        {
            Errore = "Compila tutti i campi richiesti: ufficio, data, orario, argomento, nominativo e almeno un contatto.";
            return Page();
        }

        if (!Slot.Contains(ora))
        {
            Errore = "L'orario scelto non è più disponibile. Scegli un altro orario.";
            return Page();
        }

        // La prenotazione viene completata subito, senza attesa di conferma (C.SI.2.1).
        Confermato = await _cms.PrenotaAppuntamento(new Appuntamento
        {
            UfficioId = UfficioScelto.Id,
            Data = DataScelta.Value,
            Ora = ora,
            Argomento = Argomento ?? "",
            Motivo = Motivo ?? "",
            Nome = Nome ?? "",
            Email = Email ?? "",
            Telefono = Telefono ?? ""
        });
        return Page();
    }
}

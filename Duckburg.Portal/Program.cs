using ChattyDuck.Quack;
using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Portale del Comune: Razor Pages. L'assistente ChattyDuck e' montato come widget.
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Admin", "CmsAdmin");
    o.Conventions.AllowAnonymousToPage("/Admin/Login");
    o.Conventions.AllowAnonymousToPage("/Admin/Logout");
});
builder.Services.AddQuack();

// CMS: data layer con provider plug-and-play e contenuti di default Paperopoli.
builder.Services.AddPortalCms(builder.Configuration, builder.Environment.ContentRootPath);

// Autenticazione a cookie per l'area di amministrazione del CMS.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/admin/login";
        o.LogoutPath = "/admin/logout";
        o.AccessDeniedPath = "/admin/login";
        o.Cookie.Name = "paperopoli.cms.auth";
    });
builder.Services.AddAuthorization(o =>
    o.AddPolicy("CmsAdmin", p => p.RequireAuthenticatedUser()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseRouting();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// Canale chat e diagnostica dell'assistente.
app.MapQuackEndpoints();

// Raccolta delle valutazioni di chiarezza delle pagine (C.SI.2.5 / C.SI.2.6).
app.MapPost("/api/valutazione", async (ValutazioneDto dto, ContentService cms) =>
{
    if (dto.Voto is < 1 or > 5) return Results.BadRequest();
    await cms.SalvaValutazione(new Duckburg.Portal.Cms.ValutazionePagina
    {
        Url = dto.Url ?? "",
        TitoloPagina = dto.Titolo ?? "",
        Voto = dto.Voto,
        Risposte = dto.Risposte ?? new(),
        Commento = dto.Commento ?? ""
    });
    return Results.Ok();
});

// Crea lo schema e popola i contenuti di default al primo avvio.
await app.Services.InitializeCmsAsync();

app.Run();

/// <summary>Payload del widget di valutazione della chiarezza.</summary>
internal sealed record ValutazioneDto(string? Url, string? Titolo, int Voto, List<string>? Risposte, string? Commento);

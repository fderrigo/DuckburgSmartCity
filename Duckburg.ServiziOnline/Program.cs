using System.Security.Claims;
using ChattyDuck.Quack;
using Duckburg.ServiziOnline.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Portale dei servizi online: Razor Pages + assistente ChattyDuck come nel portale
// informativo. L'accesso avviene con SPID/CIE tramite Duckburg.Identity (SSO).
builder.Services.AddRazorPages(o => o.Conventions.AuthorizePage("/AreaPersonale"));
builder.Services.AddQuack();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<SsoTokenValidator>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/accedi";
        o.Cookie.Name = "duckburg_servizionline";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(1);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

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

var identityBaseUrl = app.Configuration["Sso:IdentityBaseUrl"] ?? "http://identity.paperopoli.test:8001";
var callbackUrl = app.Configuration["Sso:CallbackUrl"] ?? "http://localhost:5300/auth/callback";
var postLogoutUrl = app.Configuration["Sso:PostLogoutUrl"] ?? "http://localhost:5300/";

// Avvio del login: manda il cittadino alla pagina di accesso SPID/CIE di Duckburg.Identity.
app.MapGet("/accedi", () =>
    Results.Redirect($"{identityBaseUrl}/oidc/rp/landing?return_url={Uri.EscapeDataString(callbackUrl)}"));

// Ritorno dal login: Identity rimanda qui con il token firmato; verificato -> sessione cookie.
app.MapGet("/auth/callback", async (string? token, HttpContext http, SsoTokenValidator validator, CancellationToken ct) =>
{
    if (string.IsNullOrEmpty(token))
        return Results.Redirect("/accesso-negato");
    try
    {
        var principal = await validator.ValidateAsync(token, ct);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return Results.Redirect("/area-personale");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Token SSO non valido");
        return Results.Redirect("/accesso-negato");
    }
});

// Uscita: chiude la sessione locale e revoca i token su Identity.
app.MapGet("/esci", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect($"{identityBaseUrl}/oidc/rp/logout?return_url={Uri.EscapeDataString(postLogoutUrl)}");
});

app.Run();

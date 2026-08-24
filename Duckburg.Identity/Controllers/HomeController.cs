using Microsoft.AspNetCore.Mvc;
using Duckburg.Identity.Models;
using Duckburg.Identity.Services;

namespace Duckburg.Identity.Controllers;

public sealed class HomeController(RpConfig rp, SsoHandoff sso, IConfiguration cfg) : Controller
{
    [HttpGet("/")]
    public IActionResult Index() => RedirectToAction(nameof(Landing));

    [HttpGet("/oidc/rp/landing")]
    public IActionResult Landing(string? return_url)
    {
        // Demo Paperopoli: solo la federazione locale di test (Trust Anchor + OP CIE in
        // Docker). I provider di produzione restano in appsettings per un domani, ma non
        // si mostrano qui: un solo pulsante SPID e uno CIE, entrambi locali.
        var local = cfg.GetSection("Oidc:LocalProviders").Get<List<ProviderOption>>() ?? new();
        var all = local.Select(p => new ProviderEntry(p.Profile, p.Name, p.EntityId, true, p.Logo)).ToList();

        // SSO: il portale chiamante passa return_url; lo propaghiamo ai bottoni di login
        // solo se in whitelist (open-redirect protection).
        var returnUrl = !string.IsNullOrEmpty(return_url) && sso.IsAllowedReturnUrl(return_url)
            ? return_url : null;

        var model = new LandingViewModel(
            ClientId: rp.Sub,
            Spid: all.Where(p => p.Profile == "spid").ToList(),
            Cie: all.Where(p => p.Profile == "cie").ToList(),
            ReturnUrl: returnUrl);
        return View(model);
    }

    [HttpGet("/error")]
    public IActionResult Error() =>
        View(new ErrorViewModel("Errore interno", "Si è verificato un errore imprevisto."));
}

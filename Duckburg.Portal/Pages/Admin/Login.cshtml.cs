using System.Security.Claims;
using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Duckburg.Portal.Pages.Admin;

public class LoginModel : PageModel
{
    private readonly CmsOptions _opts;
    public LoginModel(IOptions<CmsOptions> opts) => _opts = opts.Value;

    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? Errore { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        var a = _opts.Admin;
        if (Username == a.Username && Password == a.Password)
        {
            var claims = new List<Claim> { new(ClaimTypes.Name, Username), new(ClaimTypes.Role, "CmsAdmin") };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/admin" : returnUrl);
        }
        Errore = "Credenziali non valide.";
        return Page();
    }
}

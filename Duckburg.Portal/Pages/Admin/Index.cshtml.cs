using Duckburg.Portal.Cms.Admin;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly AdminService _admin;
    public IndexModel(AdminService admin) => _admin = admin;

    public Dictionary<string, int> Counts { get; private set; } = new();
    public bool Protezione => _admin.ProtectDefaultContent;

    public void OnGet() => Counts = _admin.Counts();
}

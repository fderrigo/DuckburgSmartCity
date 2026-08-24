using Duckburg.Portal.Cms;

namespace Duckburg.Portal.Pages;

public class PrivacyModel : PaginaContenutoModel
{
    public PrivacyModel(ContentService cms) : base(cms) { }
    protected override string Slug => "privacy";
}

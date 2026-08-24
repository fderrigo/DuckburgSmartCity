using Duckburg.Portal.Cms;

namespace Duckburg.Portal.Pages;

public class NoteLegaliModel : PaginaContenutoModel
{
    public NoteLegaliModel(ContentService cms) : base(cms) { }
    protected override string Slug => "note-legali";
}

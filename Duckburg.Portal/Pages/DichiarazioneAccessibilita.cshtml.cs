using Duckburg.Portal.Cms;

namespace Duckburg.Portal.Pages;

public class DichiarazioneAccessibilitaModel : PaginaContenutoModel
{
    public DichiarazioneAccessibilitaModel(ContentService cms) : base(cms) { }
    protected override string Slug => "dichiarazione-accessibilita";
}

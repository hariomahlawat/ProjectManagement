using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Pages.Tools.Collage;

/// <summary>
/// Collage Studio (docs/ux/collage-studio.md). Users make collages of their own photos entirely in
/// the browser; the page has no handlers that accept files, and nothing a user makes is stored or
/// uploaded to PRISM. PRISM only sends pictures to it: the crests and logos, and photos from PRISM
/// Photos when that library is switched on (docs/ux/collage-studio-library-and-unit-stickers.md).
/// </summary>
[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly IOptions<MediaLibraryOptions> _library;

    public IndexModel(IOptions<MediaLibraryOptions> library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
    }

    /// <summary>Whether "From PRISM Photos" is offered: only when the Photos library is switched on.</summary>
    public bool LibraryOn { get; private set; }

    public void OnGet()
    {
        LibraryOn = _library.Value.IsCatalogueEnabled;
    }
}

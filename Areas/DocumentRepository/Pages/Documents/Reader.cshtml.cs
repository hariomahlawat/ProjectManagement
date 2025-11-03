using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.View")]
    public sealed class ReaderModel : PageModel
    {
        [FromRoute] public Guid Id { get; set; }

        [FromQuery] public int? Page { get; set; }
        [FromQuery] public string? Zoom { get; set; } // "page-width", "page-fit", or numeric ("125")

        public string IframeSrc { get; private set; } = string.Empty;
        public string DownloadUrl { get; private set; } = string.Empty;
        public string ViewUrl { get; private set; } = string.Empty;

        public void OnGet(Guid id, int? page, string? zoom)
        {
            Id = id;

            var pg = page is > 0 ? page.Value : 1;
            var zm = string.IsNullOrWhiteSpace(zoom) ? "page-width" : zoom;

            ViewUrl = Url.Page("./View", new { id }) ?? "#";
            DownloadUrl = Url.Page("./Download", new { id }) ?? "#";

            // Use browser PDF viewer fragments: page, zoom, and pagemode=thumbs for thumbnails
            IframeSrc = $"{ViewUrl}#page={pg}&zoom={zm}&pagemode=thumbs";
        }
    }
}

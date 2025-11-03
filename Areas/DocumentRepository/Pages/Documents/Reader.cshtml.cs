using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.View")]
    public sealed class ReaderModel : PageModel
    {
        [FromRoute] public Guid Id { get; set; }

        // avoid shadowing PageModel members
        [FromQuery] public int? PageNo { get; set; }
        [FromQuery] public string? Zoom { get; set; } // "page-width" | "page-fit" | "125" | "150"

        public string IframeSrc { get; private set; } = string.Empty;
        public string DownloadUrl { get; private set; } = string.Empty;
        public string ViewUrl { get; private set; } = string.Empty;

        public void OnGet(Guid id, int? pageNo, string? zoom)
        {
            Id = id;
            var pg = pageNo is > 0 ? pageNo.Value : 1;
            var zm = string.IsNullOrWhiteSpace(zoom) ? "page-width" : zoom;

            ViewUrl = Url.Page("./View", new { id }) ?? "#";
            DownloadUrl = Url.Page("./Download", new { id }) ?? "#";
            IframeSrc = $"{ViewUrl}#page={pg}&zoom={zm}&pagemode=thumbs";
        }
    }
}

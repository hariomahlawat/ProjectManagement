using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Options;
namespace ProjectManagement.Pages.Photos;
[Authorize(Roles="Admin,HoD")]
public sealed class FaceThumbnailModel : PageModel
{
    private readonly MediaLibraryDbContext _db; private readonly MediaLibraryOptions _options; private readonly IWebHostEnvironment _environment;
    public FaceThumbnailModel(MediaLibraryDbContext db,IOptions<MediaLibraryOptions> options,IWebHostEnvironment environment){_db=db;_options=options.Value;_environment=environment;}
    public async Task<IActionResult> OnGetAsync(Guid id,CancellationToken ct)
    {
        var relative=await _db.Faces.AsNoTracking().Where(x=>x.Id==id&&!x.IsSuppressed).Select(x=>x.ReviewThumbnailPath).SingleOrDefaultAsync(ct);
        if(string.IsNullOrWhiteSpace(relative)) return NotFound();
        var root=Path.GetFullPath(Path.IsPathRooted(_options.CacheRoot)?_options.CacheRoot:Path.Combine(_environment.ContentRootPath,_options.CacheRoot));
        var full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!System.IO.File.Exists(full)) return NotFound();
        return new PhysicalFileResult(full,"image/webp") { EnableRangeProcessing = false };
    }
}

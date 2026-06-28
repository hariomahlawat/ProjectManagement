using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
namespace ProjectManagement.Pages.Photos.People;
[Authorize(Roles="Admin,HoD")]
public sealed class DetailsModel : PageModel
{
    private readonly MediaLibraryDbContext _db; public DetailsModel(MediaLibraryDbContext db)=>_db=db;
    public string PersonName{get;private set;}=string.Empty; public IReadOnlyList<PhotoRow> Photos{get;private set;}=[];
    public async Task<IActionResult> OnGetAsync(Guid id,CancellationToken ct)
    {
        var person=await _db.Persons.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.Status==MediaPersonStatus.Confirmed&&!x.IsHidden,ct); if(person is null)return NotFound(); PersonName=person.DisplayName;
        Photos=await _db.PersonFaces.AsNoTracking().Where(x=>x.MediaPersonId==id&&x.RemovedAtUtc==null&&x.MediaFace.MediaAsset.IsAvailable&&!x.MediaFace.MediaAsset.IsDeleted&&!x.MediaFace.MediaAsset.IsArchived).Select(x=>new PhotoRow(x.MediaFace.MediaAssetId,x.MediaFace.MediaAsset.ContextTitle,x.MediaFace.MediaAsset.MediaDateUtc)).Distinct().OrderByDescending(x=>x.MediaDateUtc).ToListAsync(ct);return Page();
    }
    public sealed record PhotoRow(long AssetId,string ContextTitle,DateTimeOffset MediaDateUtc);
}

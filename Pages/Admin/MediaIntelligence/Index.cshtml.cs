using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;
namespace ProjectManagement.Pages.Admin.MediaIntelligence;
[Authorize(Roles="Admin,HoD")]
public sealed class IndexModel : PageModel
{
 private readonly MediaLibraryDbContext _db; private readonly IFaceModelReadinessService _readiness; private readonly IFaceQueueService _queue;
 public IndexModel(MediaLibraryDbContext db,IFaceModelReadinessService readiness,IFaceQueueService queue){_db=db;_readiness=readiness;_queue=queue;}
 public FaceModelReadiness? ModelStatus{get;private set;} public int Eligible{get;private set;} public int Faces{get;private set;} public int Embedded{get;private set;} public int Persons{get;private set;} public int PendingReview{get;private set;} [TempData] public string? StatusMessage{get;set;}
 public async Task OnGetAsync(CancellationToken ct){ModelStatus=await _readiness.CheckAsync(ct);Eligible=await _db.Assets.CountAsync(x=>x.IsAvailable&&!x.IsDeleted&&!x.IsArchived&&x.Kind==MediaAssetKind.Photo&&x.Classification==MediaClassification.Photograph,ct);Faces=await _db.Faces.CountAsync(ct);Embedded=await _db.FaceEmbeddings.CountAsync(x=>x.InvalidatedAtUtc==null,ct);Persons=await _db.Persons.CountAsync(x=>!x.IsHidden,ct);PendingReview=await _db.FaceReviewDecisions.CountAsync(x=>x.Decision==FaceReviewDecisionType.Pending,ct);}
 public async Task<IActionResult> OnPostQueueAsync(int limit=25,CancellationToken ct=default){var n=await _queue.QueueEligibleAsync(Math.Clamp(limit,1,250),ct);StatusMessage=$"Queued {n} photograph(s) for face analysis.";return RedirectToPage();}
 public async Task<IActionResult> OnPostQueueAssetAsync(long assetId,CancellationToken ct){StatusMessage=await _queue.QueueAssetAsync(assetId,ct)?"The selected photograph was queued.":"The photograph already has active face-processing work.";return RedirectToPage();}
}

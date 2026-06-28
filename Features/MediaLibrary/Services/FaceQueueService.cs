using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
namespace ProjectManagement.Features.MediaLibrary.Services;
public sealed class FaceQueueService : IFaceQueueService
{
    private readonly MediaLibraryDbContext _db; private readonly MediaLibraryOptions _options; private readonly IFaceModelReadinessService _readiness;
    public FaceQueueService(MediaLibraryDbContext db,IOptions<MediaLibraryOptions> options,IFaceModelReadinessService readiness){_db=db;_options=options.Value;_readiness=readiness;}
    public async Task<int> QueueEligibleAsync(int limit,CancellationToken ct){if(!_options.People.Enabled||!(await _readiness.CheckAsync(ct)).IsReady)return 0;var ids=await _db.Assets.Where(x=>x.IsAvailable&&!x.IsDeleted&&!x.IsArchived&&x.Kind==MediaAssetKind.Photo&&(!_options.People.ProcessPhotographsOnly||x.Classification==MediaClassification.Photograph)).OrderByDescending(x=>x.MediaDateUtc).Select(x=>x.Id).Take(Math.Clamp(limit,1,1000)).ToListAsync(ct);var count=0;foreach(var id in ids)if(await QueueAssetAsync(id,ct))count++;return count;}
    public async Task<bool> QueueAssetAsync(long assetId,CancellationToken ct){if(!_options.People.Enabled||!(await _readiness.CheckAsync(ct)).IsReady)return false;var existing=await _db.ProcessingJobs.SingleOrDefaultAsync(x=>x.MediaAssetId==assetId&&x.JobType==MediaProcessingJobType.DetectFaces,ct);var now=DateTimeOffset.UtcNow;if(existing is null){_db.ProcessingJobs.Add(new MediaProcessingJob{MediaAssetId=assetId,JobType=MediaProcessingJobType.DetectFaces,Status=MediaProcessingJobStatus.Pending,MaxAttempts=3,AvailableAfterUtc=now,CreatedAtUtc=now,UpdatedAtUtc=now});}else if(existing.Status is MediaProcessingJobStatus.Completed or MediaProcessingJobStatus.Failed or MediaProcessingJobStatus.DeadLetter){existing.Status=MediaProcessingJobStatus.Pending;existing.AttemptCount=0;existing.AvailableAfterUtc=now;existing.FailureCode=null;existing.FailureMessage=null;existing.UpdatedAtUtc=now;}else return false;await _db.SaveChangesAsync(ct);return true;}
}

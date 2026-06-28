using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
namespace ProjectManagement.Features.MediaLibrary.Services;
public sealed class FaceIntelligenceService : IFaceIntelligenceService
{
    private readonly MediaLibraryDbContext _db; private readonly IMediaContentProviderResolver _resolver; private readonly IFaceAnalysisEngine _engine; private readonly MediaLibraryOptions _options; private readonly IWebHostEnvironment _environment;
    public FaceIntelligenceService(MediaLibraryDbContext db,IMediaContentProviderResolver resolver,IFaceAnalysisEngine engine,IOptions<MediaLibraryOptions> options,IWebHostEnvironment environment){_db=db;_resolver=resolver;_engine=engine;_options=options.Value;_environment=environment;}
    public async Task ProcessAssetAsync(long assetId,CancellationToken ct)
    {
        if(!_options.People.Enabled) return; var asset=await _db.Assets.Include(x=>x.Source).SingleAsync(x=>x.Id==assetId,ct);
        if(!asset.IsAvailable||asset.IsDeleted||asset.IsArchived||asset.Kind!=MediaAssetKind.Photo) return;
        if(_options.People.ProcessPhotographsOnly && asset.Classification!=MediaClassification.Photograph) return;
        var content=await _resolver.ResolveAsync(asset,ct)??throw new MediaContentUnavailableException($"Media content is unavailable for face analysis of asset {asset.Id}.");
        byte[] bytes; await using(var s=await content.OpenReadAsync(ct)){using var ms=new MemoryStream();await s.CopyToAsync(ms,ct);bytes=ms.ToArray();}
        var detections=await _engine.AnalyseAsync(bytes,ct); var detector=_options.People.Detector; var embedder=_options.People.Embedder; var now=DateTimeOffset.UtcNow;
        var existing=await _db.Faces.Include(x=>x.Embeddings).Include(x=>x.PersonAssignments).Where(x=>x.MediaAssetId==assetId).ToListAsync(ct);
        if (existing.Any(x => x.PersonAssignments.Any(y => y.RemovedAtUtc == null))) return;
        _db.Faces.RemoveRange(existing);
        var seq=0;foreach(var d in detections){seq++;var faceId=Guid.NewGuid();var thumbnailPath=await SaveThumbnailAsync(faceId,d.ReviewThumbnail,ct);var face=new MediaFace{Id=faceId,MediaAssetId=assetId,SequenceNumber=seq,Left=d.Left,Top=d.Top,Width=d.Width,Height=d.Height,LandmarksJson=d.Landmarks is null?null:JsonSerializer.Serialize(d.Landmarks),DetectionConfidence=d.Confidence,QualityScore=d.QualityScore,QualityStatus=d.QualityStatus,DetectorModelKey=detector.Key,DetectorModelVersion=detector.Version,ReviewThumbnailPath=thumbnailPath,CreatedAtUtc=now,UpdatedAtUtc=now};if(d.Embedding is {Length:>0}) face.Embeddings.Add(new MediaFaceEmbedding{Embedding=d.Embedding,Dimension=d.Embedding.Length,ModelKey=embedder.Key,ModelVersion=embedder.Version,QualityScore=d.QualityScore,CreatedAtUtc=now});_db.Faces.Add(face);}
        await _db.SaveChangesAsync(ct); await CreateCandidatesAsync(assetId,ct);
    }
    private async Task CreateCandidatesAsync(long assetId,CancellationToken ct)
    {
        var newFaces=await _db.Faces.Include(x=>x.Embeddings).Where(x=>x.MediaAssetId==assetId&&!x.IsSuppressed).ToListAsync(ct);
        var confirmed=await _db.PersonFaces.Include(x=>x.MediaPerson).Include(x=>x.MediaFace).ThenInclude(x=>x.Embeddings).Where(x=>x.RemovedAtUtc==null&&x.AssignmentType==FaceAssignmentType.HumanConfirmed&&!x.MediaPerson.IsHidden).ToListAsync(ct);
        foreach(var face in newFaces){var e=face.Embeddings.FirstOrDefault(x=>x.InvalidatedAtUtc==null);if(e is null)continue;var best=confirmed.Select(x=>new{x.MediaPersonId,Similarity=Cosine(e.Embedding,x.MediaFace.Embeddings.FirstOrDefault(y=>y.InvalidatedAtUtc==null)?.Embedding)}).Where(x=>x.Similarity>=_options.People.CandidateSimilarityThreshold).OrderByDescending(x=>x.Similarity).FirstOrDefault();if(best is null)continue;if(!await _db.FaceReviewDecisions.AnyAsync(x=>x.MediaFaceId==face.Id&&x.CandidatePersonId==best.MediaPersonId&&x.Decision==FaceReviewDecisionType.Pending,ct))_db.FaceReviewDecisions.Add(new MediaFaceReviewDecision{MediaFaceId=face.Id,CandidatePersonId=best.MediaPersonId,Decision=FaceReviewDecisionType.Pending,Similarity=best.Similarity,CreatedAtUtc=DateTimeOffset.UtcNow});}
        await _db.SaveChangesAsync(ct);
    }
    private async Task<string?> SaveThumbnailAsync(Guid faceId, byte[]? bytes, CancellationToken ct){if(bytes is null||bytes.Length==0)return null;var root=Path.IsPathRooted(_options.CacheRoot)?_options.CacheRoot:Path.Combine(_environment.ContentRootPath,_options.CacheRoot);var relative=Path.Combine("faces",faceId.ToString("N")[..2],faceId.ToString("N")+".webp");var full=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(full)!);var temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";await File.WriteAllBytesAsync(temp,bytes,ct);File.Move(temp,full,true);return relative.Replace(Path.DirectorySeparatorChar,'/');}
    private static double Cosine(float[] a,float[]? b){if(b is null||a.Length!=b.Length)return -1;double dot=0,aa=0,bb=0;for(var i=0;i<a.Length;i++){dot+=a[i]*b[i];aa+=a[i]*a[i];bb+=b[i]*b[i];}return aa==0||bb==0?-1:dot/Math.Sqrt(aa*bb);}
}

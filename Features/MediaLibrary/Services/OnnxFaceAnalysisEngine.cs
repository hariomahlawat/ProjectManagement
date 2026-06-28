using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using SkiaSharp;
namespace ProjectManagement.Features.MediaLibrary.Services;
public sealed class OnnxFaceAnalysisEngine : IFaceAnalysisEngine, IDisposable
{
    private readonly MediaLibraryOptions _options; private readonly IWebHostEnvironment _environment; private readonly IFaceModelReadinessService _readiness; private readonly SemaphoreSlim _gate=new(1,1);
    private InferenceSession? _detector; private InferenceSession? _embedder;
    public OnnxFaceAnalysisEngine(IOptions<MediaLibraryOptions> options,IWebHostEnvironment environment,IFaceModelReadinessService readiness){_options=options.Value;_environment=environment;_readiness=readiness;}
    public async Task<IReadOnlyList<DetectedFaceData>> AnalyseAsync(byte[] imageBytes,CancellationToken ct)
    {
        var ready=await _readiness.CheckAsync(ct); if(!ready.IsReady) throw new InvalidOperationException(ready.Message); await EnsureSessionsAsync(ready,ct);
        using var data=SKData.CreateCopy(imageBytes); using var bitmap=SKBitmap.Decode(data)??throw new InvalidDataException("The image could not be decoded for face analysis.");
        var d=_options.People.Detector; using var resized=bitmap.Resize(new SKImageInfo(d.InputWidth,d.InputHeight),SKFilterQuality.Medium)??throw new InvalidDataException("The image could not be resized.");
        var tensor=ToTensor(resized,d.InputWidth,d.InputHeight); var input=NamedOnnxValue.CreateFromTensor(d.InputName,tensor); using var results=_detector!.Run(new[]{input});
        var boxes=results.FirstOrDefault(x=>x.Name==d.BoxesOutputName)?.AsTensor<float>()??throw new InvalidDataException($"Detector output '{d.BoxesOutputName}' was not found.");
        var scores=results.FirstOrDefault(x=>x.Name==d.ScoresOutputName)?.AsTensor<float>()??throw new InvalidDataException($"Detector output '{d.ScoresOutputName}' was not found.");
        var landmarks=results.FirstOrDefault(x=>x.Name==d.LandmarksOutputName)?.AsTensor<float>(); var count=boxes.Dimensions[boxes.Dimensions.Count - 2]; var list=new List<DetectedFaceData>();
        for(var i=0;i<count && list.Count<_options.People.MaximumFacesPerAsset;i++)
        {
            var score=ReadScore(scores,i); if(score<_options.People.MinimumDetectionConfidence) continue; var b=ReadRow(boxes,i,4); var x1=b[0];var y1=b[1];var x2=b[2];var y2=b[3];
            if(d.BoxesAreNormalized){x1*=bitmap.Width;x2*=bitmap.Width;y1*=bitmap.Height;y2*=bitmap.Height;} else {x1*=bitmap.Width/(float)d.InputWidth;x2*=bitmap.Width/(float)d.InputWidth;y1*=bitmap.Height/(float)d.InputHeight;y2*=bitmap.Height/(float)d.InputHeight;}
            var rect=ClampRect(x1,y1,x2,y2,bitmap.Width,bitmap.Height); if(rect.Width<_options.People.MinimumFacePixels||rect.Height<_options.People.MinimumFacePixels) continue;
            var quality=Quality(bitmap,rect); var status=quality>=_options.People.MinimumQualityScore?FaceQualityStatus.EmbeddingEligible:FaceQualityStatus.LowResolution;
            float[]? embedding=null; if(status==FaceQualityStatus.EmbeddingEligible) embedding=Embed(bitmap,rect);
            IReadOnlyList<double>? lm=landmarks is null?null:ReadRow(landmarks,i,10).Select(v=>(double)v).ToArray();
            var thumb=CreateReviewThumbnail(bitmap,rect);
            list.Add(new(rect.Left/(double)bitmap.Width,rect.Top/(double)bitmap.Height,rect.Width/(double)bitmap.Width,rect.Height/(double)bitmap.Height,score,quality,status,embedding,lm,thumb));
        }
        return list;
    }
    private async Task EnsureSessionsAsync(FaceModelReadiness r,CancellationToken ct){if(_detector is not null&&_embedder is not null)return;await _gate.WaitAsync(ct);try{_detector??=new InferenceSession(r.DetectorPath!);_embedder??=new InferenceSession(r.EmbedderPath!);}finally{_gate.Release();}}
    private float[] Embed(SKBitmap source,SKRectI rect){using var crop=new SKBitmap(rect.Width,rect.Height);using(var canvas=new SKCanvas(crop)){canvas.DrawBitmap(source,new SKRect(rect.Left,rect.Top,rect.Right,rect.Bottom),new SKRect(0,0,rect.Width,rect.Height));}var e=_options.People.Embedder;using var resized=crop.Resize(new SKImageInfo(e.InputWidth,e.InputHeight),SKFilterQuality.High)??throw new InvalidDataException("Face crop resize failed.");var tensor=ToTensor(resized,e.InputWidth,e.InputHeight);var input=NamedOnnxValue.CreateFromTensor(e.InputName,tensor);using var results=_embedder!.Run(new[]{input});var output=results.FirstOrDefault(x=>x.Name==e.EmbeddingOutputName)?.AsTensor<float>()??results.First().AsTensor<float>();var vector=output.ToArray();Normalize(vector);return vector;}
    private static DenseTensor<float> ToTensor(SKBitmap b,int w,int h){var t=new DenseTensor<float>(new[]{1,3,h,w});for(var y=0;y<h;y++)for(var x=0;x<w;x++){var c=b.GetPixel(x,y);t[0,0,y,x]=(c.Red-127.5f)/128f;t[0,1,y,x]=(c.Green-127.5f)/128f;t[0,2,y,x]=(c.Blue-127.5f)/128f;}return t;}
    private static float ReadScore(Tensor<float> t,int i)=>t.Dimensions.Count==1?t[i]:t.Dimensions.Count==2?t[0,i]:t[0,i,0];
    private static float[] ReadRow(Tensor<float> t,int i,int width){var a=new float[width];for(var j=0;j<width;j++)a[j]=t.Dimensions.Count==2?t[i,j]:t[0,i,j];return a;}
    private static SKRectI ClampRect(float x1,float y1,float x2,float y2,int w,int h){var l=Math.Clamp((int)MathF.Floor(x1),0,w-1);var t=Math.Clamp((int)MathF.Floor(y1),0,h-1);var r=Math.Clamp((int)MathF.Ceiling(x2),l+1,w);var b=Math.Clamp((int)MathF.Ceiling(y2),t+1,h);return new(l,t,r,b);}
    private static double Quality(SKBitmap b,SKRectI r){var area=(r.Width*r.Height)/(double)(b.Width*b.Height);return Math.Clamp(Math.Sqrt(area)*3.0,0,1);}
    private static byte[] CreateReviewThumbnail(SKBitmap source, SKRectI rect){var margin=(int)(Math.Max(rect.Width,rect.Height)*0.25);var expanded=ClampRect(rect.Left-margin,rect.Top-margin,rect.Right+margin,rect.Bottom+margin,source.Width,source.Height);using var crop=new SKBitmap(expanded.Width,expanded.Height);using(var canvas=new SKCanvas(crop)){canvas.DrawBitmap(source,new SKRect(expanded.Left,expanded.Top,expanded.Right,expanded.Bottom),new SKRect(0,0,expanded.Width,expanded.Height));}using var image=SKImage.FromBitmap(crop);using var encoded=image.Encode(SKEncodedImageFormat.Webp,82);return encoded.ToArray();}
    private static void Normalize(float[] v){double sum=0;foreach(var x in v)sum+=x*x;var n=Math.Sqrt(sum);if(n<=1e-12)return;for(var i=0;i<v.Length;i++)v[i]=(float)(v[i]/n);}
    public void Dispose(){_detector?.Dispose();_embedder?.Dispose();_gate.Dispose();}
}

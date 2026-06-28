using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
namespace ProjectManagement.Features.MediaLibrary.Services;
public sealed class FaceModelReadinessService : IFaceModelReadinessService
{
    private readonly MediaLibraryOptions _options; private readonly IWebHostEnvironment _environment;
    public FaceModelReadinessService(IOptions<MediaLibraryOptions> options,IWebHostEnvironment environment){_options=options.Value;_environment=environment;}
    public async Task<FaceModelReadiness> CheckAsync(CancellationToken cancellationToken)
    {
        var p=_options.People; if(!p.Enabled) return new(false,false,"Face intelligence is disabled.",null,null);
        var root=Path.IsPathRooted(p.ModelRoot)?p.ModelRoot:Path.Combine(_environment.ContentRootPath,p.ModelRoot);
        var detector=Path.Combine(root,p.Detector.FileName??string.Empty); var embedder=Path.Combine(root,p.Embedder.FileName??string.Empty);
        if(string.IsNullOrWhiteSpace(p.Detector.FileName)||string.IsNullOrWhiteSpace(p.Embedder.FileName)) return new(true,false,"Detector and embedding model files are not configured.",detector,embedder);
        if(!File.Exists(detector)||!File.Exists(embedder)) return new(true,false,"One or more configured model files are missing.",detector,embedder);
        if(string.IsNullOrWhiteSpace(p.Detector.License)||string.IsNullOrWhiteSpace(p.Embedder.License)) return new(true,false,"Model licence metadata is required before processing can start.",detector,embedder);
        if(!await MatchesAsync(detector,p.Detector.Sha256,cancellationToken)||!await MatchesAsync(embedder,p.Embedder.Sha256,cancellationToken)) return new(true,false,"A model checksum does not match the approved configuration.",detector,embedder);
        return new(true,true,"Face models are installed and verified.",detector,embedder);
    }
    private static async Task<bool> MatchesAsync(string path,string expected,CancellationToken ct){if(string.IsNullOrWhiteSpace(expected)) return false; await using var s=File.OpenRead(path); var hash=await SHA256.HashDataAsync(s,ct); return string.Equals(Convert.ToHexString(hash),expected.Replace("-","").Trim(),StringComparison.OrdinalIgnoreCase);}
}

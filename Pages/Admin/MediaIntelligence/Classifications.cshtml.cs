using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Admin.MediaIntelligence;

[Authorize(Roles = "Admin,HoD")]
public sealed class ClassificationsModel : PageModel
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaClassificationOverrideService _overrides;
    public ClassificationsModel(MediaLibraryDbContext db, IMediaClassificationOverrideService overrides) { _db = db; _overrides = overrides; }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public MediaClassification? Classification { get; set; }
    [BindProperty(SupportsGet = true)] public string Mode { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    public int PageSize { get; } = 24;
    public int Total { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public IReadOnlyList<Row> Rows { get; private set; } = Array.Empty<Row>();
    [TempData] public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        P = Math.Max(1, P);
        var query = _db.Assets.AsNoTracking().Where(x => x.IsAvailable && !x.IsDeleted && !x.IsArchived && x.Kind == MediaAssetKind.Photo);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim().ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(q) || x.OriginalFileName.ToLower().Contains(q) || x.ContextTitle.ToLower().Contains(q));
        }
        if (Classification.HasValue) query = query.Where(x => x.Classification == Classification.Value);
        query = Mode switch
        {
            "manual" => query.Where(x => x.ClassificationIsManual),
            "low" => query.Where(x => !x.ClassificationIsManual && (x.ClassificationConfidence == null || x.ClassificationConfidence < 0.65)),
            "eligible" => query.Where(x => x.Classification == MediaClassification.Photograph),
            _ => query
        };
        Total = await query.CountAsync(cancellationToken);
        if (P > TotalPages) P = TotalPages;
        Rows = await query.OrderByDescending(x => x.MediaDateUtc).ThenBy(x => x.Id).Skip((P - 1) * PageSize).Take(PageSize)
            .Select(x => new Row(x.Id, x.Title, x.ContextTitle, x.OriginalFileName, x.Classification, x.ClassificationConfidence, x.ClassificationIsManual, x.ClassifierVersion, x.AnalysisSignalsJson, x.Classification == MediaClassification.Photograph, x.MediaDateUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSetAsync(long assetId, MediaClassification classification, string? reason, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "unknown";
        await _overrides.SetManualAsync(assetId, classification, userId, reason, cancellationToken);
        StatusMessage = "Classification updated and audited.";
        return RedirectToPage(new { Q, Classification, Mode, P });
    }

    public async Task<IActionResult> OnPostResetAsync(long assetId, string? reason, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "unknown";
        await _overrides.ResetToAutomaticAsync(assetId, userId, reason, cancellationToken);
        StatusMessage = "Automatic classification has been queued.";
        return RedirectToPage(new { Q, Classification, Mode, P });
    }

    public sealed record Row(long Id, string Title, string ContextTitle, string OriginalFileName, MediaClassification Classification, double? Confidence, bool IsManual, string? ClassifierVersion, string? SignalsJson, bool FaceEligible, DateTimeOffset MediaDateUtc)
    {
        public IReadOnlyList<string> Signals
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SignalsJson)) return Array.Empty<string>();
                try { return JsonSerializer.Deserialize<string[]>(SignalsJson) ?? Array.Empty<string>(); } catch { return Array.Empty<string>(); }
            }
        }
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Pages.Admin.MediaSources;

[Authorize(Roles = "Admin,HoD")]
public sealed class IndexModel : PageModel
{
    private readonly MediaLibraryDbContext _db;

    public IndexModel(MediaLibraryDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public IReadOnlyList<SourceRow> Sources { get; private set; } = Array.Empty<SourceRow>();
    public int PendingJobs { get; private set; }
    public int FailedJobs { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostScanAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = await _db.Sources.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        source.ScanRequestedAtUtc = DateTimeOffset.UtcNow;
        source.ScanStatus = source.IsEnabled ? "Queued" : "Disabled";
        source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = $"A scan has been queued for {source.Name}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryFailedAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var count = await _db.ProcessingJobs
            .Where(job => job.Status == MediaProcessingJobStatus.Failed
                          || job.Status == MediaProcessingJobStatus.DeadLetter)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, MediaProcessingJobStatus.Pending)
                .SetProperty(job => job.AttemptCount, 0)
                .SetProperty(job => job.AvailableAfterUtc, now)
                .SetProperty(job => job.LockedBy, (string?)null)
                .SetProperty(job => job.LockExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(job => job.FailureCode, (string?)null)
                .SetProperty(job => job.FailureMessage, (string?)null)
                .SetProperty(job => job.UpdatedAtUtc, now), cancellationToken);

        StatusMessage = $"{count} failed media processing job(s) were queued again.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Sources = await _db.Sources
            .AsNoTracking()
            .OrderBy(source => source.SourceType)
            .ThenBy(source => source.Name)
            .Select(source => new SourceRow(
                source.Id,
                source.Name,
                source.SourceType,
                source.IsEnabled,
                source.IsReadOnly,
                source.RootPath,
                source.ScanStatus,
                source.IndexedAssetCount,
                source.LastSuccessfulScanAtUtc,
                source.LastError))
            .ToListAsync(cancellationToken);

        PendingJobs = await _db.ProcessingJobs.CountAsync(
            job => job.Status == MediaProcessingJobStatus.Pending
                   || job.Status == MediaProcessingJobStatus.Running,
            cancellationToken);
        FailedJobs = await _db.ProcessingJobs.CountAsync(
            job => job.Status == MediaProcessingJobStatus.Failed
                   || job.Status == MediaProcessingJobStatus.DeadLetter,
            cancellationToken);
    }

    public sealed record SourceRow(
        Guid Id,
        string Name,
        MediaLibrarySourceType Type,
        bool IsEnabled,
        bool IsReadOnly,
        string? RootPath,
        string Status,
        long AssetCount,
        DateTimeOffset? LastSuccessfulScanAtUtc,
        string? LastError);
}

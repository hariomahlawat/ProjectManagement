using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Projects;

[Authorize(Roles = "Admin")]
public class TrashModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ProjectRetentionOptions _retentionOptions;

    public TrashModel(ApplicationDbContext db, IClock clock, IOptions<ProjectRetentionOptions> retentionOptions)
    {
        _db = db;
        _clock = clock;
        _retentionOptions = retentionOptions.Value;
    }

    public IReadOnlyList<ProjectTrashRow> Projects { get; private set; } = Array.Empty<ProjectTrashRow>();

    public bool RemoveAssetsByDefault => _retentionOptions.RemoveAssetsOnPurge;

    public int RetentionDays => Math.Max(0, _retentionOptions.TrashRetentionDays);

    public async Task OnGetAsync()
    {
        var now = _clock.UtcNow;

        var rows = await _db.Projects
            .IgnoreQueryFilters()
            .Where(p => p.IsDeleted)
            .OrderByDescending(p => p.DeletedAt)
            .Select(p => new ProjectTrashRow
            {
                ProjectId = p.Id,
                Name = p.Name,
                CaseFileNumber = p.CaseFileNumber,
                HodDisplay = p.HodUser != null ? (p.HodUser.FullName ?? p.HodUser.UserName) : null,
                ProjectOfficerDisplay = p.LeadPoUser != null ? (p.LeadPoUser.FullName ?? p.LeadPoUser.UserName) : null,
                DeletedAtUtc = p.DeletedAt,
                DeletedByUserId = p.DeletedByUserId,
                DeleteReason = p.DeleteReason,
                DeleteMethod = p.DeleteMethod,
                IsArchived = p.IsArchived
            })
            .ToListAsync();

        var deletedByIds = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.DeletedByUserId))
            .Select(r => r.DeletedByUserId!)
            .Distinct()
            .ToList();

        var userMap = deletedByIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : await _db.Users
                .Where(u => deletedByIds.Contains(u.Id))
                .Select(u => new { u.Id, Display = u.FullName ?? u.UserName ?? u.Id })
                .ToDictionaryAsync(x => x.Id, x => x.Display, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.DeletedByUserId) && userMap.TryGetValue(row.DeletedByUserId!, out var display))
            {
                row.DeletedByDisplay = display;
            }

            if (row.DeletedAtUtc.HasValue)
            {
                var due = row.DeletedAtUtc.Value.AddDays(RetentionDays);
                row.PurgeScheduledUtc = due;
                var remaining = due - now;
                row.DaysUntilPurge = remaining.TotalDays <= 0 ? 0 : (int)Math.Ceiling(remaining.TotalDays);
            }
        }

        Projects = rows;
    }

    public string FormatTimestamp(DateTimeOffset? value)
    {
        return value.HasValue
            ? value.Value.ToLocalTime().ToString("dd MMM yyyy HH:mm")
            : "—";
    }

    public string FormatDate(DateTimeOffset? value)
    {
        return value.HasValue
            ? value.Value.ToLocalTime().ToString("dd MMM yyyy")
            : "—";
    }

    public sealed class ProjectTrashRow
    {
        public int ProjectId { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? CaseFileNumber { get; init; }

        public string? HodDisplay { get; init; }

        public string? ProjectOfficerDisplay { get; init; }

        public DateTimeOffset? DeletedAtUtc { get; init; }

        public string? DeletedByUserId { get; init; }

        public string? DeletedByDisplay { get; set; }

        public string? DeleteReason { get; init; }

        public string? DeleteMethod { get; init; }

        public bool IsArchived { get; init; }

        public DateTimeOffset? PurgeScheduledUtc { get; set; }

        public int? DaysUntilPurge { get; set; }

        public string DeletedByFallback => !string.IsNullOrWhiteSpace(DeletedByDisplay)
            ? DeletedByDisplay!
            : (DeletedByUserId ?? "—");
    }
}

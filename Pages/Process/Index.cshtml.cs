using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Pages.Process;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public string ProcessVersion { get; private set; } = PlanConstants.StageTemplateVersion;
    public bool CanEditChecklist { get; private set; }
        = false;

    public DateTimeOffset? ProcessUpdatedOn { get; private set; }
        = null;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var latestVersion = await _db.StageTemplates
            .AsNoTracking()
            .OrderByDescending(t => t.Version)
            .Select(t => t.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(latestVersion))
        {
            ProcessVersion = latestVersion;
        }

        await LoadProcessUpdatedOnAsync(ProcessVersion, cancellationToken);

        CanEditChecklist = User.IsInRole("MCO") || User.IsInRole("HoD");
    }

    private async Task LoadProcessUpdatedOnAsync(string version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        var templateSnapshots = await _db.StageChecklistTemplates
            .AsNoTracking()
            .Where(t => t.Version == version)
            .Select(t => new { t.Id, t.UpdatedOn })
            .ToListAsync(cancellationToken);

        if (templateSnapshots.Count == 0)
        {
            return;
        }

        var templateIds = templateSnapshots
            .Select(t => t.Id)
            .ToArray();

        var candidateDates = new List<DateTimeOffset>();

        foreach (var snapshot in templateSnapshots)
        {
            if (snapshot.UpdatedOn.HasValue)
            {
                candidateDates.Add(snapshot.UpdatedOn.Value);
            }
        }

        var latestItemUpdate = await _db.StageChecklistItemTemplates
            .AsNoTracking()
            .Where(i => templateIds.Contains(i.TemplateId) && i.UpdatedOn != null)
            .OrderByDescending(i => i.UpdatedOn)
            .Select(i => i.UpdatedOn)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestItemUpdate.HasValue)
        {
            candidateDates.Add(latestItemUpdate.Value);
        }

        var latestAudit = await _db.StageChecklistAudits
            .AsNoTracking()
            .Where(a => templateIds.Contains(a.TemplateId))
            .OrderByDescending(a => a.PerformedOn)
            .Select(a => (DateTimeOffset?)a.PerformedOn)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestAudit.HasValue)
        {
            candidateDates.Add(latestAudit.Value);
        }

        if (candidateDates.Count > 0)
        {
            ProcessUpdatedOn = candidateDates.Max();
        }
    }
}

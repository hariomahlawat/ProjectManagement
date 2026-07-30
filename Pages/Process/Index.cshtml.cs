using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Pages.Process;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authorizationService;

    public IndexModel(ApplicationDbContext db, IAuthorizationService authorizationService)
    {
        _db = db;
        _authorizationService = authorizationService;
    }

    public string ProcessVersion { get; private set; } = PlanConstants.DefaultStageTemplateVersion;
    public bool CanEditChecklist { get; private set; }
    public bool CanEditPurpose { get; private set; }
    public DateTimeOffset? ProcessUpdatedOn { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var availableVersions = await _db.StageTemplates
            .AsNoTracking()
            .Select(t => t.Version)
            .Distinct()
            .ToListAsync(cancellationToken);

        ProcessVersion = availableVersions
            .OrderByDescending(ParseVersion)
            .ThenByDescending(v => v, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? PlanConstants.DefaultStageTemplateVersion;

        await LoadGuidanceUpdatedOnAsync(ProcessVersion, cancellationToken);

        var checklistAuthorization = await _authorizationService.AuthorizeAsync(User, Policies.Checklist.Edit);
        CanEditChecklist = checklistAuthorization.Succeeded;

        var purposeAuthorization = await _authorizationService.AuthorizeAsync(User, Policies.Checklist.EditPurpose);
        CanEditPurpose = purposeAuthorization.Succeeded;
    }

    private static Version ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Version(0, 0);
        }

        var candidate = value.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        return Version.TryParse(candidate, out var version)
            ? version
            : new Version(0, 0);
    }

    private async Task LoadGuidanceUpdatedOnAsync(string version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        var templateSnapshots = await _db.StageChecklistTemplates
            .AsNoTracking()
            .Where(t => t.Version == version)
            .Select(t => new { t.Id, t.UpdatedOn, t.PurposeUpdatedOn })
            .ToListAsync(cancellationToken);

        if (templateSnapshots.Count == 0)
        {
            return;
        }

        var templateIds = templateSnapshots.Select(t => t.Id).ToArray();
        var candidateDates = new List<DateTimeOffset>();

        candidateDates.AddRange(templateSnapshots
            .Where(t => t.UpdatedOn.HasValue)
            .Select(t => t.UpdatedOn!.Value));

        candidateDates.AddRange(templateSnapshots
            .Where(t => t.PurposeUpdatedOn.HasValue)
            .Select(t => t.PurposeUpdatedOn!.Value));

        var latestItemUpdate = await _db.StageChecklistItemTemplates
            .AsNoTracking()
            .Where(i => templateIds.Contains(i.TemplateId) && i.UpdatedOn.HasValue)
            .MaxAsync(i => (DateTimeOffset?)i.UpdatedOn, cancellationToken);

        if (latestItemUpdate.HasValue)
        {
            candidateDates.Add(latestItemUpdate.Value);
        }

        ProcessUpdatedOn = candidateDates.Count > 0 ? candidateDates.Max() : null;
    }
}

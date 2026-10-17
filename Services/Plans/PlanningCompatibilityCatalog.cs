using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Plans;

/// <summary>
/// Establishes the trusted published duration catalog at the legacy-to-B1 boundary.
/// Untrusted duration-only extension rows may be abandoned private work and must not
/// become authoritative merely because the project-level publication marker advances.
/// </summary>
public static class PlanningCompatibilityCatalog
{
    public static async Task<List<ProjectPlanDuration>> PrepareDurationRowsForPublicationAsync(
        ApplicationDbContext db,
        int projectId,
        string? workflowVersion,
        bool baselineIsTrusted,
        IEnumerable<string?> explicitlyPublishedStageCodes,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.ProjectPlanDurations
            .Where(duration => duration.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        if (baselineIsTrusted)
        {
            return rows;
        }

        var version = workflowVersion ?? PlanConstants.DefaultStageTemplateVersion;
        var authoritativeCodes = new HashSet<string>(
            ProcurementWorkflow.StageCodesFor(version),
            StringComparer.OrdinalIgnoreCase);

        var templateCodes = await db.StageTemplates
            .AsNoTracking()
            .Where(template => template.Version == version)
            .Select(template => template.Code)
            .ToListAsync(cancellationToken);
        authoritativeCodes.UnionWith(templateCodes.Where(code => !string.IsNullOrWhiteSpace(code)));

        var officialCodes = await db.ProjectStages
            .AsNoTracking()
            .Where(stage => stage.ProjectId == projectId)
            .Select(stage => stage.StageCode)
            .ToListAsync(cancellationToken);
        authoritativeCodes.UnionWith(officialCodes.Where(code => !string.IsNullOrWhiteSpace(code)));

        authoritativeCodes.UnionWith(explicitlyPublishedStageCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!));

        var retained = new List<ProjectPlanDuration>(rows.Count);
        foreach (var row in rows)
        {
            if (!authoritativeCodes.Contains(row.StageCode))
            {
                db.ProjectPlanDurations.Remove(row);
                continue;
            }

            // The row identity/catalog is authoritative, but a legacy value is not.
            // The reviewed/submitted publication below will populate explicit values.
            row.DurationDays = null;
            retained.Add(row);
        }

        return retained;
    }
}

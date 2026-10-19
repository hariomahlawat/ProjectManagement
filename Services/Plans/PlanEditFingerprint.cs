using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Services.Plans;

/// <summary>
/// Optimistic editor identity for concurrent browser tabs. Calculated from the
/// persisted actor-owned proposal; it is not an authorization or CSRF token.
/// The project transaction gate protects comparison and save as one operation.
/// </summary>
public static class PlanEditFingerprint
{
    public static string Compute(PlanVersion? plan)
    {
        if (plan is null)
            return "none";

        var canonical = JsonSerializer.Serialize(new
        {
            plan.Id, plan.VersionNo, plan.Status, plan.SubmittedOn,
            plan.OwnerUserId, plan.PlanningInputVersion,
            plan.AnchorStageCode, plan.AnchorDate, plan.SkipWeekends,
            plan.SkipHolidays, plan.TransitionRule, plan.PncApplicable,
            Stages = plan.StagePlans
                .OrderBy(row => row.StageCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.StageCode, StringComparer.Ordinal)
                .Select(row => new
                {
                    row.StageCode, row.PlannedStart, row.PlannedDue,
                    row.DurationDays, row.SortOrder
                })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

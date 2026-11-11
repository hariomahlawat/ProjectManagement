using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.ItProjects;

namespace ProjectManagement.Services.ItProjects;

/// <summary>A live IT project (not started or in progress) with what the workspace views show about it.</summary>
public sealed record LiveItProjectRow(
    int Id,
    string Title,
    string Reference,
    int FinancialYearStart,
    ItProjectStatus Status,
    ItProjectTiming Timing,
    ItProjectYearTypeInfo? YearType);

/// <summary>
/// The live IT projects, in board order (current stage, then title). IT projects have no assigned
/// Project Officer: the ITO runs them, so the conference review and the Officer workload view both
/// give every live IT project to each officer who holds the ITO role. One query keeps the two views
/// from drifting apart.
/// </summary>
public static class LiveItProjects
{
    public static async Task<IReadOnlyList<LiveItProjectRow>> LoadAsync(
        ApplicationDbContext db,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var projects = await db.ItProjects
            .AsNoTracking()
            .Where(project => project.Module == ItProjectModules.It && !project.IsDeleted && project.DroppedOn == null)
            .Include(project => project.Stages)
            .Include(project => project.FinancialYears)
            .ToListAsync(cancellationToken);

        var today = ItProjectYearTypes.TodayIst(utcNow);
        var currentYear = ItProjectYearTypes.CurrentFinancialYear(utcNow);
        return projects
            .Select(project => (Project: project, Status: ItProjectStatus.Resolve(project)))
            .Where(item => item.Status.Kind is ItProjectStatusKind.NotStarted or ItProjectStatusKind.InProgress)
            .Select(item => new LiveItProjectRow(
                item.Project.Id,
                item.Project.Title,
                item.Project.PppReferenceNumber,
                item.Project.FinancialYearStart,
                item.Status,
                ItProjectTiming.Resolve(item.Project, today, ItProjectYearTypes.ToIstDate),
                ItProjectYearTypes.Resolve(item.Project, currentYear, currentYear)))
            .OrderBy(row => row.Status.CurrentStageCode is null ? int.MaxValue : ItProjectStages.OrderOf(row.Status.CurrentStageCode))
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>"Not started" or "At {stage}", the state line both views show.</summary>
    public static string StateText(ItProjectStatus status)
        => status.Kind == ItProjectStatusKind.NotStarted ? "Not started" : $"At {status.CurrentStageLabel}";
}

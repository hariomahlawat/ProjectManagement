using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.ItProjects;

namespace ProjectManagement.Services.ItProjects;

/// <summary>A live project (not started or in progress) of the IT projects engine, with what the workspace views show about it.</summary>
/// <param name="Module">The module the project belongs to (<see cref="ItProjectModules"/>).</param>
/// <param name="ProjectOfficerUserId">The assigned Project Officer, in a module that assigns one; null for IT projects.</param>
public sealed record LiveItProjectRow(
    int Id,
    string Title,
    string Reference,
    int FinancialYearStart,
    ItProjectStatus Status,
    ItProjectTiming Timing,
    ItProjectYearTypeInfo? YearType,
    string Module = ItProjectModules.It,
    string? ProjectOfficerUserId = null);

/// <summary>
/// The live projects the workspace views (conference review, Officer workload) put under an
/// officer, in board order (current stage, then title). One query for both views keeps them from
/// drifting apart.
///
/// Who a project sits under depends on its module (docs/ux/molti-sre-projects.md):
/// IT projects have no assigned officer, the ITO runs them, so <see cref="LoadAsync"/> gives every
/// live IT project to each officer who holds the ITO role. MOLTI and SRE projects sit under the
/// Project Officer assigned to them (<see cref="LoadAssignedAsync"/>); an unassigned one sits
/// under nobody.
/// </summary>
public static class LiveItProjects
{
    /// <summary>The live IT projects.</summary>
    public static Task<IReadOnlyList<LiveItProjectRow>> LoadAsync(
        ApplicationDbContext db,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return LoadCoreAsync(
            db.ItProjects.Where(project => project.Module == ItProjectModules.It),
            utcNow,
            cancellationToken);
    }

    /// <summary>
    /// The live projects of the modules that assign a Project Officer, for the projects whose
    /// officer still can be one (an active user with the Project Officer role): a project whose
    /// officer lost the role gives them no rights, so it is not their workload either. Ordered by
    /// module, then as the board.
    /// </summary>
    public static async Task<IReadOnlyList<LiveItProjectRow>> LoadAssignedAsync(
        ApplicationDbContext db,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var modules = OfficerModules.Select(module => module.Code).ToArray();
        var rows = await LoadCoreAsync(
            db.ItProjects.Where(project => modules.Contains(project.Module) && project.ProjectOfficerUserId != null),
            utcNow,
            cancellationToken);
        if (rows.Count == 0)
        {
            return rows;
        }

        var officers = (await ItProjectOfficers.ListAssignableAsync(db, cancellationToken))
            .Select(officer => officer.UserId)
            .ToHashSet(StringComparer.Ordinal);
        return rows
            .Where(row => officers.Contains(row.ProjectOfficerUserId!))
            .OrderBy(row => Array.IndexOf(modules, row.Module))
            .ToList();
    }

    /// <summary>The modules whose projects sit under an assigned Project Officer, in catalogue order.</summary>
    public static IReadOnlyList<ItProjectModules.Definition> OfficerModules { get; } =
        ItProjectModules.All.Where(module => module.HasAssignedOfficer).ToList();

    private static async Task<IReadOnlyList<LiveItProjectRow>> LoadCoreAsync(
        IQueryable<ItProject> source,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var projects = await source
            .AsNoTracking()
            .Where(project => !project.IsDeleted && project.DroppedOn == null)
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
                ItProjectYearTypes.Resolve(item.Project, currentYear, currentYear),
                item.Project.Module,
                item.Project.ProjectOfficerUserId))
            .OrderBy(row => row.Status.CurrentStageCode is null ? int.MaxValue : ItProjectStages.OrderOf(row.Status.CurrentStageCode))
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>"Not started" or "At {stage}", the state line both views show.</summary>
    public static string StateText(ItProjectStatus status)
        => status.Kind == ItProjectStatusKind.NotStarted ? "Not started" : $"At {status.CurrentStageLabel}";

    /// <summary>The project's own page, under its module's route.</summary>
    public static string OpenUrl(LiveItProjectRow row) => $"{ItProjectModules.Get(row.Module).RoutePrefix}/{row.Id}";

    /// <summary>Who runs a project of the module, as a sentence subject: "The ITO" or "The officer".</summary>
    public static string RunnerText(string module)
        => ItProjectModules.IsKnown(module) && ItProjectModules.HasAssignedOfficer(module) ? "The officer" : "The ITO";

    /// <summary>The Bootstrap icon of a module's menu entry and sections.</summary>
    public static string IconOf(string module) => module switch
    {
        ItProjectModules.Molti => "bi-clipboard-check",
        ItProjectModules.Sre => "bi-clipboard-pulse",
        _ => "bi-pc-display"
    };
}

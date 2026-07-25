using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Utilities.PartialDates;

namespace ProjectManagement.ViewComponents;

public sealed class ProjectTotCommandCardViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _db;

    public ProjectTotCommandCardViewComponent(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync(int projectId, bool canManage)
    {
        var tot = await _db.ProjectTots
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .Select(item => new
            {
                item.Status,
                item.StartedOn,
                item.StartDatePrecision,
                item.CompletedOn,
                item.CompletionDatePrecision
            })
            .SingleOrDefaultAsync();

        var pending = await _db.ProjectTotRequests
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.DecisionState == ProjectTotRequestDecisionState.Pending)
            .Select(item => new { item.ProposedStatus })
            .SingleOrDefaultAsync();

        var model = pending is not null
            ? new ProjectTotCommandCardViewModel(
                projectId,
                "Approval pending",
                $"Proposed: {StatusLabel(pending.ProposedStatus)}",
                "info",
                canManage: false)
            : Build(projectId, tot?.Status, tot?.StartedOn, tot?.StartDatePrecision,
                tot?.CompletedOn, tot?.CompletionDatePrecision, canManage);

        return View(model);
    }

    private static ProjectTotCommandCardViewModel Build(
        int projectId,
        ProjectTotStatus? status,
        DateOnly? startedOn,
        PartialDatePrecision? startPrecision,
        DateOnly? completedOn,
        PartialDatePrecision? completionPrecision,
        bool canManage)
    {
        if (!status.HasValue)
        {
            return new(projectId, "Not recorded", canManage ? "Record ToT position" : "No ToT position recorded", "warning", canManage);
        }

        var title = StatusLabel(status.Value);
        var summary = status.Value switch
        {
            ProjectTotStatus.NotRequired => "No ToT action required",
            ProjectTotStatus.NotStarted => "ToT action pending",
            ProjectTotStatus.InProgress when startedOn.HasValue
                => $"Started {FormatDate(startedOn.Value, startPrecision ?? PartialDatePrecision.Day)}",
            ProjectTotStatus.InProgress => "ToT is in progress",
            ProjectTotStatus.Completed when completedOn.HasValue
                => $"Completed {FormatDate(completedOn.Value, completionPrecision ?? PartialDatePrecision.Day)}",
            ProjectTotStatus.Completed => "ToT marked completed",
            _ => "Record ToT position"
        };

        var tone = status.Value switch
        {
            ProjectTotStatus.Completed => "positive",
            ProjectTotStatus.InProgress => "info",
            ProjectTotStatus.NotRequired => "neutral",
            _ => "warning"
        };

        return new(projectId, title, summary, tone, canManage);
    }

    private static string StatusLabel(ProjectTotStatus status) => status switch
    {
        ProjectTotStatus.NotRequired => "Not required",
        ProjectTotStatus.NotStarted => "Not started",
        ProjectTotStatus.InProgress => "In progress",
        ProjectTotStatus.Completed => "Completed",
        _ => status.ToString()
    };

    private static string FormatDate(DateOnly value, PartialDatePrecision precision) => precision switch
    {
        PartialDatePrecision.Year => value.Year.ToString(CultureInfo.InvariantCulture),
        PartialDatePrecision.Month => value.ToString("MMM yyyy", CultureInfo.InvariantCulture),
        _ => value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
    };
}

public sealed record ProjectTotCommandCardViewModel(
    int ProjectId,
    string Title,
    string Summary,
    string Tone,
    bool CanManage);

using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
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
        var isBuild = await _db.Projects
            .AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => project.IsBuild)
            .SingleOrDefaultAsync();

        if (isBuild)
        {
            return View(new ProjectTotCommandCardViewModel(
                projectId,
                "Not applicable",
                "Repeat Build",
                "neutral",
                CanManage: false));
        }

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

        var card = ProjectTotPresentation.BuildCard(
            tot?.Status,
            tot?.StartedOn,
            tot?.StartDatePrecision ?? PartialDatePrecision.None,
            tot?.CompletedOn,
            tot?.CompletionDatePrecision ?? PartialDatePrecision.None,
            pending?.ProposedStatus,
            canManage);

        return View(new ProjectTotCommandCardViewModel(
            projectId,
            card.Title,
            card.Summary,
            card.Tone,
            CanManage: pending is null && canManage));
    }
}

public sealed record ProjectTotCommandCardViewModel(
    int ProjectId,
    string Title,
    string Summary,
    string Tone,
    bool CanManage);

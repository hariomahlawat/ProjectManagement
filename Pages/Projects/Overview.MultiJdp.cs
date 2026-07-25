using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Services.IndustryPartners;

namespace ProjectManagement.Pages.Projects;

public partial class OverviewModel
{
    public async Task<IActionResult> OnGetMultiJdpProfileAsync(int id, CancellationToken ct)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        var exists = await _db.Projects
            .AsNoTracking()
            .AnyAsync(project => project.Id == id && !project.IsDeleted, ct);
        if (!exists)
        {
            return NotFound();
        }

        var profile = await _industryPartners.GetProjectMultiJdpProfileAsync(id, ct);
        return new JsonResult(new
        {
            success = true,
            profile = ToMultiJdpResponse(profile)
        });
    }

    public async Task<IActionResult> OnPostAddProjectJdpAsync(
        int id,
        int partnerId,
        CancellationToken ct)
    {
        if (id <= 0 || partnerId <= 0)
        {
            return new JsonResult(new { error = "Select a valid organisation." })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        if (!await CanManageProjectJdpAsync(id, ct))
        {
            return new JsonResult(new { error = "You are not authorised to update JDPs for this project." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        try
        {
            var profile = await _industryPartners.AddProjectJdpAsync(id, partnerId, User, ct);
            return new JsonResult(new
            {
                success = true,
                message = "JDP added to the project.",
                profile = ToMultiJdpResponse(profile)
            });
        }
        catch (KeyNotFoundException exception)
        {
            return new JsonResult(new { error = exception.Message })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }
        catch (IndustryPartnerValidationException exception)
        {
            var message = exception.Errors
                .SelectMany(entry => entry.Value)
                .FirstOrDefault()
                ?? "Unable to add the JDP.";

            return new JsonResult(new { error = message })
            {
                StatusCode = message.Contains("already linked", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest
            };
        }
    }

    public async Task<IActionResult> OnPostRemoveProjectJdpAsync(
        int id,
        int partnerId,
        CancellationToken ct)
    {
        if (id <= 0 || partnerId <= 0)
        {
            return BadRequest();
        }

        if (!await CanManageProjectJdpAsync(id, ct))
        {
            return new JsonResult(new { error = "You are not authorised to update JDPs for this project." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        try
        {
            var profile = await _industryPartners.RemoveProjectJdpAsync(id, partnerId, User, ct);
            return new JsonResult(new
            {
                success = true,
                message = "JDP removed from the project.",
                profile = ToMultiJdpResponse(profile)
            });
        }
        catch (KeyNotFoundException exception)
        {
            return new JsonResult(new { error = exception.Message })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }
    }

    private static object ToMultiJdpResponse(ProjectMultiJdpProfileDto profile) => new
    {
        projectId = profile.ProjectId,
        count = profile.Count,
        hasJdp = profile.HasJdp,
        cardTitle = profile.CardTitle,
        cardSummary = profile.CardSummary,
        partners = profile.Partners.Select(partner => new
        {
            id = partner.Id,
            name = partner.Name,
            location = partner.Location,
            usageSummary = partner.UsageSummary,
            otherProjectCount = partner.OtherProjectCount,
            otherOngoingProjectCount = partner.OtherOngoingProjectCount,
            otherCompletedProjectCount = partner.OtherCompletedProjectCount,
            otherProjects = partner.OtherProjects.Select(project => new
            {
                projectId = project.ProjectId,
                projectName = project.ProjectName,
                caseFileNumber = project.CaseFileNumber,
                statusLabel = project.StatusLabel
            })
        })
    };
}

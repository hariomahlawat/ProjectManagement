using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models;
using ProjectManagement.Models.IndustryPartners;

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

        return new JsonResult(new
        {
            success = true,
            profile = await BuildMultiJdpProfileAsync(id, ct)
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

        var projectExists = await _db.Projects
            .AnyAsync(project => project.Id == id && !project.IsDeleted, ct);
        if (!projectExists)
        {
            return NotFound();
        }

        var partner = await _db.IndustryPartners
            .FirstOrDefaultAsync(item => item.Id == partnerId, ct);
        if (partner is null)
        {
            return new JsonResult(new { error = "Industry organisation not found." })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }

        var alreadyLinked = await _db.IndustryPartnerProjects
            .AnyAsync(link => link.ProjectId == id && link.IndustryPartnerId == partnerId, ct);
        if (alreadyLinked)
        {
            return new JsonResult(new { error = "This organisation is already linked to the project as a JDP." })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }

        _db.IndustryPartnerProjects.Add(new IndustryPartnerProject
        {
            ProjectId = id,
            IndustryPartnerId = partnerId,
            LinkedByUserId = _users.GetUserId(User),
            LinkedUtc = DateTimeOffset.UtcNow
        });
        partner.UpdatedUtc = DateTimeOffset.UtcNow;
        partner.UpdatedByUserId = _users.GetUserId(User);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var nowLinked = await _db.IndustryPartnerProjects
                .AsNoTracking()
                .AnyAsync(link => link.ProjectId == id && link.IndustryPartnerId == partnerId, ct);
            if (nowLinked)
            {
                return new JsonResult(new { error = "This organisation is already linked to the project as a JDP." })
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
            }

            throw;
        }

        return new JsonResult(new
        {
            success = true,
            message = "JDP added to the project.",
            profile = await BuildMultiJdpProfileAsync(id, ct)
        });
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

        var link = await _db.IndustryPartnerProjects
            .FirstOrDefaultAsync(item => item.ProjectId == id && item.IndustryPartnerId == partnerId, ct);
        if (link is null)
        {
            return new JsonResult(new { error = "The selected JDP is no longer linked to this project." })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }

        var partner = await _db.IndustryPartners
            .FirstOrDefaultAsync(item => item.Id == partnerId, ct);

        _db.IndustryPartnerProjects.Remove(link);
        if (partner is not null)
        {
            partner.UpdatedUtc = DateTimeOffset.UtcNow;
            partner.UpdatedByUserId = _users.GetUserId(User);
        }

        await _db.SaveChangesAsync(ct);

        return new JsonResult(new
        {
            success = true,
            message = "JDP removed from the project.",
            profile = await BuildMultiJdpProfileAsync(id, ct)
        });
    }

    private async Task<object> BuildMultiJdpProfileAsync(int projectId, CancellationToken ct)
    {
        var linkedPartners = await _db.IndustryPartnerProjects
            .AsNoTracking()
            .Where(link => link.ProjectId == projectId)
            .OrderBy(link => link.IndustryPartner.Name)
            .ThenBy(link => link.IndustryPartnerId)
            .Select(link => new
            {
                id = link.IndustryPartnerId,
                name = link.IndustryPartner.Name,
                location = link.IndustryPartner.Location,
                otherProjects = link.IndustryPartner.PartnerProjects
                    .Where(other => other.ProjectId != projectId && !other.Project.IsDeleted)
                    .Select(other => new
                    {
                        projectId = other.ProjectId,
                        projectName = other.Project.Name,
                        caseFileNumber = other.Project.CaseFileNumber,
                        lifecycleStatus = other.Project.LifecycleStatus,
                        isArchived = other.Project.IsArchived
                    })
                    .ToList()
            })
            .ToListAsync(ct);

        var partners = linkedPartners.Select(partner =>
        {
            var projects = partner.otherProjects
                .Select(project => new
                {
                    project.projectId,
                    project.projectName,
                    project.caseFileNumber,
                    statusLabel = project.isArchived
                        ? "Archived"
                        : project.lifecycleStatus switch
                        {
                            ProjectLifecycleStatus.Completed => "Completed",
                            ProjectLifecycleStatus.Cancelled => "Cancelled",
                            _ => "Ongoing"
                        }
                })
                .OrderBy(project => project.statusLabel == "Ongoing" ? 0 : project.statusLabel == "Completed" ? 1 : 2)
                .ThenBy(project => project.projectName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new
            {
                partner.id,
                partner.name,
                partner.location,
                otherProjectCount = projects.Count,
                otherOngoingProjectCount = projects.Count(project => project.statusLabel == "Ongoing"),
                otherCompletedProjectCount = projects.Count(project => project.statusLabel == "Completed"),
                otherProjects = projects
            };
        }).ToList();

        return new
        {
            projectId,
            count = partners.Count,
            hasJdp = partners.Count > 0,
            cardTitle = partners.Count switch
            {
                0 => "No JDP linked",
                1 => partners[0].name,
                _ => $"{partners.Count} JDPs linked"
            },
            cardSummary = partners.Count switch
            {
                0 => CanManageJdp ? "Link an industry partner" : "No JDP recorded",
                1 => BuildPartnerUsageSummary(partners[0].otherProjectCount, partners[0].otherOngoingProjectCount, partners[0].otherCompletedProjectCount),
                _ => string.Join(" · ", partners.Take(2).Select(partner => partner.name)) + (partners.Count > 2 ? $" · +{partners.Count - 2} more" : string.Empty)
            },
            partners
        };
    }

    private static string BuildPartnerUsageSummary(int total, int ongoing, int completed)
    {
        if (total == 0)
        {
            return "Not linked to any other project";
        }

        var parts = new List<string>();
        if (ongoing > 0) parts.Add($"{ongoing} ongoing");
        if (completed > 0) parts.Add($"{completed} completed");
        var other = Math.Max(0, total - ongoing - completed);
        if (other > 0) parts.Add($"{other} other");

        return $"Also linked to {total} other {(total == 1 ? "project" : "projects")}" +
               (parts.Count == 0 ? string.Empty : $" · {string.Join(" · ", parts)}");
    }
}

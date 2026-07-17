using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Configuration;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

public sealed partial class IndexModel
{
    private async Task PopulateSelectListsAsync(CancellationToken cancellationToken)
    {
        var supportedTypes = new[] { IprType.Patent, IprType.Copyright };

        TypeOptions = supportedTypes
            .Select(type => new SelectListItem(GetTypeLabel(type), type.ToString())
            {
                Selected = Types.Contains(type)
            })
            .ToList();

        TypeFormOptions = supportedTypes
            .Select(type => new SelectListItem(GetTypeLabel(type), type.ToString())
            {
                Selected = Input.Type.HasValue && Input.Type.Value == type
            })
            .ToList();

        var supportedStatuses = new[] { IprStatus.Filed, IprStatus.Granted };

        StatusOptions = supportedStatuses
            .Select(status => new SelectListItem(GetStatusLabel(status), status.ToString())
            {
                Selected = Statuses.Contains(status)
            })
            .ToList();

        StatusFormOptions = supportedStatuses
            .Select(status => new SelectListItem(GetStatusLabel(status), status.ToString())
            {
                Selected = Input.Status.HasValue && Input.Status.Value == status
            })
            .ToList();

        var projectSnapshot = await _db.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted)
            .OrderBy(project => project.Name)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.CaseFileNumber,
                project.LifecycleStatus,
                project.IsArchived
            })
            .ToListAsync(cancellationToken);

        ProjectPickerOptions = projectSnapshot
            .Select(project => new ProjectPickerOption(
                project.Id,
                project.Name,
                project.CaseFileNumber,
                GetProjectLifecycleLabel(project.LifecycleStatus, project.IsArchived)))
            .ToList();

        var projectItems = ProjectPickerOptions
            .Select(project => new SelectListItem(project.Name, project.Id.ToString(CultureInfo.InvariantCulture))
            {
                Selected = ProjectId.HasValue && ProjectId.Value == project.Id
            })
            .ToList();

        var projectOptions = new List<SelectListItem>
        {
            new("All projects", string.Empty)
            {
                Selected = !ProjectId.HasValue
            }
        };
        projectOptions.AddRange(projectItems);
        ProjectOptions = projectOptions;

        var years = await _db.IprRecords.AsNoTracking()
            .Where(r => r.FiledAtUtc != null)
            .Select(r => r.FiledAtUtc!.Value.Year)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(cancellationToken);

        var yearOptions = new List<SelectListItem>
        {
            new("All years", string.Empty)
            {
                Selected = !Year.HasValue
            }
        };

        foreach (var year in years)
        {
            yearOptions.Add(new SelectListItem(year.ToString(CultureInfo.InvariantCulture), year.ToString(CultureInfo.InvariantCulture))
            {
                Selected = Year.HasValue && Year.Value == year
            });
        }

        YearOptions = yearOptions;

        PageSizeOptions = new List<SelectListItem>
        {
            new("10", "10") { Selected = PageSize == 10 },
            new("25", "25") { Selected = PageSize == 25 },
            new("50", "50") { Selected = PageSize == 50 }
        };
    }

    private async Task EvaluateAuthorizationAsync()
    {
        var result = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        CanEdit = result.Succeeded;
    }

    private void NormalizeFilters()
    {
        Types = Types.Distinct().ToList();
        Statuses = Statuses
            .Select(status => status == IprStatus.FilingUnderProcess ? IprStatus.Filed : status)
            .Where(status => status is IprStatus.Filed or IprStatus.Granted)
            .Distinct()
            .ToList();

        Tab = Tab?.Trim().ToLowerInvariant() switch
        {
            "project" => "project",
            "followup" => "followup",
            "analytics" => "analytics",
            _ => "records"
        };

        if (ProjectId.HasValue && ProjectId.Value <= 0)
        {
            ProjectId = null;
        }

        if (Year.HasValue && Year.Value <= 0)
        {
            Year = null;
        }

        if (Id.HasValue && Id.Value <= 0)
        {
            Id = null;
        }
    }

    private void NormalizeMode()
    {
        if (string.Equals(_mode, "create", StringComparison.OrdinalIgnoreCase))
        {
            _mode = CanEdit ? "create" : null;
        }
        else if (string.Equals(_mode, "edit", StringComparison.OrdinalIgnoreCase))
        {
            _mode = CanEdit ? "edit" : null;
        }
        else
        {
            _mode = null;
        }
    }

    private void NormalizePaging()
    {
        if (PageNumber < 1)
        {
            PageNumber = 1;
        }

        if (PageSize is not (10 or 25 or 50))
        {
            PageSize = 25;
        }
    }

    private IprFilter BuildFilter()
    {
        var filter = new IprFilter
        {
            Query = Query,
            Types = Types.Count > 0 ? Types.ToArray() : null,
            Statuses = Statuses.Count > 0 ? Statuses.ToArray() : null,
            ProjectId = ProjectId,
            FiledFrom = Year.HasValue ? new DateOnly(Year.Value, 1, 1) : null,
            FiledTo = Year.HasValue ? new DateOnly(Year.Value, 12, 31) : null
        };

        filter.Page = PageNumber;
        filter.PageSize = PageSize;
        PageNumber = filter.Page;
        PageSize = filter.PageSize;

        return filter;
    }


    private static string GetTypeLabel(IprType type)
        => type switch
        {
            IprType.Patent => "Patent",
            IprType.Copyright => "Copyright",
            _ => type.ToString()
        };

    private static string GetStatusLabel(IprStatus status)
        => status switch
        {
            IprStatus.FilingUnderProcess => "Awaiting grant",
            IprStatus.Filed => "Awaiting grant",
            IprStatus.Granted => "Granted",
            IprStatus.Rejected => "Rejected",
            IprStatus.Withdrawn => "Withdrawn",
            _ => status.ToString()
        };

    private static string GetProjectLifecycleLabel(ProjectLifecycleStatus status, bool isArchived)
    {
        if (isArchived)
        {
            return "Archived";
        }

        return status switch
        {
            ProjectLifecycleStatus.Active => "Active",
            ProjectLifecycleStatus.Completed => "Completed",
            ProjectLifecycleStatus.Cancelled => "Cancelled",
            _ => status.ToString()
        };
    }

}

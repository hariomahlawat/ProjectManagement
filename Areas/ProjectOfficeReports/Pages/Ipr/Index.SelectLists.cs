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

        var selectedFormType = Input.Type;
        StatusFormOptions = supportedStatuses
            .Select(status => new SelectListItem(
                selectedFormType is IprType type
                    ? GetStatusLabel(status, type)
                    : GetStatusLabel(status),
                status.ToString())
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

        var projectOptions = new List<SelectListItem>
        {
            new("All projects", string.Empty)
            {
                Selected = !ProjectId.HasValue
            }
        };
        projectOptions.AddRange(ProjectPickerOptions.Select(project =>
            new SelectListItem(project.Name, project.Id.ToString(CultureInfo.InvariantCulture))
            {
                Selected = ProjectId.HasValue && ProjectId.Value == project.Id
            }));
        ProjectOptions = projectOptions;

        DateBasisOptions = new[]
        {
            new SelectListItem("Filed year", IprDateBasis.Filed.ToString())
            {
                Selected = DateBasis == IprDateBasis.Filed
            },
            new SelectListItem("Grant / registration year", IprDateBasis.Protected.ToString())
            {
                Selected = DateBasis == IprDateBasis.Protected
            }
        };

        var dateYears = await _db.IprRecords
            .AsNoTracking()
            .Where(record =>
                record.Status == IprStatus.FilingUnderProcess ||
                record.Status == IprStatus.Filed ||
                record.Status == IprStatus.Granted)
            .Select(record => new
            {
                FiledYear = record.FiledAtUtc.HasValue ? (int?)record.FiledAtUtc.Value.Year : null,
                ProtectedYear = record.GrantedAtUtc.HasValue ? (int?)record.GrantedAtUtc.Value.Year : null
            })
            .ToListAsync(cancellationToken);

        DateYearOptions = dateYears
            .SelectMany(item => new[] { item.FiledYear, item.ProtectedYear })
            .Where(year => year.HasValue)
            .Select(year => year!.Value)
            .Distinct()
            .OrderByDescending(year => year)
            .Select(year => new IprYearOption(
                year,
                dateYears.Any(item => item.FiledYear == year),
                dateYears.Any(item => item.ProtectedYear == year)))
            .ToList();

        var yearOptions = new List<SelectListItem>
        {
            new("All years", string.Empty)
            {
                Selected = !Year.HasValue
            }
        };
        yearOptions.AddRange(DateYearOptions
            .Where(option => DateBasis == IprDateBasis.Protected ? option.HasProtected : option.HasFiled)
            .Select(option => new SelectListItem(option.Label, option.Value)
            {
                Selected = Year.HasValue && Year.Value == option.Year
            }));
        YearOptions = yearOptions;

        LinkageOptions = new[]
        {
            new SelectListItem("All records", IprLinkageFilter.All.ToString()) { Selected = Linkage == IprLinkageFilter.All },
            new SelectListItem("Linked to a project", IprLinkageFilter.Linked.ToString()) { Selected = Linkage == IprLinkageFilter.Linked },
            new SelectListItem("Unassigned", IprLinkageFilter.Unassigned.ToString()) { Selected = Linkage == IprLinkageFilter.Unassigned }
        };

        EvidenceOptions = new[]
        {
            new SelectListItem("All evidence states", IprEvidenceFilter.All.ToString()) { Selected = Evidence == IprEvidenceFilter.All },
            new SelectListItem("Evidence available", IprEvidenceFilter.Available.ToString()) { Selected = Evidence == IprEvidenceFilter.Available },
            new SelectListItem("Evidence missing", IprEvidenceFilter.Missing.ToString()) { Selected = Evidence == IprEvidenceFilter.Missing }
        };

        PageSizeOptions = new List<SelectListItem>
        {
            new("10", "10") { Selected = PageSize == 10 },
            new("15", "15") { Selected = PageSize == 15 },
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
        Types = Types
            .Where(type => type is IprType.Patent or IprType.Copyright)
            .Distinct()
            .ToList();

        Statuses = Statuses
            .Select(status => status == IprStatus.FilingUnderProcess ? IprStatus.Filed : status)
            .Where(status => status is IprStatus.Filed or IprStatus.Granted)
            .Distinct()
            .ToList();

        if (!Enum.IsDefined(DateBasis))
        {
            DateBasis = IprDateBasis.Filed;
        }

        if (!Enum.IsDefined(Linkage))
        {
            Linkage = IprLinkageFilter.All;
        }

        if (!Enum.IsDefined(Evidence))
        {
            Evidence = IprEvidenceFilter.All;
        }

        Tab = Tab?.Trim().ToLowerInvariant() switch
        {
            "project" => "project",
            "followup" => "followup",
            "analytics" => "analytics",
            _ => "records"
        };

        AttentionIssue = AttentionIssue?.Trim().ToLowerInvariant() switch
        {
            "overdue" => "overdue",
            "data" => "data",
            "linkage" => "linkage",
            "evidence" => "evidence",
            _ => null
        };

        if (!string.Equals(Tab, "records", StringComparison.Ordinal))
        {
            Query = null;
            SelectedRecordId = null;
        }

        if (!string.Equals(Tab, "followup", StringComparison.Ordinal))
        {
            AttentionIssue = null;
        }

        if (ProjectId.HasValue && ProjectId.Value <= 0)
        {
            ProjectId = null;
        }

        if (ProjectId.HasValue)
        {
            Linkage = IprLinkageFilter.All;
        }

        if (Year.HasValue && (Year.Value < 1900 || Year.Value > DateTime.UtcNow.Year + 1))
        {
            Year = null;
        }

        if (SelectedRecordId.HasValue && SelectedRecordId.Value <= 0)
        {
            SelectedRecordId = null;
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

        if (string.Equals(_mode, "edit", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Tab, "records", StringComparison.Ordinal) &&
            Id.HasValue)
        {
            SelectedRecordId ??= Id.Value;
        }
    }

    private void NormalizePaging()
    {
        if (PageNumber < 1)
        {
            PageNumber = 1;
        }

        if (PageSize is not (10 or 15 or 25 or 50))
        {
            PageSize = 15;
        }
    }

    private IprFilter BuildFilter(bool includeQuery = true)
    {
        var filter = new IprFilter
        {
            Query = includeQuery ? Query : null,
            Types = Types.Count > 0 ? Types.ToArray() : null,
            Statuses = Statuses.Count > 0 ? Statuses.ToArray() : null,
            ProjectId = ProjectId,
            DateBasis = DateBasis,
            Year = Year,
            Linkage = Linkage,
            Evidence = Evidence
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
            IprStatus.FilingUnderProcess => "Pending",
            IprStatus.Filed => "Pending",
            IprStatus.Granted => "Protected",
            IprStatus.Rejected => "Rejected",
            IprStatus.Withdrawn => "Withdrawn",
            _ => status.ToString()
        };

    private static string GetStatusLabel(IprStatus status, IprType type)
        => status switch
        {
            IprStatus.FilingUnderProcess or IprStatus.Filed
                => type == IprType.Copyright ? "Registration pending" : "Patent pending",
            IprStatus.Granted
                => type == IprType.Copyright ? "Copyright registered" : "Patent granted",
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

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

        TypeFormOptions = supportedTypes
            .Select(type => new SelectListItem(GetTypeLabel(type), type.ToString())
            {
                Selected = Input.Type.HasValue && Input.Type.Value == type
            })
            .ToList();

        var supportedStatuses = IprStatusPolicy.SelectableStatuses;

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
            .Where(IprProjectEligibilityPolicy.EligibleProjectPredicate)
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

    }

    private async Task EvaluateAuthorizationAsync()
    {
        var result = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        CanEdit = result.Succeeded;
    }

    private const string ClosedStatusScope = "Closed";

    private void NormalizeFilters()
    {
        if (!string.IsNullOrWhiteSpace(StatusScope))
        {
            var scope = StatusScope.Trim();
            if (string.Equals(scope, ClosedStatusScope, StringComparison.OrdinalIgnoreCase))
            {
                Statuses = IprStatusPolicy.ClosedStatuses.ToList();
            }
            else if (Enum.TryParse<IprStatus>(scope, ignoreCase: true, out var scopedStatus))
            {
                Statuses = new List<IprStatus> { scopedStatus };
            }

            StatusScope = null;
        }

        Types = Types
            .Where(type => type is IprType.Patent or IprType.Copyright)
            .Distinct()
            .ToList();

        Statuses = Statuses
            .Select(IprStatusPolicy.Normalize)
            .Where(IprStatusPolicy.IsSelectable)
            .Distinct()
            .ToList();

        if (!Enum.IsDefined(DateBasis))
        {
            DateBasis = IprDateBasis.Filed;
        }

        if (From.HasValue && To.HasValue && From.Value > To.Value)
        {
            (From, To) = (To, From);
        }

        if (!Enum.IsDefined(Linkage))
        {
            Linkage = IprLinkageFilter.All;
        }

        if (!Enum.IsDefined(Evidence))
        {
            Evidence = IprEvidenceFilter.All;
        }

        if (!Enum.IsDefined(PendingAge))
        {
            PendingAge = IprPendingAge.Any;
        }

        var requestedTab = Tab?.Trim().ToLowerInvariant();
        Tab = requestedTab switch
        {
            "followup" => "followup",
            "analytics" => "analytics",
            _ => "records"
        };

        // Links to the retired Projects tab open the register grouped by project.
        if (requestedTab == "project" && string.IsNullOrWhiteSpace(Group))
        {
            Group = "project";
        }

        Group = Group?.Trim().ToLowerInvariant() switch
        {
            "project" => "project",
            "status" => "status",
            "year" => "year",
            _ => null
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
            FiledFrom = DateBasis == IprDateBasis.Protected ? null : From,
            FiledTo = DateBasis == IprDateBasis.Protected ? null : To,
            ProtectedFrom = DateBasis == IprDateBasis.Protected ? From : null,
            ProtectedTo = DateBasis == IprDateBasis.Protected ? To : null,
            Linkage = Linkage,
            Evidence = Evidence,
            PendingAge = PendingAge
        };

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
            IprStatus.Granted => "Granted",
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

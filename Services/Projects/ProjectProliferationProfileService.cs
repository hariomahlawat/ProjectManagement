using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectProliferationProfileService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public ProjectProliferationProfileService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<ProjectProliferationProfileVm?> GetAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var projectExists = await _db.Projects
            .AsNoTracking()
            .AnyAsync(project => project.Id == projectId && !project.IsDeleted, cancellationToken);

        if (!projectExists)
        {
            return null;
        }

        var cost = await _db.ProjectProductionCostFacts
            .AsNoTracking()
            .Where(fact => fact.ProjectId == projectId)
            .Select(fact => new
            {
                fact.ApproxProductionCost,
                fact.UpdatedAtUtc,
                fact.UpdatedByUserId
            })
            .SingleOrDefaultAsync(cancellationToken);

        var status = await _db.ProjectTechStatuses
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .Select(item => new
            {
                item.AvailableForProliferation,
                item.NotAvailableReason,
                item.ProliferationRemarks,
                item.MarkedAtUtc,
                item.MarkedByUserId
            })
            .SingleOrDefaultAsync(cancellationToken);

        DateTimeOffset? costUpdatedAt = cost is not null && cost.UpdatedAtUtc > DateTimeOffset.MinValue
            ? cost.UpdatedAtUtc
            : null;
        DateTimeOffset? statusUpdatedAt = status is not null && status.MarkedAtUtc > DateTimeOffset.MinValue
            ? status.MarkedAtUtc
            : null;
        var latestIsStatus = statusUpdatedAt.HasValue &&
            (!costUpdatedAt.HasValue || statusUpdatedAt.Value >= costUpdatedAt.Value);
        var updatedAt = latestIsStatus ? statusUpdatedAt : costUpdatedAt;
        var updatedByUserId = latestIsStatus ? status?.MarkedByUserId : cost?.UpdatedByUserId;

        string? updatedByDisplayName = null;
        if (!string.IsNullOrWhiteSpace(updatedByUserId))
        {
            updatedByDisplayName = await _db.Users
                .AsNoTracking()
                .Where(user => user.Id == updatedByUserId)
                .Select(user => user.FullName != null && user.FullName != string.Empty
                    ? user.FullName
                    : user.UserName)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return new ProjectProliferationProfileVm
        {
            ProjectId = projectId,
            CostLakhs = cost?.ApproxProductionCost,
            AvailableForProliferation = status?.AvailableForProliferation,
            NotAvailableReason = status?.NotAvailableReason,
            Remarks = status?.ProliferationRemarks,
            UpdatedAtUtc = updatedAt,
            UpdatedByDisplayName = updatedByDisplayName
        };
    }

    public async Task<ProjectProliferationUpdateResult> UpdateAsync(
        ProjectProliferationUpdateCommand command,
        string userId,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = Validate(command);
        if (errors.Count > 0)
        {
            return ProjectProliferationUpdateResult.ValidationFailed(errors);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return ProjectProliferationUpdateResult.Forbidden();
        }

        var project = await _db.Projects
            .AsNoTracking()
            .Where(item => item.Id == command.ProjectId && !item.IsDeleted)
            .Select(item => new { item.Id, item.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return ProjectProliferationUpdateResult.NotFound();
        }

        var normalizedReason = Normalize(command.NotAvailableReason);
        var normalizedRemarks = Normalize(command.Remarks);
        if (command.AvailableForProliferation != false)
        {
            normalizedReason = null;
        }

        var productionFact = await _db.ProjectProductionCostFacts
            .SingleOrDefaultAsync(item => item.ProjectId == command.ProjectId, cancellationToken);
        var techStatus = await _db.ProjectTechStatuses
            .SingleOrDefaultAsync(item => item.ProjectId == command.ProjectId, cancellationToken);

        var previousCost = productionFact?.ApproxProductionCost;
        var previousAvailability = techStatus?.AvailableForProliferation;
        var previousReason = techStatus?.NotAvailableReason;
        var previousRemarks = techStatus?.ProliferationRemarks;
        var now = _clock.UtcNow;

        if (productionFact is not null || command.CostLakhs.HasValue)
        {
            productionFact ??= new ProjectProductionCostFact
            {
                ProjectId = command.ProjectId
            };

            if (_db.Entry(productionFact).State == EntityState.Detached)
            {
                await _db.ProjectProductionCostFacts.AddAsync(productionFact, cancellationToken);
            }

            productionFact.ApproxProductionCost = command.CostLakhs;
            productionFact.UpdatedAtUtc = now;
            productionFact.UpdatedByUserId = userId;
        }

        var requiresTechRecord = techStatus is not null ||
            command.AvailableForProliferation.HasValue ||
            normalizedRemarks is not null ||
            normalizedReason is not null;

        if (requiresTechRecord)
        {
            techStatus ??= new ProjectTechStatus
            {
                ProjectId = command.ProjectId,
                TechStatus = ProjectTechStatusCodes.Current
            };

            if (_db.Entry(techStatus).State == EntityState.Detached)
            {
                await _db.ProjectTechStatuses.AddAsync(techStatus, cancellationToken);
            }

            techStatus.AvailableForProliferation = command.AvailableForProliferation;
            techStatus.NotAvailableReason = normalizedReason;
            techStatus.ProliferationRemarks = normalizedRemarks;
            techStatus.MarkedAtUtc = now;
            techStatus.MarkedByUserId = userId;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            action: "Project.ProliferationProfileUpdated",
            message: $"Proliferation details updated for project {project.Name}.",
            userId: userId,
            userName: userName,
            data: new Dictionary<string, string?>
            {
                ["ProjectId"] = project.Id.ToString(CultureInfo.InvariantCulture),
                ["ProjectName"] = project.Name,
                ["CostBeforeLakhs"] = Format(previousCost),
                ["CostAfterLakhs"] = Format(command.CostLakhs),
                ["AvailabilityBefore"] = Format(previousAvailability),
                ["AvailabilityAfter"] = Format(command.AvailableForProliferation),
                ["ReasonBefore"] = previousReason,
                ["ReasonAfter"] = normalizedReason,
                ["RemarksBefore"] = previousRemarks,
                ["RemarksAfter"] = normalizedRemarks
            });

        var profile = await GetAsync(command.ProjectId, cancellationToken)
            ?? ProjectProliferationProfileVm.Empty(command.ProjectId);

        return ProjectProliferationUpdateResult.Success(profile);
    }

    private static Dictionary<string, string[]> Validate(ProjectProliferationUpdateCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (command.ProjectId <= 0)
        {
            errors[nameof(ProjectProliferationUpdateCommand.ProjectId)] = new[] { "A valid project is required." };
        }

        if (command.CostLakhs is < 0m)
        {
            errors[nameof(ProjectProliferationUpdateCommand.CostLakhs)] = new[]
            {
                "Proliferation cost cannot be negative."
            };
        }
        else if (command.CostLakhs.HasValue && DecimalScale(command.CostLakhs.Value) > 2)
        {
            errors[nameof(ProjectProliferationUpdateCommand.CostLakhs)] = new[]
            {
                "Use no more than two decimal places."
            };
        }

        var reason = Normalize(command.NotAvailableReason);
        var remarks = Normalize(command.Remarks);

        if (command.AvailableForProliferation == false && reason is null)
        {
            errors[nameof(ProjectProliferationUpdateCommand.NotAvailableReason)] = new[]
            {
                "Enter the reason the project is not available for proliferation."
            };
        }
        else if (reason?.Length > 500)
        {
            errors[nameof(ProjectProliferationUpdateCommand.NotAvailableReason)] = new[]
            {
                "Reason cannot exceed 500 characters."
            };
        }

        if (remarks?.Length > 500)
        {
            errors[nameof(ProjectProliferationUpdateCommand.Remarks)] = new[]
            {
                "Remarks cannot exceed 500 characters."
            };
        }

        return errors;
    }

    private static int DecimalScale(decimal value)
        => (decimal.GetBits(value)[3] >> 16) & 0x7F;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Format(decimal? value)
        => value?.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Format(bool? value)
        => value switch
        {
            true => "Available",
            false => "Not available",
            _ => "Not assessed"
        };
}

public sealed record ProjectProliferationUpdateCommand(
    int ProjectId,
    decimal? CostLakhs,
    bool? AvailableForProliferation,
    string? NotAvailableReason,
    string? Remarks);

public enum ProjectProliferationUpdateStatus
{
    Success,
    ValidationFailed,
    NotFound,
    Forbidden
}

public sealed class ProjectProliferationUpdateResult
{
    private ProjectProliferationUpdateResult(
        ProjectProliferationUpdateStatus status,
        ProjectProliferationProfileVm? profile = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        Status = status;
        Profile = profile;
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public ProjectProliferationUpdateStatus Status { get; }
    public ProjectProliferationProfileVm? Profile { get; }
    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public bool IsSuccess => Status == ProjectProliferationUpdateStatus.Success;

    public static ProjectProliferationUpdateResult Success(ProjectProliferationProfileVm profile)
        => new(ProjectProliferationUpdateStatus.Success, profile);

    public static ProjectProliferationUpdateResult ValidationFailed(IReadOnlyDictionary<string, string[]> errors)
        => new(ProjectProliferationUpdateStatus.ValidationFailed, errors: errors);

    public static ProjectProliferationUpdateResult NotFound()
        => new(ProjectProliferationUpdateStatus.NotFound);

    public static ProjectProliferationUpdateResult Forbidden()
        => new(ProjectProliferationUpdateStatus.Forbidden);
}

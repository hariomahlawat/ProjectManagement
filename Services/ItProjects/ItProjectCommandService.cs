using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.ItProjects;

/// <summary>
/// The only writer of IT project rows. Validates, applies <see cref="ItProjectStageRules"/>,
/// writes the row and its audit entry in one transaction, and rejects stale row versions.
/// </summary>
public sealed class ItProjectCommandService : IItProjectCommandService
{
    public const string ConcurrencyConflictMessage = "This IT project was changed by someone else. Refresh and review the latest version before trying again.";
    public const string NotFoundMessage = "The IT project could not be found.";
    public const int TitleMaxLength = 200;
    public const int ReferenceMaxLength = 100;
    public const int DroppedReasonMaxLength = 500;

    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public ItProjectCommandService(ApplicationDbContext db, IAuditService audit, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<(ItProjectCommandResult Result, int Id)> CreateAsync(ItProjectDetailsInput input, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureActor(actor);

        var errors = ValidateDetails(input);
        if (errors.Count > 0)
        {
            return (ItProjectCommandResult.Fail(errors), 0);
        }

        var now = UtcNow();
        var project = new ItProject
        {
            Title = input.Title.Trim(),
            PppReferenceNumber = input.PppReferenceNumber.Trim(),
            FinancialYearStart = input.FinancialYearStart,
            Description = input.Description.Trim(),
            CreatedByUserId = actor.UserId,
            CreatedAtUtc = now,
            UpdatedByUserId = actor.UserId,
            UpdatedAtUtc = now,
            RowVersion = NewRowVersion(),
            Stages = ItProjectStages.NewStageRows()
        };

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        _db.ItProjects.Add(project);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ItProjects.Created", actor, project, new Dictionary<string, string?>
        {
            ["Title"] = project.Title,
            ["PppReferenceNumber"] = project.PppReferenceNumber,
            ["FinancialYear"] = FinancialYearHelper.Format(project.FinancialYearStart)
        });
        await transaction.CommitAsync(cancellationToken);

        return (ItProjectCommandResult.Ok(), project.Id);
    }

    public async Task<ItProjectCommandResult> UpdateDetailsAsync(int id, ItProjectDetailsInput input, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureActor(actor);

        var errors = ValidateDetails(input);
        if (errors.Count > 0)
        {
            return ItProjectCommandResult.Fail(errors);
        }

        var project = await LoadForWriteAsync(id, cancellationToken);
        if (project is null)
        {
            return ItProjectCommandResult.Fail(NotFoundMessage);
        }

        var before = new Dictionary<string, string?>
        {
            ["Title"] = project.Title,
            ["PppReferenceNumber"] = project.PppReferenceNumber,
            ["FinancialYear"] = FinancialYearHelper.Format(project.FinancialYearStart)
        };

        project.Title = input.Title.Trim();
        project.PppReferenceNumber = input.PppReferenceNumber.Trim();
        project.FinancialYearStart = input.FinancialYearStart;
        project.Description = input.Description.Trim();

        return await CommitAsync(project, rowVersion, actor, "ItProjects.Updated", new Dictionary<string, string?>
        {
            ["BeforeTitle"] = before["Title"],
            ["BeforePppReferenceNumber"] = before["PppReferenceNumber"],
            ["BeforeFinancialYear"] = before["FinancialYear"],
            ["Title"] = project.Title,
            ["PppReferenceNumber"] = project.PppReferenceNumber,
            ["FinancialYear"] = FinancialYearHelper.Format(project.FinancialYearStart)
        }, cancellationToken);
    }

    public async Task<ItProjectStageSaveResult> SaveStagesAsync(int id, IReadOnlyCollection<ItProjectStageInput> stages, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stages);
        EnsureActor(actor);

        var rules = ItProjectStageRules.Apply(stages);
        if (!rules.IsValid)
        {
            return new ItProjectStageSaveResult(ItProjectCommandResult.Fail(rules.Errors), Array.Empty<string>(), Array.Empty<string>());
        }

        var project = await LoadForWriteAsync(id, cancellationToken);
        if (project is null)
        {
            return new ItProjectStageSaveResult(ItProjectCommandResult.Fail(NotFoundMessage), Array.Empty<string>(), Array.Empty<string>());
        }

        var (autoCompleted, reopened) = ItProjectStageRules.Diff(project.Stages, rules.Stages);
        var statusBefore = ItProjectStatus.Resolve(project);

        var rows = project.Stages.ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);
        foreach (var state in rules.Stages)
        {
            if (!rows.TryGetValue(state.StageCode, out var row))
            {
                // A project created before a stage existed in the catalogue: add the missing row.
                row = new ItProjectStage { StageCode = state.StageCode, SortOrder = state.SortOrder };
                project.Stages.Add(row);
            }

            row.SortOrder = state.SortOrder;
            row.IsCompleted = state.IsCompleted;
            row.CompletedOn = state.CompletedOn;
            row.IsSkipped = state.IsSkipped;
            row.CostLakhs = state.CostLakhs;
        }

        var statusAfter = ItProjectStatus.Resolve(project);
        if (!string.Equals(statusBefore.CurrentStageCode, statusAfter.CurrentStageCode, StringComparison.OrdinalIgnoreCase))
        {
            // Only a change of the current stage counts as movement; a date or cost edit does not.
            project.StageMovedAtUtc = UtcNow();
        }

        var data = new Dictionary<string, string?>
        {
            ["StatusBefore"] = statusBefore.Label,
            ["StatusAfter"] = statusAfter.Label,
            ["AutoCompleted"] = string.Join(",", autoCompleted),
            ["Reopened"] = string.Join(",", reopened)
        };
        foreach (var state in rules.Stages)
        {
            data[$"Stage.{state.StageCode}"] = Describe(state);
        }

        var result = await CommitAsync(project, rowVersion, actor, "ItProjects.StagesChanged", data, cancellationToken);
        return new ItProjectStageSaveResult(result, autoCompleted, reopened);
    }

    public async Task<ItProjectCommandResult> DropAsync(int id, DateOnly droppedOn, string reason, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);
        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return ItProjectCommandResult.Fail("Give the reason the project was dropped.");
        }

        if (trimmed.Length > DroppedReasonMaxLength)
        {
            return ItProjectCommandResult.Fail($"The reason must be {DroppedReasonMaxLength} characters or fewer.");
        }

        var project = await LoadForWriteAsync(id, cancellationToken);
        if (project is null)
        {
            return ItProjectCommandResult.Fail(NotFoundMessage);
        }

        if (project.DroppedOn.HasValue)
        {
            return ItProjectCommandResult.Fail("This IT project is already dropped.");
        }

        if (ItProjectStatus.Resolve(project).Kind == ItProjectStatusKind.Completed)
        {
            return ItProjectCommandResult.Fail("A completed IT project cannot be dropped. Reopen its last stage first.");
        }

        project.DroppedOn = droppedOn;
        project.DroppedReason = trimmed;
        project.DroppedByUserId = actor.UserId;

        return await CommitAsync(project, rowVersion, actor, "ItProjects.Dropped", new Dictionary<string, string?>
        {
            ["DroppedOn"] = droppedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Reason"] = trimmed
        }, cancellationToken);
    }

    public async Task<ItProjectCommandResult> ReinstateAsync(int id, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);
        var project = await LoadForWriteAsync(id, cancellationToken);
        if (project is null)
        {
            return ItProjectCommandResult.Fail(NotFoundMessage);
        }

        if (!project.DroppedOn.HasValue)
        {
            return ItProjectCommandResult.Fail("This IT project is not dropped.");
        }

        var data = new Dictionary<string, string?>
        {
            ["WasDroppedOn"] = project.DroppedOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["WasDroppedReason"] = project.DroppedReason
        };
        project.DroppedOn = null;
        project.DroppedReason = null;
        project.DroppedByUserId = null;

        return await CommitAsync(project, rowVersion, actor, "ItProjects.Reinstated", data, cancellationToken);
    }

    public async Task<ItProjectCommandResult> SetYearTypeAsync(int id, int financialYearStart, ItProjectYearType? type, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);
        if (type is { } requested && !Enum.IsDefined(requested))
        {
            return ItProjectCommandResult.Fail("Choose New, CL or CF.");
        }

        var project = await LoadForWriteAsync(id, cancellationToken);
        if (project is null)
        {
            return ItProjectCommandResult.Fail(NotFoundMessage);
        }

        var currentYear = ItProjectYearTypes.CurrentFinancialYear(_clock.UtcNow);
        var before = ItProjectYearTypes.Resolve(project, financialYearStart, currentYear);
        if (before is null)
        {
            return ItProjectCommandResult.Fail($"This IT project does not count in FY {FinancialYearHelper.Format(financialYearStart)}.");
        }

        var existing = project.YearTypeOverrides.FirstOrDefault(item => item.FinancialYearStart == financialYearStart);

        // Choosing the automatic type, or Reset, removes the hand-set row: there is nothing to keep.
        var manual = type is { } chosen && chosen != before.AutomaticType ? chosen : (ItProjectYearType?)null;
        if (manual is null && existing is null)
        {
            return ItProjectCommandResult.Ok();
        }

        if (manual is { } value && existing?.Type == value)
        {
            return ItProjectCommandResult.Ok();
        }

        if (manual is null)
        {
            project.YearTypeOverrides.Remove(existing!);
            _db.ItProjectYearTypeOverrides.Remove(existing!);
        }
        else if (existing is null)
        {
            project.YearTypeOverrides.Add(new ItProjectYearTypeOverride
            {
                FinancialYearStart = financialYearStart,
                Type = manual.Value,
                SetByUserId = actor.UserId,
                SetAtUtc = UtcNow()
            });
        }
        else
        {
            existing.Type = manual.Value;
            existing.SetByUserId = actor.UserId;
            existing.SetAtUtc = UtcNow();
        }

        var after = manual ?? before.AutomaticType;
        return await CommitAsync(project, rowVersion, actor, "ItProjects.YearTypeChanged", new Dictionary<string, string?>
        {
            ["FinancialYear"] = FinancialYearHelper.Format(financialYearStart),
            ["Before"] = ItProjectYearTypes.Code(before.Type) + (before.IsManual ? " (set by hand)" : " (automatic)"),
            ["After"] = ItProjectYearTypes.Code(after) + (manual is null ? " (automatic)" : " (set by hand)"),
            ["Automatic"] = ItProjectYearTypes.Code(before.AutomaticType)
        }, cancellationToken);
    }

    public async Task<ItProjectCommandResult> DeleteAsync(int id, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);
        var project = await LoadForWriteAsync(id, cancellationToken);
        if (project is null)
        {
            return ItProjectCommandResult.Fail(NotFoundMessage);
        }

        project.IsDeleted = true;
        project.DeletedAtUtc = UtcNow();
        project.DeletedByUserId = actor.UserId;

        return await CommitAsync(project, rowVersion, actor, "ItProjects.Deleted", new Dictionary<string, string?>
        {
            ["Title"] = project.Title,
            ["PppReferenceNumber"] = project.PppReferenceNumber
        }, cancellationToken);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static List<string> ValidateDetails(ItProjectDetailsInput input)
    {
        var errors = new List<string>();
        var title = input.Title?.Trim() ?? string.Empty;
        var reference = input.PppReferenceNumber?.Trim() ?? string.Empty;
        var description = input.Description?.Trim() ?? string.Empty;

        if (title.Length == 0)
        {
            errors.Add("Give the project a title.");
        }
        else if (title.Length > TitleMaxLength)
        {
            errors.Add($"The title must be {TitleMaxLength} characters or fewer.");
        }

        if (reference.Length == 0)
        {
            errors.Add("Give the IT PPP reference number.");
        }
        else if (reference.Length > ReferenceMaxLength)
        {
            errors.Add($"The IT PPP reference number must be {ReferenceMaxLength} characters or fewer.");
        }

        if (description.Length == 0)
        {
            errors.Add("Give the project a description.");
        }

        if (input.FinancialYearStart is < FinancialYearHelper.MinimumSupportedStartYear or > FinancialYearHelper.MaximumSupportedStartYear)
        {
            errors.Add("Choose the financial year.");
        }

        return errors;
    }

    private Task<ItProject?> LoadForWriteAsync(int id, CancellationToken cancellationToken)
        => _db.ItProjects
            .Include(project => project.Stages)
            .Include(project => project.YearTypeOverrides)
            .FirstOrDefaultAsync(project => project.Id == id && !project.IsDeleted, cancellationToken);

    private async Task<ItProjectCommandResult> CommitAsync(
        ItProject project,
        byte[] rowVersion,
        ItProjectActor actor,
        string action,
        Dictionary<string, string?> data,
        CancellationToken cancellationToken)
    {
        if (rowVersion is null || rowVersion.Length == 0)
        {
            return ItProjectCommandResult.Conflict();
        }

        _db.Entry(project).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;
        project.RowVersion = NewRowVersion();
        project.UpdatedByUserId = actor.UserId;
        project.UpdatedAtUtc = UtcNow();

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ItProjectCommandResult.Conflict();
        }

        await AuditAsync(action, actor, project, data);
        await transaction.CommitAsync(cancellationToken);
        return ItProjectCommandResult.Ok();
    }

    private Task AuditAsync(string action, ItProjectActor actor, ItProject project, Dictionary<string, string?> data)
    {
        data["ItProjectId"] = project.Id.ToString(CultureInfo.InvariantCulture);
        return _audit.LogAsync(action, userId: actor.UserId, userName: actor.UserName, data: data);
    }

    private static string Describe(ItProjectStageState state)
    {
        if (state.IsSkipped)
        {
            return "skipped";
        }

        var text = state.IsCompleted
            ? state.CompletedOn.HasValue ? $"done {state.CompletedOn.Value:yyyy-MM-dd}" : "done"
            : "open";
        return state.CostLakhs.HasValue
            ? $"{text}; cost {state.CostLakhs.Value.ToString("0.00", CultureInfo.InvariantCulture)} L"
            : text;
    }

    private static void EnsureActor(ItProjectActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(actor.UserId))
        {
            throw new InvalidOperationException("The acting user is required.");
        }
    }

    private DateTime UtcNow() => _clock.UtcNow.UtcDateTime;

    private static byte[] NewRowVersion() => Guid.NewGuid().ToByteArray();
}

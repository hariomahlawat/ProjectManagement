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

    /// <summary>The first year's IT ARPPP number of a carried-over case whose first number is not known.</summary>
    public const string ReferenceNotRecorded = "Not recorded";

    /// <summary>
    /// The longest description a save accepts. The column is unbounded text, so an older, longer
    /// description is still accepted while it is left unchanged.
    /// </summary>
    public const int DescriptionMaxLength = 8000;
    public const string DroppedStagesMessage = "This IT project is dropped. Reinstate it before changing its stages.";
    public const string DroppedInFutureMessage = "The dropped date cannot be later than today.";
    public const string NoOfficerInModuleMessage = "IT projects are run by the ITO; no Project Officer is assigned to them.";
    public const string NotAnOfficerMessage = "Choose an active user who holds the Project Officer role.";

    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public ItProjectCommandService(ApplicationDbContext db, IAuditService audit, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    // ---- public surface: every result goes out in the actor's module's words -----------------

    public async Task<(ItProjectCommandResult Result, int Id)> CreateAsync(ItProjectDetailsInput input, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        var (result, id) = await CreateCoreAsync(input, actor, cancellationToken);
        return (result.In(actor.Module), id);
    }

    public async Task<(ItProjectCommandResult Result, int Id)> CreateCarriedOverAsync(ItProjectCarriedOverInput input, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        var (result, id) = await CreateCarriedOverCoreAsync(input, actor, cancellationToken);
        return (result.In(actor.Module), id);
    }

    public async Task<ItProjectCommandResult> UpdateDetailsAsync(int id, ItProjectDetailsInput input, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
        => (await UpdateDetailsCoreAsync(id, input, rowVersion, actor, cancellationToken)).In(actor.Module);

    public async Task<ItProjectStageSaveResult> SaveStagesAsync(int id, IReadOnlyCollection<ItProjectStageInput> stages, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        var save = await SaveStagesCoreAsync(id, stages, rowVersion, actor, cancellationToken);
        return save with { Result = save.Result.In(actor.Module) };
    }

    public async Task<ItProjectCommandResult> DropAsync(int id, DateOnly droppedOn, string reason, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
        => (await DropCoreAsync(id, droppedOn, reason, rowVersion, actor, cancellationToken)).In(actor.Module);

    public async Task<ItProjectCommandResult> ReinstateAsync(int id, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
        => (await ReinstateCoreAsync(id, rowVersion, actor, cancellationToken)).In(actor.Module);

    public async Task<ItProjectCommandResult> SaveYearAsync(int id, int financialYearStart, ItProjectYearInput input, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
        => (await SaveYearCoreAsync(id, financialYearStart, input, rowVersion, actor, cancellationToken)).In(actor.Module);

    public async Task<ItProjectCommandResult> DeleteAsync(int id, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
        => (await DeleteCoreAsync(id, rowVersion, actor, cancellationToken)).In(actor.Module);

    public async Task<ItProjectCommandResult> AssignOfficerAsync(int id, string? officerUserId, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
        => (await AssignOfficerCoreAsync(id, officerUserId, rowVersion, actor, cancellationToken)).In(actor.Module);

    // ---- the commands, worded for IT projects ------------------------------------------------

    private async Task<(ItProjectCommandResult Result, int Id)> CreateCoreAsync(ItProjectDetailsInput input, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureActor(actor);

        var errors = ValidateDetails(input, currentDescription: null);
        var officerId = NormaliseOfficer(input.ProjectOfficerUserId);
        if (await OfficerErrorAsync(officerId, actor.Module, cancellationToken) is { } officerError)
        {
            errors.Add(officerError);
        }

        if (errors.Count > 0)
        {
            return (ItProjectCommandResult.Fail(errors), 0);
        }

        var now = UtcNow();
        var project = new ItProject
        {
            Module = actor.Module,
            ProjectOfficerUserId = officerId,
            ProjectOfficerAssignedAtUtc = officerId is null ? null : now,
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
            ["FinancialYear"] = FinancialYearHelper.Format(project.FinancialYearStart),
            ["ProjectOfficerUserId"] = project.ProjectOfficerUserId
        });
        await transaction.CommitAsync(cancellationToken);

        return (ItProjectCommandResult.Ok(), project.Id);
    }

    private async Task<(ItProjectCommandResult Result, int Id)> CreateCarriedOverCoreAsync(ItProjectCarriedOverInput input, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureActor(actor);

        var currentYear = ItProjectYearTypes.CurrentFinancialYear(_clock.UtcNow);
        var currentReference = input.CurrentYearPppReferenceNumber?.Trim() ?? string.Empty;
        var firstReference = string.IsNullOrWhiteSpace(input.FirstPppReferenceNumber) ? null : input.FirstPppReferenceNumber.Trim();

        // The project's own fields are checked as for any project; the first year's number is
        // optional here, so the placeholder stands in for the check.
        var errors = ValidateDetails(
            new ItProjectDetailsInput(input.Title, firstReference ?? ReferenceNotRecorded, input.FirstFinancialYearStart, input.Description),
            currentDescription: null);
        if (input.Type is not (ItProjectYearType.CommittedLiability or ItProjectYearType.CarriedForward))
        {
            errors.Add("Choose CL or CF for a case carried over from an earlier year.");
        }

        if (currentReference.Length == 0)
        {
            errors.Add($"Give the IT ARPPP reference number in FY {FinancialYearHelper.Format(currentYear)}.");
        }
        else if (currentReference.Length > ItProjectFinancialYear.ReferenceMaxLength)
        {
            errors.Add($"The IT ARPPP reference number must be {ItProjectFinancialYear.ReferenceMaxLength} characters or fewer.");
        }

        if (input.FirstFinancialYearStart >= currentYear &&
            input.FirstFinancialYearStart is >= FinancialYearHelper.MinimumSupportedStartYear and <= FinancialYearHelper.MaximumSupportedStartYear)
        {
            errors.Add($"A carried-over case was first listed before FY {FinancialYearHelper.Format(currentYear)}.");
        }

        var officerId = NormaliseOfficer(input.ProjectOfficerUserId);
        if (await OfficerErrorAsync(officerId, actor.Module, cancellationToken) is { } officerError)
        {
            errors.Add(officerError);
        }

        if (errors.Count > 0)
        {
            return (ItProjectCommandResult.Fail(errors), 0);
        }

        var now = UtcNow();
        var project = new ItProject
        {
            Module = actor.Module,
            ProjectOfficerUserId = officerId,
            ProjectOfficerAssignedAtUtc = officerId is null ? null : now,
            Title = input.Title.Trim(),
            PppReferenceNumber = firstReference ?? ReferenceNotRecorded,
            FinancialYearStart = input.FirstFinancialYearStart,
            Description = input.Description.Trim(),
            CreatedByUserId = actor.UserId,
            CreatedAtUtc = now,
            UpdatedByUserId = actor.UserId,
            UpdatedAtUtc = now,
            RowVersion = NewRowVersion(),
            Stages = ItProjectStages.NewStageRows(),
            FinancialYears =
            {
                // Chosen by hand, so the case reads as CL or CF from the start, before its stages
                // (and the SO date the automatic type needs) are entered.
                new ItProjectFinancialYear
                {
                    FinancialYearStart = currentYear,
                    PppReferenceNumber = currentReference,
                    TypeOverride = input.Type,
                    UpdatedByUserId = actor.UserId,
                    UpdatedAtUtc = now
                }
            }
        };

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        _db.ItProjects.Add(project);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ItProjects.Created", actor, project, new Dictionary<string, string?>
        {
            ["Title"] = project.Title,
            ["PppReferenceNumber"] = project.PppReferenceNumber,
            ["FinancialYear"] = FinancialYearHelper.Format(project.FinancialYearStart),
            ["CarriedOverInto"] = FinancialYearHelper.Format(currentYear),
            ["CarriedOverType"] = ItProjectYearTypes.Code(input.Type),
            ["CarriedOverPppReferenceNumber"] = currentReference,
            ["ProjectOfficerUserId"] = project.ProjectOfficerUserId
        });
        await transaction.CommitAsync(cancellationToken);

        return (ItProjectCommandResult.Ok(), project.Id);
    }

    private async Task<ItProjectCommandResult> UpdateDetailsCoreAsync(int id, ItProjectDetailsInput input, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureActor(actor);

        var project = await LoadForWriteAsync(id, actor.Module, cancellationToken);
        if (project is null)
        {
            return ItProjectCommandResult.Fail(NotFoundMessage);
        }

        var errors = ValidateDetails(input, project.Description);
        if (errors.Count > 0)
        {
            return ItProjectCommandResult.Fail(errors);
        }

        var before = new Dictionary<string, string?>
        {
            ["Title"] = project.Title,
            ["PppReferenceNumber"] = project.PppReferenceNumber,
            ["FinancialYear"] = FinancialYearHelper.Format(project.FinancialYearStart)
        };

        var previousYearStart = project.FinancialYearStart;
        project.Title = input.Title.Trim();
        project.PppReferenceNumber = input.PppReferenceNumber.Trim();
        project.FinancialYearStart = input.FinancialYearStart;
        project.Description = input.Description.Trim();

        // A later year's row (its IT ARPPP number and any type set by hand) describes a year the
        // project was carried into. When the project's own year moves to or past it, that row
        // would turn into a type set by hand on the project's own year, so it goes; the audit
        // entry keeps what it held. Only a change of year does this: a type set by hand on the
        // own year, kept so it can be put back to automatic, is left alone by other edits.
        var superseded = project.FinancialYears
            .Where(year => project.FinancialYearStart != previousYearStart && year.FinancialYearStart <= project.FinancialYearStart)
            .OrderBy(year => year.FinancialYearStart)
            .ToList();
        foreach (var year in superseded)
        {
            project.FinancialYears.Remove(year);
            _db.ItProjectFinancialYears.Remove(year);
        }

        var data = new Dictionary<string, string?>
        {
            ["BeforeTitle"] = before["Title"],
            ["BeforePppReferenceNumber"] = before["PppReferenceNumber"],
            ["BeforeFinancialYear"] = before["FinancialYear"],
            ["Title"] = project.Title,
            ["PppReferenceNumber"] = project.PppReferenceNumber,
            ["FinancialYear"] = FinancialYearHelper.Format(project.FinancialYearStart)
        };
        if (superseded.Count > 0)
        {
            data["RemovedYears"] = string.Join("; ", superseded.Select(year =>
                $"{FinancialYearHelper.Format(year.FinancialYearStart)}: " +
                $"{year.PppReferenceNumber ?? "no number"}, " +
                $"{(year.TypeOverride is { } type ? ItProjectYearTypes.Code(type) + " set by hand" : "automatic type")}"));
        }

        return await CommitAsync(project, rowVersion, actor, "ItProjects.Updated", data, cancellationToken);
    }

    private async Task<ItProjectStageSaveResult> SaveStagesCoreAsync(int id, IReadOnlyCollection<ItProjectStageInput> stages, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stages);
        EnsureActor(actor);

        var rules = ItProjectStageRules.Apply(stages, actor.Module);
        if (!rules.IsValid)
        {
            return new ItProjectStageSaveResult(ItProjectCommandResult.Fail(rules.Errors), Array.Empty<string>(), Array.Empty<string>());
        }

        var project = await LoadForWriteAsync(id, actor.Module, cancellationToken);
        if (project is null)
        {
            return new ItProjectStageSaveResult(ItProjectCommandResult.Fail(NotFoundMessage), Array.Empty<string>(), Array.Empty<string>());
        }

        if (project.DroppedOn.HasValue)
        {
            // A dropped case has no stage to move to; completing it here would also get round the
            // rule that a completed case cannot be dropped.
            return new ItProjectStageSaveResult(ItProjectCommandResult.Fail(DroppedStagesMessage), Array.Empty<string>(), Array.Empty<string>());
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

    private async Task<ItProjectCommandResult> DropCoreAsync(int id, DateOnly droppedOn, string reason, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
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

        if (droppedOn > ItProjectYearTypes.TodayIst(_clock.UtcNow))
        {
            return ItProjectCommandResult.Fail(DroppedInFutureMessage);
        }

        var project = await LoadForWriteAsync(id, actor.Module, cancellationToken);
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

    private async Task<ItProjectCommandResult> ReinstateCoreAsync(int id, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);
        var project = await LoadForWriteAsync(id, actor.Module, cancellationToken);
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

    private async Task<ItProjectCommandResult> SaveYearCoreAsync(int id, int financialYearStart, ItProjectYearInput input, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureActor(actor);
        if (input.Type is { } requested && !Enum.IsDefined(requested))
        {
            return ItProjectCommandResult.Fail("Choose New, CL or CF.");
        }

        var reference = string.IsNullOrWhiteSpace(input.PppReferenceNumber) ? null : input.PppReferenceNumber.Trim();
        if (reference is { Length: > ItProjectFinancialYear.ReferenceMaxLength })
        {
            return ItProjectCommandResult.Fail($"The IT ARPPP reference number must be {ItProjectFinancialYear.ReferenceMaxLength} characters or fewer.");
        }

        var project = await LoadForWriteAsync(id, actor.Module, cancellationToken);
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

        // The first year's number lives on the project itself, and a project always has one: a
        // number given for the own year replaces it (the Record card's first-year row and the Edit
        // drawer both change it), and no number leaves it as it is.
        var referenceBefore = before.IsOwnYear ? project.PppReferenceNumber : before.PppReferenceNumber;
        var ownReferenceChanged = before.IsOwnYear && reference is not null && !string.Equals(reference, project.PppReferenceNumber, StringComparison.Ordinal);
        if (ownReferenceChanged)
        {
            project.PppReferenceNumber = reference!;
        }

        // Only later years store a number. In a later year a type chosen by hand is kept even when
        // it is today's automatic type, so it stays put when the automatic type later changes (an
        // SO dated before the year began turns CF into CL); "Automatic" (null) stores none. The
        // own year is always New automatically, so there only a different type is stored.
        var storedReference = before.IsOwnYear ? null : reference;
        var manual = input.Type is { } chosen && (!before.IsOwnYear || chosen != before.AutomaticType) ? chosen : (ItProjectYearType?)null;
        var row = ItProjectYearTypes.YearRow(project, financialYearStart);
        var rowUnchanged = row?.TypeOverride == manual && row?.PppReferenceNumber == storedReference;

        if (rowUnchanged && !ownReferenceChanged)
        {
            return ItProjectCommandResult.Ok();
        }

        // When only the first year's number changed, the year row (if any) stays as it is.
        if (!rowUnchanged && manual is null && storedReference is null)
        {
            project.FinancialYears.Remove(row!);
            _db.ItProjectFinancialYears.Remove(row!);
        }
        else if (!rowUnchanged)
        {
            if (row is null)
            {
                row = new ItProjectFinancialYear { FinancialYearStart = financialYearStart };
                project.FinancialYears.Add(row);
            }

            row.TypeOverride = manual;
            row.PppReferenceNumber = storedReference;
            row.UpdatedByUserId = actor.UserId;
            row.UpdatedAtUtc = UtcNow();
        }

        var afterType = manual ?? before.AutomaticType;
        return await CommitAsync(project, rowVersion, actor, "ItProjects.YearChanged", new Dictionary<string, string?>
        {
            ["FinancialYear"] = FinancialYearHelper.Format(financialYearStart),
            ["TypeBefore"] = ItProjectYearTypes.Code(before.Type) + (before.IsManual ? " (set by hand)" : " (automatic)"),
            ["TypeAfter"] = ItProjectYearTypes.Code(afterType) + (manual is null ? " (automatic)" : " (set by hand)"),
            ["ReferenceBefore"] = referenceBefore,
            ["ReferenceAfter"] = before.IsOwnYear ? project.PppReferenceNumber : storedReference
        }, cancellationToken);
    }

    private async Task<ItProjectCommandResult> DeleteCoreAsync(int id, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);
        var project = await LoadForWriteAsync(id, actor.Module, cancellationToken);
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

    private async Task<ItProjectCommandResult> AssignOfficerCoreAsync(int id, string? officerUserId, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken)
    {
        EnsureActor(actor);
        var officerId = NormaliseOfficer(officerUserId);

        var project = await LoadForWriteAsync(id, actor.Module, cancellationToken);
        if (project is null)
        {
            return ItProjectCommandResult.Fail(NotFoundMessage);
        }

        if (!ItProjectModules.HasAssignedOfficer(project.Module))
        {
            return ItProjectCommandResult.Fail(NoOfficerInModuleMessage);
        }

        if (await OfficerErrorAsync(officerId, project.Module, cancellationToken) is { } officerError)
        {
            return ItProjectCommandResult.Fail(officerError);
        }

        var before = project.ProjectOfficerUserId;
        if (string.Equals(before, officerId, StringComparison.Ordinal))
        {
            return ItProjectCommandResult.Ok();
        }

        project.ProjectOfficerUserId = officerId;
        project.ProjectOfficerAssignedAtUtc = officerId is null ? null : UtcNow();

        var action = before is null ? "ItProjects.OfficerAssigned"
            : officerId is null ? "ItProjects.OfficerUnassigned"
            : "ItProjects.OfficerChanged";
        return await CommitAsync(project, rowVersion, actor, action, new Dictionary<string, string?>
        {
            ["Title"] = project.Title,
            ["OfficerBeforeUserId"] = before,
            ["OfficerBefore"] = await UserNameAsync(before, cancellationToken),
            ["OfficerAfterUserId"] = officerId,
            ["OfficerAfter"] = await UserNameAsync(officerId, cancellationToken)
        }, cancellationToken);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static string? NormaliseOfficer(string? userId) => string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();

    /// <summary>
    /// Why this user cannot be the project's officer, or null when they can (or none is asked
    /// for). An officer belongs only to a module that assigns one, and must be an active user
    /// who holds the Project Officer role, as <see cref="ItProjectAccess.IsAssignedOfficer"/>
    /// requires before it gives them any right.
    /// </summary>
    private async Task<string?> OfficerErrorAsync(string? officerId, string module, CancellationToken cancellationToken)
    {
        if (officerId is null)
        {
            return null;
        }

        if (!ItProjectModules.HasAssignedOfficer(module))
        {
            return NoOfficerInModuleMessage;
        }

        return await ItProjectOfficers.IsAssignableAsync(_db, officerId, cancellationToken) ? null : NotAnOfficerMessage;
    }

    private async Task<string?> UserNameAsync(string? userId, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return null;
        }

        return await _db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static List<string> ValidateDetails(ItProjectDetailsInput input, string? currentDescription)
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
            errors.Add("Give the IT ARPPP reference number.");
        }
        else if (reference.Length > ReferenceMaxLength)
        {
            errors.Add($"The IT ARPPP reference number must be {ReferenceMaxLength} characters or fewer.");
        }

        if (description.Length == 0)
        {
            errors.Add("Give the project a description.");
        }
        else if (description.Length > DescriptionMaxLength &&
                 !string.Equals(description, currentDescription?.Trim(), StringComparison.Ordinal))
        {
            errors.Add($"The description must be {DescriptionMaxLength} characters or fewer.");
        }

        if (input.FinancialYearStart is < FinancialYearHelper.MinimumSupportedStartYear or > FinancialYearHelper.MaximumSupportedStartYear)
        {
            errors.Add("Choose the financial year.");
        }

        return errors;
    }

    /// <summary>The live project with that id in the actor's module; a project of another module is "not found".</summary>
    private Task<ItProject?> LoadForWriteAsync(int id, string module, CancellationToken cancellationToken)
        => _db.ItProjects
            .Include(project => project.Stages)
            .Include(project => project.FinancialYears)
            .FirstOrDefaultAsync(project => project.Id == id && project.Module == module && !project.IsDeleted, cancellationToken);

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
        data["Module"] = project.Module;
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

        if (!ItProjectModules.IsKnown(actor.Module))
        {
            throw new InvalidOperationException("The module the user is acting in is not known.");
        }
    }

    private DateTime UtcNow() => _clock.UtcNow.UtcDateTime;

    private static byte[] NewRowVersion() => Guid.NewGuid().ToByteArray();
}

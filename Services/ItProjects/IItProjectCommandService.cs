using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.ItProjects;

namespace ProjectManagement.Services.ItProjects;

/// <summary>
/// Who is acting, and in which module (<see cref="ItProjectModules"/>). The user id is required;
/// the name is for the audit row. A command only reaches projects of the actor's module, so a
/// project id of another module is "not found".
/// </summary>
public sealed record ItProjectActor(string UserId, string? UserName = null, string Module = ItProjectModules.It);

public sealed record ItProjectDetailsInput(
    string Title,
    string PppReferenceNumber,
    int FinancialYearStart,
    string Description);

/// <summary>
/// A case that is already in this year's IT ARPPP as CL or CF: it was first listed in an earlier
/// year, whose IT ARPPP number may no longer be known.
/// </summary>
public sealed record ItProjectCarriedOverInput(
    string Title,
    string Description,
    ItProjectYearType Type,
    string CurrentYearPppReferenceNumber,
    int FirstFinancialYearStart,
    string? FirstPppReferenceNumber);

/// <summary>One year's IT ARPPP entry for a project: the reference number in that year and an optional type set by hand.</summary>
public sealed record ItProjectYearInput(ItProjectYearType? Type, string? PppReferenceNumber);

public sealed record ItProjectCommandResult(
    bool Succeeded,
    IReadOnlyList<string> Errors,
    bool ConcurrencyConflict = false)
{
    public static ItProjectCommandResult Ok() => new(true, Array.Empty<string>());
    public static ItProjectCommandResult Fail(params string[] errors) => new(false, errors);
    public static ItProjectCommandResult Fail(IReadOnlyList<string> errors) => new(false, errors);
    public static ItProjectCommandResult Conflict() => new(false, new[] { ItProjectCommandService.ConcurrencyConflictMessage }, ConcurrencyConflict: true);
}

public sealed record ItProjectStageSaveResult(
    ItProjectCommandResult Result,
    IReadOnlyList<string> AutoCompletedStageCodes,
    IReadOnlyList<string> ReopenedStageCodes);

public interface IItProjectCommandService
{
    /// <summary>Creates the project with its nine stage rows. Returns the new id, or the validation errors.</summary>
    Task<(ItProjectCommandResult Result, int Id)> CreateAsync(ItProjectDetailsInput input, ItProjectActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a case carried over into the current financial year as CL or CF: the project in the
    /// year it was first listed (its first number, or <see cref="ItProjectCommandService.ReferenceNotRecorded"/>),
    /// and this year's number and type, chosen by hand, in one transaction.
    /// </summary>
    Task<(ItProjectCommandResult Result, int Id)> CreateCarriedOverAsync(ItProjectCarriedOverInput input, ItProjectActor actor, CancellationToken cancellationToken = default);

    Task<ItProjectCommandResult> UpdateDetailsAsync(int id, ItProjectDetailsInput input, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default);

    /// <summary>Saves all nine stages in one go after <see cref="ItProjectStageRules"/> has run on them.</summary>
    Task<ItProjectStageSaveResult> SaveStagesAsync(int id, IReadOnlyCollection<ItProjectStageInput> stages, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default);

    Task<ItProjectCommandResult> DropAsync(int id, DateOnly droppedOn, string reason, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default);

    Task<ItProjectCommandResult> ReinstateAsync(int id, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves what one financial year's IT ARPPP says about the project: a later year's IT ARPPP
    /// reference number, and a New / CL / CF type set by hand (null or the automatic type means
    /// automatic). The first year's number stays on the project: a number given for that year
    /// replaces it (as Edit does), and no number leaves it unchanged.
    /// </summary>
    Task<ItProjectCommandResult> SaveYearAsync(int id, int financialYearStart, ItProjectYearInput input, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default);

    /// <summary>Soft delete. The row and its stages stay for audit and a later restore.</summary>
    Task<ItProjectCommandResult> DeleteAsync(int id, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default);
}

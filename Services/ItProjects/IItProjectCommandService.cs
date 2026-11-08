using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.ItProjects;

namespace ProjectManagement.Services.ItProjects;

/// <summary>Who is acting. The user id is required; the name is for the audit row.</summary>
public sealed record ItProjectActor(string UserId, string? UserName = null);

public sealed record ItProjectDetailsInput(
    string Title,
    string PppReferenceNumber,
    int FinancialYearStart,
    string Description);

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

    Task<ItProjectCommandResult> UpdateDetailsAsync(int id, ItProjectDetailsInput input, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default);

    /// <summary>Saves all nine stages in one go after <see cref="ItProjectStageRules"/> has run on them.</summary>
    Task<ItProjectStageSaveResult> SaveStagesAsync(int id, IReadOnlyCollection<ItProjectStageInput> stages, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default);

    Task<ItProjectCommandResult> DropAsync(int id, DateOnly droppedOn, string reason, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default);

    Task<ItProjectCommandResult> ReinstateAsync(int id, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default);

    /// <summary>Soft delete. The row and its stages stay for audit and a later restore.</summary>
    Task<ItProjectCommandResult> DeleteAsync(int id, byte[] rowVersion, ItProjectActor actor, CancellationToken cancellationToken = default);
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.ItProjects;

/// <summary>One remark as the project page shows it.</summary>
public sealed record ItProjectRemarkView(
    int Id,
    string AuthorUserId,
    string AuthorName,
    string AuthorRole,
    string Body,
    DateOnly EventDate,
    string? StageCode,
    DateTime CreatedAtUtc,
    DateTime? LastEditedAtUtc,
    byte[] RowVersion);

public sealed record ItProjectRemarkInput(string Body, DateOnly EventDate, string? StageCode);

/// <summary>Who is acting on a remark: the user, the remark role they hold (null for none), and whether they are an Admin.</summary>
public sealed record ItProjectRemarkActor(string UserId, string? UserName, string? RemarkRole, bool IsAdmin);

public interface IItProjectRemarkService
{
    /// <summary>Live remarks of a project, newest event first (then newest written first).</summary>
    Task<IReadOnlyList<ItProjectRemarkView>> ListAsync(int itProjectId, CancellationToken cancellationToken = default);

    /// <summary>Adds a remark as the actor's remark role. Fails when the actor holds no remark role.</summary>
    Task<(ItProjectCommandResult Result, int RemarkId)> AddAsync(int itProjectId, ItProjectRemarkInput input, ItProjectRemarkActor actor, CancellationToken cancellationToken = default);

    /// <summary>Edits the actor's own remark.</summary>
    Task<ItProjectCommandResult> EditAsync(int remarkId, ItProjectRemarkInput input, byte[] rowVersion, ItProjectRemarkActor actor, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the actor's own remark, or any remark when the actor is an Admin.</summary>
    Task<ItProjectCommandResult> DeleteAsync(int remarkId, byte[] rowVersion, ItProjectRemarkActor actor, CancellationToken cancellationToken = default);
}

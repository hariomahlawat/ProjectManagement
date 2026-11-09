using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.ItProjects;

namespace ProjectManagement.Services.ItProjects;

/// <summary>One remark as the project page shows it.</summary>
public sealed record ItProjectRemarkView(
    int Id,
    string AuthorUserId,
    string AuthorName,
    string AuthorRole,
    string Body,
    DateOnly EventDate,
    ItProjectRemarkType Type,
    DateTime CreatedAtUtc,
    DateTime? LastEditedAtUtc,
    byte[] RowVersion);

/// <summary>A remark to add or edit. The type is set when the remark is added and does not change on edit.</summary>
public sealed record ItProjectRemarkInput(string Body, DateOnly EventDate, ItProjectRemarkType Type = ItProjectRemarkType.General);

/// <summary>Who is acting on a remark: the user, the remark role they hold (null for none), and whether they are an Admin.</summary>
public sealed record ItProjectRemarkActor(string UserId, string? UserName, string? RemarkRole, bool IsAdmin);

public interface IItProjectRemarkService
{
    /// <summary>Live remarks of a project, newest event first (then newest written first).</summary>
    Task<IReadOnlyList<ItProjectRemarkView>> ListAsync(int itProjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a remark as the actor's remark role. Fails when the actor holds no remark role, or asks
    /// for a conference remark without being the Comdt or HoD.
    /// </summary>
    Task<(ItProjectCommandResult Result, int RemarkId)> AddAsync(int itProjectId, ItProjectRemarkInput input, ItProjectRemarkActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edits the actor's own remark (a conference remark only while they are the Comdt or HoD). The
    /// remark must belong to the given, live project. The type does not change.
    /// </summary>
    Task<ItProjectCommandResult> EditAsync(int itProjectId, int remarkId, ItProjectRemarkInput input, byte[] rowVersion, ItProjectRemarkActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes the actor's own remark (a conference remark only while they are the Comdt or
    /// HoD), or any remark when the actor is an Admin. The remark must belong to the given, live project.
    /// </summary>
    Task<ItProjectCommandResult> DeleteAsync(int itProjectId, int remarkId, byte[] rowVersion, ItProjectRemarkActor actor, CancellationToken cancellationToken = default);
}

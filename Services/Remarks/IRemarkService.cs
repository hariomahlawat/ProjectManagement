using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Remarks;

namespace ProjectManagement.Services.Remarks;

public interface IRemarkService
{
    Task<Remark> CreateRemarkAsync(CreateRemarkRequest request, CancellationToken cancellationToken = default);

    Task<RemarkListResult> ListRemarksAsync(ListRemarksRequest request, CancellationToken cancellationToken = default);

    Task<Remark?> EditRemarkAsync(int remarkId, EditRemarkRequest request, CancellationToken cancellationToken = default);

    Task<bool> SoftDeleteRemarkAsync(int remarkId, SoftDeleteRemarkRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemarkAudit>> GetRemarkAuditAsync(int remarkId, RemarkActorContext actor, CancellationToken cancellationToken = default);
}

public sealed record RemarkActorContext(string UserId, RemarkActorRole ActorRole, IReadOnlyCollection<RemarkActorRole> Roles);

public sealed record CreateRemarkRequest(
    int ProjectId,
    RemarkActorContext Actor,
    RemarkType Type,
    string Body,
    DateOnly EventDate,
    string? StageRef,
    string? StageNameSnapshot,
    string? Meta);

public sealed record ListRemarksRequest(
    int ProjectId,
    RemarkActorContext Actor,
    RemarkType? Type = null,
    RemarkActorRole? AuthorRole = null,
    string? StageRef = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    bool IncludeDeleted = false,
    int Page = 1,
    int PageSize = 50);

public sealed record RemarkListResult(int TotalCount, IReadOnlyList<Remark> Items, int Page, int PageSize);

public sealed record EditRemarkRequest(
    RemarkActorContext Actor,
    string Body,
    DateOnly EventDate,
    string? StageRef,
    string? StageNameSnapshot,
    string? Meta);

public sealed record SoftDeleteRemarkRequest(
    RemarkActorContext Actor,
    string? Meta);

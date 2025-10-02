using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ganss.Xss;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Remarks;

public sealed class RemarkService : IRemarkService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<RemarkService> _logger;

    public RemarkService(ApplicationDbContext db, IClock clock, ILogger<RemarkService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Remark> CreateRemarkAsync(CreateRemarkRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureActorContext(request.Actor);

        if (!await _db.Projects.AnyAsync(p => p.Id == request.ProjectId, cancellationToken))
        {
            throw new InvalidOperationException("Project not found.");
        }

        ValidateRoleForType(request.Actor.ActorRole, request.Actor.Roles, request.Type);

        var now = _clock.UtcNow.UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        EnsureValidEventDate(request.EventDate, today);

        var sanitizedBody = SanitizeBody(request.Body);
        if (string.IsNullOrWhiteSpace(sanitizedBody))
        {
            throw new InvalidOperationException("Remark body cannot be empty after sanitisation.");
        }

        var stage = await NormalizeStageAsync(request.ProjectId, request.StageRef, request.StageNameSnapshot, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var remark = new Remark
        {
            ProjectId = request.ProjectId,
            AuthorUserId = request.Actor.UserId,
            AuthorRole = request.Actor.ActorRole,
            Type = request.Type,
            Body = sanitizedBody,
            EventDate = request.EventDate,
            StageRef = stage.StageRef,
            StageNameSnapshot = stage.StageNameSnapshot,
            CreatedAtUtc = now,
            IsDeleted = false
        };

        _db.Remarks.Add(remark);
        await _db.SaveChangesAsync(cancellationToken);

        var audit = CreateAudit(remark, RemarkAuditAction.Created, request.Actor.ActorRole, request.Actor.UserId, now, request.Meta);
        _db.RemarkAudits.Add(audit);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogDecision("Create", true, null, request.Actor, remark.Id, remark.ProjectId);

        return remark;
    }

    public async Task<RemarkListResult> ListRemarksAsync(ListRemarksRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureActorContext(request.Actor);

        var query = _db.Remarks
            .AsNoTracking()
            .Where(r => r.ProjectId == request.ProjectId);

        var includeDeleted = request.IncludeDeleted && ActorHasAdmin(request.Actor.Roles);
        if (!includeDeleted)
        {
            query = query.Where(r => !r.IsDeleted);
            if (request.IncludeDeleted)
            {
                LogDecision("List", false, "IncludeDeletedNotPermitted", request.Actor, null, request.ProjectId);
            }
        }

        if (request.Type.HasValue)
        {
            query = query.Where(r => r.Type == request.Type.Value);
        }

        if (request.AuthorRole.HasValue)
        {
            query = query.Where(r => r.AuthorRole == request.AuthorRole.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.StageRef))
        {
            var stageRef = NormalizeStageRef(request.StageRef);
            query = query.Where(r => r.StageRef == stageRef);
        }

        if (request.FromDate.HasValue)
        {
            query = query.Where(r => r.EventDate >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(r => r.EventDate <= request.ToDate.Value);
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        LogDecision("List", true, includeDeleted ? "IncludeDeleted" : null, request.Actor, null, request.ProjectId);

        return new RemarkListResult(total, items, page, pageSize);
    }

    public async Task<Remark?> EditRemarkAsync(int remarkId, EditRemarkRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureActorContext(request.Actor);

        var remark = await _db.Remarks.FirstOrDefaultAsync(r => r.Id == remarkId, cancellationToken);
        if (remark is null || remark.IsDeleted)
        {
            return null;
        }

        var now = _clock.UtcNow.UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        EnsureValidEventDate(request.EventDate, today);

        if (!IsEditAllowed(remark, request.Actor, now))
        {
            LogDecision("Edit", false, "AuthorWindowExpired", request.Actor, remark.Id, remark.ProjectId);
            throw new InvalidOperationException("Not authorised to edit this remark.");
        }

        var sanitizedBody = SanitizeBody(request.Body);
        if (string.IsNullOrWhiteSpace(sanitizedBody))
        {
            throw new InvalidOperationException("Remark body cannot be empty after sanitisation.");
        }

        var stage = await NormalizeStageAsync(remark.ProjectId, request.StageRef, request.StageNameSnapshot, cancellationToken);

        remark.Body = sanitizedBody;
        remark.EventDate = request.EventDate;
        remark.StageRef = stage.StageRef;
        remark.StageNameSnapshot = stage.StageNameSnapshot;
        remark.LastEditedAtUtc = now;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var actorRole = ResolveActorRoleForAction(request.Actor);
        var audit = CreateAudit(remark, RemarkAuditAction.Edited, actorRole, request.Actor.UserId, now, request.Meta);
        _db.RemarkAudits.Add(audit);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogDecision("Edit", true, null, request.Actor, remark.Id, remark.ProjectId);

        return remark;
    }

    public async Task<bool> SoftDeleteRemarkAsync(int remarkId, SoftDeleteRemarkRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureActorContext(request.Actor);

        var remark = await _db.Remarks.FirstOrDefaultAsync(r => r.Id == remarkId, cancellationToken);
        if (remark is null || remark.IsDeleted)
        {
            return false;
        }

        var now = _clock.UtcNow.UtcDateTime;
        if (!IsEditAllowed(remark, request.Actor, now))
        {
            LogDecision("SoftDelete", false, "AuthorWindowExpired", request.Actor, remark.Id, remark.ProjectId);
            throw new InvalidOperationException("Not authorised to delete this remark.");
        }

        remark.IsDeleted = true;
        remark.DeletedAtUtc = now;
        remark.DeletedByUserId = request.Actor.UserId;
        remark.DeletedByRole = ResolveActorRoleForAction(request.Actor);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var audit = CreateAudit(remark, RemarkAuditAction.Deleted, remark.DeletedByRole ?? request.Actor.ActorRole, request.Actor.UserId, now, request.Meta);
        _db.RemarkAudits.Add(audit);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogDecision("SoftDelete", true, null, request.Actor, remark.Id, remark.ProjectId);

        return true;
    }

    public async Task<IReadOnlyList<RemarkAudit>> GetRemarkAuditAsync(int remarkId, RemarkActorContext actor, CancellationToken cancellationToken = default)
    {
        EnsureActorContext(actor);

        if (!ActorHasAdmin(actor.Roles))
        {
            LogDecision("GetAudit", false, "AdminOnly", actor, remarkId, null);
            throw new InvalidOperationException("Only administrators may view remark audits.");
        }

        var audits = await _db.RemarkAudits
            .AsNoTracking()
            .Where(a => a.RemarkId == remarkId)
            .OrderByDescending(a => a.ActionAtUtc)
            .ToListAsync(cancellationToken);

        LogDecision("GetAudit", true, null, actor, remarkId, null);

        return audits;
    }

    private static void EnsureActorContext(RemarkActorContext actor)
    {
        if (actor is null)
        {
            throw new ArgumentNullException(nameof(actor));
        }

        if (string.IsNullOrWhiteSpace(actor.UserId))
        {
            throw new ArgumentException("UserId is required.", nameof(actor));
        }

        if (actor.Roles is null)
        {
            throw new ArgumentException("Role set cannot be null.", nameof(actor));
        }

        if (actor.ActorRole == RemarkActorRole.Unknown || !actor.Roles.Contains(actor.ActorRole))
        {
            throw new InvalidOperationException("Actor role is not recognised or not granted to the user.");
        }
    }

    private static void EnsureValidEventDate(DateOnly eventDate, DateOnly today)
    {
        if (eventDate > today)
        {
            throw new InvalidOperationException("Event date cannot be in the future.");
        }
    }

    private static string SanitizeBody(string? body)
    {
        var trimmed = body?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Add("data");
        return sanitizer.Sanitize(trimmed).Trim();
    }

    private async Task<(string? StageRef, string? StageNameSnapshot)> NormalizeStageAsync(
        int projectId,
        string? stageRef,
        string? stageName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stageRef))
        {
            return (null, null);
        }

        var normalizedRef = NormalizeStageRef(stageRef);
        var name = !string.IsNullOrWhiteSpace(stageName) ? stageName.Trim() : StageCodes.DisplayNameOf(normalizedRef);

        var stageExists = await _db.ProjectStages
            .AsNoTracking()
            .AnyAsync(s => s.ProjectId == projectId && s.StageCode == normalizedRef, cancellationToken);

        if (!stageExists)
        {
            throw new InvalidOperationException("Stage reference is not part of this project.");
        }

        return (normalizedRef, name);
    }

    private static string NormalizeStageRef(string stageRef)
    {
        if (!StageCodes.All.Contains(stageRef, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Stage reference is not recognised.");
        }

        return stageRef.Trim().ToUpperInvariant();
    }

    private static bool IsEditAllowed(Remark remark, RemarkActorContext actor, DateTime nowUtc)
    {
        var hasOverride = HasOverride(actor.Roles);
        if (string.Equals(remark.AuthorUserId, actor.UserId, StringComparison.Ordinal))
        {
            if (nowUtc <= remark.CreatedAtUtc.AddHours(3))
            {
                return true;
            }

            return hasOverride;
        }

        return hasOverride;
    }

    private static bool HasOverride(IReadOnlyCollection<RemarkActorRole> roles)
        => roles.Contains(RemarkActorRole.HeadOfDepartment)
           || roles.Contains(RemarkActorRole.Commandant)
           || roles.Contains(RemarkActorRole.Administrator);

    private static bool ActorHasAdmin(IReadOnlyCollection<RemarkActorRole> roles)
        => roles.Contains(RemarkActorRole.Administrator);

    private static RemarkActorRole ResolveActorRoleForAction(RemarkActorContext actor)
    {
        if (actor.Roles.Contains(actor.ActorRole))
        {
            return actor.ActorRole;
        }

        if (actor.Roles.Contains(RemarkActorRole.HeadOfDepartment))
        {
            return RemarkActorRole.HeadOfDepartment;
        }

        if (actor.Roles.Contains(RemarkActorRole.Commandant))
        {
            return RemarkActorRole.Commandant;
        }

        if (actor.Roles.Contains(RemarkActorRole.Administrator))
        {
            return RemarkActorRole.Administrator;
        }

        return actor.ActorRole;
    }

    private static RemarkAudit CreateAudit(
        Remark remark,
        RemarkAuditAction action,
        RemarkActorRole actorRole,
        string actorUserId,
        DateTime actionAtUtc,
        string? meta)
        => new()
        {
            RemarkId = remark.Id,
            Action = action,
            SnapshotType = remark.Type,
            SnapshotAuthorRole = remark.AuthorRole,
            SnapshotAuthorUserId = remark.AuthorUserId,
            SnapshotEventDate = remark.EventDate,
            SnapshotStageRef = remark.StageRef,
            SnapshotStageName = remark.StageNameSnapshot,
            SnapshotBody = remark.Body,
            SnapshotCreatedAtUtc = remark.CreatedAtUtc,
            SnapshotLastEditedAtUtc = remark.LastEditedAtUtc,
            SnapshotIsDeleted = remark.IsDeleted,
            SnapshotDeletedAtUtc = remark.DeletedAtUtc,
            SnapshotDeletedByUserId = remark.DeletedByUserId,
            SnapshotDeletedByRole = remark.DeletedByRole,
            SnapshotProjectId = remark.ProjectId,
            ActorUserId = actorUserId,
            ActorRole = actorRole,
            ActionAtUtc = actionAtUtc,
            Meta = meta
        };

    private static void ValidateRoleForType(RemarkActorRole actorRole, IReadOnlyCollection<RemarkActorRole> grantedRoles, RemarkType type)
    {
        if (actorRole == RemarkActorRole.Unknown || !grantedRoles.Contains(actorRole))
        {
            throw new InvalidOperationException("Actor role is not recognised or not assigned.");
        }

        if (type == RemarkType.External && !grantedRoles.Any(r => r is RemarkActorRole.HeadOfDepartment or RemarkActorRole.Commandant or RemarkActorRole.Administrator))
        {
            throw new InvalidOperationException("External remarks require HoD, Comdt or Admin role.");
        }
    }

    private void LogDecision(string action, bool allowed, string? reason, RemarkActorContext actor, int? remarkId, int? projectId)
    {
        _logger.LogInformation(
            "RemarkDecision {Action} Allowed={Allowed} User={UserId} Role={Role} Remark={RemarkId} Project={ProjectId} Reason={Reason}",
            action,
            allowed,
            actor.UserId,
            actor.ActorRole,
            remarkId,
            projectId,
            reason);
    }
}

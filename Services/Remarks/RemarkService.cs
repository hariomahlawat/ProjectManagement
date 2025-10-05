using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using Ganss.Xss;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Remarks;

public sealed class RemarkService : IRemarkService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<RemarkService> _logger;
    private readonly IRemarkNotificationService _notification;
    private readonly IRemarkMetrics _metrics;
    private readonly UserManager<ApplicationUser> _userManager;

    public const string ConcurrencyConflictMessage = "This remark was changed by someone else. Reload to continue.";
    public const string RowVersionRequiredMessage = "Row version is required for this operation.";
    public const string PermissionDeniedMessage = "You do not have permission for this action.";
    public const string EditWindowMessage = "You can edit your remark within 3 hours of posting.";
    public const string DeleteWindowMessage = "You can delete your remark within 3 hours of posting.";
    public const string StageNotInProjectMessage = "Selected stage does not belong to this project.";

    private static readonly Regex MentionPlaceholderRegex = new("@\\[(?<name>[^\\]]+)\\]\\(user:(?<id>[^)]+)\\)", RegexOptions.Compiled);

    public RemarkService(
        ApplicationDbContext db,
        IClock clock,
        ILogger<RemarkService> logger,
        IRemarkNotificationService notification,
        IRemarkMetrics metrics,
        UserManager<ApplicationUser> userManager)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _notification = notification ?? throw new ArgumentNullException(nameof(notification));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    public async Task<Remark> CreateRemarkAsync(CreateRemarkRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureActorContext(request.Actor);

        var project = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == request.ProjectId)
            .Select(p => new RemarkProjectInfo(p.Id, p.Name, p.LeadPoUserId, p.HodUserId))
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            throw new InvalidOperationException("Project not found.");
        }

        ValidateRoleForType(request.Actor, request.Type, request.ProjectId);

        var now = _clock.UtcNow.UtcDateTime;
        var todayIst = DateOnly.FromDateTime(IstClock.ToIst(now));
        EnsureValidEventDate(request.EventDate, todayIst);

        var processedBody = await PrepareRemarkBodyAsync(request.Body, cancellationToken);
        if (string.IsNullOrWhiteSpace(processedBody.Body))
        {
            throw new InvalidOperationException("Remark body cannot be empty after sanitisation.");
        }

        var stage = await NormalizeStageAsync(request.ProjectId, request.StageRef, request.StageNameSnapshot, cancellationToken);

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

        var remark = new Remark
        {
            ProjectId = request.ProjectId,
            AuthorUserId = request.Actor.UserId,
            AuthorRole = request.Actor.ActorRole,
            Type = request.Type,
            Body = processedBody.Body,
            EventDate = request.EventDate,
            StageRef = stage.StageRef,
            StageNameSnapshot = stage.StageNameSnapshot,
            CreatedAtUtc = now,
            IsDeleted = false,
            Mentions = processedBody.MentionUserIds
                .Select(id => new RemarkMention { UserId = id })
                .ToList()
        };

        _db.Remarks.Add(remark);
        await _db.SaveChangesAsync(cancellationToken);

        var audit = CreateAudit(remark, RemarkAuditAction.Created, request.Actor.ActorRole, request.Actor.UserId, now, request.Meta);
        _db.RemarkAudits.Add(audit);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogDecision("Create", true, null, request.Actor, remark.Id, remark.ProjectId);
        _metrics.RecordCreated();

        try
        {
            await _notification.NotifyRemarkCreatedAsync(remark, request.Actor, project, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch remark notifications for remark {RemarkId}.", remark.Id);
        }

        return remark;
    }

    public async Task<RemarkListResult> ListRemarksAsync(ListRemarksRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureActorContext(request.Actor);

        var query = _db.Remarks
            .AsNoTracking()
            .Include(r => r.Mentions)
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

        if (request.Mine)
        {
            query = query.Where(r => r.AuthorUserId == request.Actor.UserId);
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

        var remark = await _db.Remarks
            .Include(r => r.Mentions)
            .FirstOrDefaultAsync(r => r.Id == remarkId, cancellationToken);
        if (remark is null || remark.IsDeleted)
        {
            return null;
        }

        var now = _clock.UtcNow.UtcDateTime;
        var todayIst = DateOnly.FromDateTime(IstClock.ToIst(now));
        EnsureValidEventDate(request.EventDate, todayIst);

        var editPermission = EvaluateRemarkPermission(remark, request.Actor, now, isDelete: false);
        if (!editPermission.Allowed)
        {
            if (string.Equals(editPermission.ReasonCode, "AuthorWindowExpired", StringComparison.Ordinal))
            {
                _metrics.RecordEditDeniedWindowExpired("Edit");
            }
            LogDecision("Edit", false, editPermission.ReasonCode, request.Actor, remark.Id, remark.ProjectId);
            throw new InvalidOperationException(editPermission.Message ?? PermissionDeniedMessage);
        }

        var processedBody = await PrepareRemarkBodyAsync(request.Body, cancellationToken);
        if (string.IsNullOrWhiteSpace(processedBody.Body))
        {
            throw new InvalidOperationException("Remark body cannot be empty after sanitisation.");
        }

        var stage = await NormalizeStageAsync(remark.ProjectId, request.StageRef, request.StageNameSnapshot, cancellationToken);

        remark.Body = processedBody.Body;
        remark.EventDate = request.EventDate;
        remark.StageRef = stage.StageRef;
        remark.StageNameSnapshot = stage.StageNameSnapshot;
        remark.LastEditedAtUtc = now;
        UpdateRemarkMentions(remark, processedBody.MentionUserIds);

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

        ApplyRowVersion(remark, request.RowVersion);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            LogDecision("Edit", false, "ConcurrencyConflict", request.Actor, remark.Id, remark.ProjectId);
            throw new InvalidOperationException(ConcurrencyConflictMessage, ex);
        }

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
        var deletePermission = EvaluateRemarkPermission(remark, request.Actor, now, isDelete: true);
        if (!deletePermission.Allowed)
        {
            LogDecision("SoftDelete", false, deletePermission.ReasonCode, request.Actor, remark.Id, remark.ProjectId);
            throw new InvalidOperationException(deletePermission.Message ?? PermissionDeniedMessage);
        }

        remark.IsDeleted = true;
        remark.DeletedAtUtc = now;
        remark.DeletedByUserId = request.Actor.UserId;
        remark.DeletedByRole = ResolveActorRoleForAction(request.Actor);

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

        ApplyRowVersion(remark, request.RowVersion);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            LogDecision("SoftDelete", false, "ConcurrencyConflict", request.Actor, remark.Id, remark.ProjectId);
            throw new InvalidOperationException(ConcurrencyConflictMessage, ex);
        }

        var audit = CreateAudit(remark, RemarkAuditAction.Deleted, remark.DeletedByRole ?? request.Actor.ActorRole, request.Actor.UserId, now, request.Meta);
        _db.RemarkAudits.Add(audit);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogDecision("SoftDelete", true, null, request.Actor, remark.Id, remark.ProjectId);
        _metrics.RecordDeleted();

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

    private async Task<ProcessedRemarkBody> PrepareRemarkBodyAsync(string? body, CancellationToken cancellationToken)
    {
        var trimmed = body?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new ProcessedRemarkBody(string.Empty, Array.Empty<string>());
        }

        var matches = MentionPlaceholderRegex.Matches(trimmed);
        var matchedIds = matches
            .Select(match => match.Groups["id"].Value.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var activeMentions = new HashSet<string>(StringComparer.Ordinal);
        if (matchedIds.Length > 0)
        {
            var activeIds = await _userManager.Users
                .AsNoTracking()
                .Where(u => matchedIds.Contains(u.Id) && !u.IsDisabled && !u.PendingDeletion)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            foreach (var id in activeIds)
            {
                activeMentions.Add(id);
            }
        }

        var orderedMentions = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var replaced = MentionPlaceholderRegex.Replace(trimmed, match =>
        {
            var userId = match.Groups["id"].Value.Trim();
            if (!activeMentions.Contains(userId))
            {
                return HtmlEncoder.Default.Encode(match.Value);
            }

            if (seen.Add(userId))
            {
                orderedMentions.Add(userId);
            }

            var display = match.Groups["name"].Value.Trim();
            if (display.Length == 0)
            {
                display = $"@{userId}";
            }

            var encodedId = HtmlEncoder.Default.Encode(userId);
            var encodedDisplay = HtmlEncoder.Default.Encode(display);
            return $"<span class=\"remark-mention\" data-user-id=\"{encodedId}\">{encodedDisplay}</span>";
        });

        var sanitized = SanitizeBody(replaced);

        if (orderedMentions.Count == 0)
        {
            return new ProcessedRemarkBody(sanitized, Array.Empty<string>());
        }

        var sanitizedMentions = orderedMentions
            .Where(activeMentions.Contains)
            .ToArray();

        return new ProcessedRemarkBody(sanitized, sanitizedMentions);
    }

    private static string SanitizeBody(string? body)
    {
        var trimmed = body?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var sanitizer = CreateSanitizer();
        return sanitizer.Sanitize(trimmed).Trim();
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Add("data");
        sanitizer.AllowedTags.Add("span");
        sanitizer.AllowedAttributes.Add("class");
        sanitizer.AllowedAttributes.Add("data-user-id");
        sanitizer.PostProcessNode += (_, args) =>
        {
            if (args.Node is not IElement element)
            {
                return;
            }

            if (!element.NodeName.Equals("SPAN", StringComparison.OrdinalIgnoreCase))
            {
                if (element.HasAttribute("data-user-id"))
                {
                    element.RemoveAttribute("data-user-id");
                }

                if (element.HasAttribute("class") && !element.ClassList.Contains("remark-mention"))
                {
                    element.RemoveAttribute("class");
                }

                return;
            }

            if (!element.ClassList.Contains("remark-mention"))
            {
                element.RemoveAttribute("class");
                element.RemoveAttribute("data-user-id");
                return;
            }

            var userId = element.GetAttribute("data-user-id");
            if (string.IsNullOrWhiteSpace(userId))
            {
                element.RemoveAttribute("data-user-id");
                element.ClassList.Remove("remark-mention");
            }
            else
            {
                element.SetAttribute("data-user-id", userId.Trim());
            }
        };

        return sanitizer;
    }

    private void UpdateRemarkMentions(Remark remark, IReadOnlyCollection<string> mentionUserIds)
    {
        var desired = mentionUserIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        var current = remark.Mentions.ToList();
        foreach (var existing in current)
        {
            if (!desired.Contains(existing.UserId))
            {
                _db.RemarkMentions.Remove(existing);
                remark.Mentions.Remove(existing);
            }
        }

        foreach (var userId in desired)
        {
            if (!remark.Mentions.Any(m => string.Equals(m.UserId, userId, StringComparison.Ordinal)))
            {
                remark.Mentions.Add(new RemarkMention { UserId = userId });
            }
        }
    }

    private sealed record ProcessedRemarkBody(string Body, IReadOnlyList<string> MentionUserIds);

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
            throw new InvalidOperationException(StageNotInProjectMessage);
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

    private static (bool Allowed, string? Message, string? ReasonCode) EvaluateRemarkPermission(
        Remark remark,
        RemarkActorContext actor,
        DateTime nowUtc,
        bool isDelete)
    {
        if (HasOverride(actor.Roles))
        {
            return (true, null, null);
        }

        var isAuthor = string.Equals(remark.AuthorUserId, actor.UserId, StringComparison.Ordinal);
        if (!isAuthor)
        {
            return (false, PermissionDeniedMessage, "NotAuthor");
        }

        if (nowUtc <= remark.CreatedAtUtc.AddHours(3))
        {
            return (true, null, null);
        }

        return (false, isDelete ? DeleteWindowMessage : EditWindowMessage, "AuthorWindowExpired");
    }

    private static bool HasOverride(IReadOnlyCollection<RemarkActorRole> roles)
        => roles.Contains(RemarkActorRole.HeadOfDepartment)
           || roles.Contains(RemarkActorRole.Commandant)
           || roles.Contains(RemarkActorRole.Administrator);

    private static bool ActorHasAdmin(IReadOnlyCollection<RemarkActorRole> roles)
        => roles.Contains(RemarkActorRole.Administrator);

    private void ApplyRowVersion(Remark remark, byte[]? rowVersion)
    {
        if (remark is null)
        {
            throw new ArgumentNullException(nameof(remark));
        }

        if (rowVersion is not { Length: > 0 })
        {
            throw new InvalidOperationException(RowVersionRequiredMessage);
        }

        var entry = _db.Entry(remark);
        entry.Property(r => r.RowVersion).OriginalValue = rowVersion;
    }

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

    private void ValidateRoleForType(RemarkActorContext actor, RemarkType type, int projectId)
    {
        if (actor.ActorRole == RemarkActorRole.Unknown || !actor.Roles.Contains(actor.ActorRole))
        {
            LogDecision("Create", false, "ActorRoleInvalid", actor, null, projectId);
            throw new InvalidOperationException("Actor role is not recognised or not assigned.");
        }

        if (type == RemarkType.External && !actor.Roles.Any(r => r is RemarkActorRole.HeadOfDepartment or RemarkActorRole.Commandant or RemarkActorRole.Administrator))
        {
            LogDecision("Create", false, "ExternalRequiresOverride", actor, null, projectId);
            throw new InvalidOperationException("External remarks require HoD, Comdt or Admin role.");
        }
    }

    private void LogDecision(string action, bool allowed, string? reason, RemarkActorContext actor, int? remarkId, int? projectId)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? (allowed ? "Success" : "Unknown")
            : reason;

        if (!allowed)
        {
            _metrics.RecordPermissionDenied(action, normalizedReason);
        }

        var entry = new RemarkDecisionLog(
            Action: action,
            Allowed: allowed,
            Reason: normalizedReason,
            UserId: actor.UserId,
            Role: actor.ActorRole,
            RemarkId: remarkId,
            ProjectId: projectId);

        _logger.LogInformation("RemarkDecision {@Decision}", entry);
    }

    private sealed record RemarkDecisionLog(
        string Action,
        bool Allowed,
        string Reason,
        string UserId,
        RemarkActorRole Role,
        int? RemarkId,
        int? ProjectId);
}

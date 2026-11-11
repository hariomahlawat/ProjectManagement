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

namespace ProjectManagement.Services.ItProjects;

/// <summary>
/// The only writer of IT project remarks. Every change writes the remark, an immutable audit
/// snapshot and the audit log entry in one transaction (docs/ux/it-projects.md, "Remarks").
/// </summary>
public sealed class ItProjectRemarkService : IItProjectRemarkService
{
    public const string NotFoundMessage = "The remark could not be found.";
    public const string NoRoleMessage = "Only the ITO, MCO, HoD or Comdt can write remarks on IT projects.";
    public const string NotAuthorMessage = "Only the author can change this remark.";
    public const string NotAuthorOrAdminMessage = "Only the author, the HoD or an administrator can delete this remark.";
    public const string ConferenceRoleMessage = "Only the Comdt or HoD can write or change a conference remark.";

    /// <summary>Whether a remark role may write conference remarks (Comdt, HoD), as in the other modules.</summary>
    public static bool CanWriteConference(string? remarkRole)
        => remarkRole is not null && Configuration.Policies.ConferenceRemarks.ManageAllowedRoles.Contains(remarkRole, StringComparer.OrdinalIgnoreCase);
    public const int MinBodyLength = 2;

    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public ItProjectRemarkService(ApplicationDbContext db, IAuditService audit, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<IReadOnlyList<ItProjectRemarkView>> ListAsync(int itProjectId, CancellationToken cancellationToken = default)
        => ListAsync(itProjectId, ItProjectModules.It, cancellationToken);

    public async Task<IReadOnlyList<ItProjectRemarkView>> ListAsync(int itProjectId, string module, CancellationToken cancellationToken = default)
    {
        var rows = await _db.ItProjectRemarks.AsNoTracking()
            .Where(remark => remark.ItProjectId == itProjectId && !remark.IsDeleted && remark.ItProject != null && remark.ItProject.Module == module)
            .OrderByDescending(remark => remark.EventDate)
            .ThenByDescending(remark => remark.CreatedAtUtc)
            .Select(remark => new
            {
                remark.Id,
                remark.AuthorUserId,
                remark.AuthorRole,
                remark.Body,
                remark.EventDate,
                remark.Type,
                remark.CreatedAtUtc,
                remark.LastEditedAtUtc,
                remark.RowVersion
            })
            .ToListAsync(cancellationToken);

        var authorIds = rows.Select(row => row.AuthorUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(user => authorIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FullName, user.UserName })
            .ToDictionaryAsync(user => user.Id, user => string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? user.Id : user.FullName, cancellationToken);

        return rows
            .Select(row => new ItProjectRemarkView(
                row.Id,
                row.AuthorUserId,
                names.TryGetValue(row.AuthorUserId, out var name) ? name : row.AuthorUserId,
                row.AuthorRole,
                row.Body,
                row.EventDate,
                row.Type,
                row.CreatedAtUtc,
                row.LastEditedAtUtc,
                row.RowVersion))
            .ToList();
    }

    public async Task<(ItProjectCommandResult Result, int RemarkId)> AddAsync(int itProjectId, ItProjectRemarkInput input, ItProjectRemarkActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureActor(actor);

        if (string.IsNullOrWhiteSpace(actor.RemarkRole))
        {
            return (ItProjectCommandResult.Fail(NoRoleMessage), 0);
        }

        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return (ItProjectCommandResult.Fail(errors), 0);
        }

        if (input.Type == ItProjectRemarkType.Conference && !CanWriteConference(actor.RemarkRole))
        {
            return (ItProjectCommandResult.Fail(ConferenceRoleMessage), 0);
        }

        var project = await _db.ItProjects
            .AsNoTracking()
            .Include(candidate => candidate.Stages)
            .SingleOrDefaultAsync(candidate => candidate.Id == itProjectId && candidate.Module == actor.Module && !candidate.IsDeleted, cancellationToken);
        if (project is null)
        {
            return (ItProjectCommandResult.Fail(ItProjectCommandService.NotFoundMessage), 0);
        }

        // A conference direction records the stage the case was at when it was issued, as the
        // other modules do ("Stage when issued"); a general remark is not tied to a stage.
        var stageWhenIssued = input.Type == ItProjectRemarkType.Conference
            ? ItProjectStatus.Resolve(project).CurrentStageCode
            : null;

        var now = UtcNow();
        var remark = new ItProjectRemark
        {
            ItProjectId = itProjectId,
            AuthorUserId = actor.UserId,
            AuthorRole = actor.RemarkRole!,
            Body = input.Body.Trim(),
            EventDate = input.EventDate,
            Type = input.Type,
            StageCode = stageWhenIssued,
            CreatedAtUtc = now,
            RowVersion = NewRowVersion()
        };
        remark.AuditEntries.Add(Snapshot(remark, ItProjectRemarkAuditAction.Created, actor, now));

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        _db.ItProjectRemarks.Add(remark);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("ItProjects.RemarkAdded", userId: actor.UserId, userName: actor.UserName, data: Data(remark));
        await transaction.CommitAsync(cancellationToken);

        return (ItProjectCommandResult.Ok(), remark.Id);
    }

    public async Task<ItProjectCommandResult> EditAsync(int itProjectId, int remarkId, ItProjectRemarkInput input, byte[] rowVersion, ItProjectRemarkActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureActor(actor);

        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return ItProjectCommandResult.Fail(errors);
        }

        var remark = await LoadAsync(itProjectId, remarkId, actor.Module, cancellationToken);
        if (remark is null)
        {
            return ItProjectCommandResult.Fail(NotFoundMessage);
        }

        if (!string.Equals(remark.AuthorUserId, actor.UserId, StringComparison.Ordinal))
        {
            return ItProjectCommandResult.Fail(NotAuthorMessage);
        }

        if (remark.Type == ItProjectRemarkType.Conference && !CanWriteConference(actor.RemarkRole))
        {
            return ItProjectCommandResult.Fail(ConferenceRoleMessage);
        }

        // The type and any legacy stage stay as they are: remarks are no longer tied to a stage.
        var now = UtcNow();
        remark.Body = input.Body.Trim();
        remark.EventDate = input.EventDate;
        remark.LastEditedAtUtc = now;
        remark.AuditEntries.Add(Snapshot(remark, ItProjectRemarkAuditAction.Edited, actor, now));

        return await CommitAsync(remark, rowVersion, actor, "ItProjects.RemarkEdited", cancellationToken);
    }

    public async Task<ItProjectCommandResult> DeleteAsync(int itProjectId, int remarkId, byte[] rowVersion, ItProjectRemarkActor actor, CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);

        var remark = await LoadAsync(itProjectId, remarkId, actor.Module, cancellationToken);
        if (remark is null)
        {
            return ItProjectCommandResult.Fail(NotFoundMessage);
        }

        // Owner decision 11 Nov 2026: an Admin or the HoD may delete any remark; everyone else,
        // the Comdt included, only their own.
        var isAuthor = string.Equals(remark.AuthorUserId, actor.UserId, StringComparison.Ordinal);
        if (!actor.CanDeleteAnyRemark)
        {
            if (!isAuthor)
            {
                return ItProjectCommandResult.Fail(NotAuthorOrAdminMessage);
            }

            if (remark.Type == ItProjectRemarkType.Conference && !CanWriteConference(actor.RemarkRole))
            {
                return ItProjectCommandResult.Fail(ConferenceRoleMessage);
            }
        }

        var now = UtcNow();
        remark.IsDeleted = true;
        remark.DeletedAtUtc = now;
        remark.DeletedByUserId = actor.UserId;
        remark.AuditEntries.Add(Snapshot(remark, ItProjectRemarkAuditAction.Deleted, actor, now));

        return await CommitAsync(remark, rowVersion, actor, "ItProjects.RemarkDeleted", cancellationToken, new Dictionary<string, string?>
        {
            ["DeletedByAuthor"] = isAuthor ? "true" : "false"
        });
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static List<string> Validate(ItProjectRemarkInput input)
    {
        var errors = new List<string>();
        var body = input.Body?.Trim() ?? string.Empty;
        if (body.Length < MinBodyLength)
        {
            errors.Add("Write the remark.");
        }
        else if (body.Length > ItProjectRemark.BodyMaxLength)
        {
            errors.Add($"The remark must be {ItProjectRemark.BodyMaxLength} characters or fewer.");
        }

        if (!Enum.IsDefined(input.Type))
        {
            errors.Add("Choose a remark or a conference remark.");
        }

        return errors;
    }

    /// <summary>
    /// The live remark with that id on that live project. A remark id from another project, or
    /// from a soft-deleted project, is "not found": the route's project is the one the caller
    /// has been authorised for (engineering invariant 6).
    /// </summary>
    private Task<ItProjectRemark?> LoadAsync(int itProjectId, int remarkId, string module, CancellationToken cancellationToken)
        => _db.ItProjectRemarks
            .Include(remark => remark.AuditEntries)
            .FirstOrDefaultAsync(remark =>
                remark.Id == remarkId &&
                remark.ItProjectId == itProjectId &&
                !remark.IsDeleted &&
                remark.ItProject != null &&
                remark.ItProject.Module == module &&
                !remark.ItProject.IsDeleted,
                cancellationToken);

    private async Task<ItProjectCommandResult> CommitAsync(ItProjectRemark remark, byte[] rowVersion, ItProjectRemarkActor actor, string action, CancellationToken cancellationToken, Dictionary<string, string?>? extra = null)
    {
        if (rowVersion is null || rowVersion.Length == 0)
        {
            return ItProjectCommandResult.Conflict();
        }

        _db.Entry(remark).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;
        remark.RowVersion = NewRowVersion();

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ItProjectCommandResult.Conflict();
        }

        var data = Data(remark);
        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                data[pair.Key] = pair.Value;
            }
        }

        await _audit.LogAsync(action, userId: actor.UserId, userName: actor.UserName, data: data);
        await transaction.CommitAsync(cancellationToken);
        return ItProjectCommandResult.Ok();
    }

    private static ItProjectRemarkAudit Snapshot(ItProjectRemark remark, ItProjectRemarkAuditAction action, ItProjectRemarkActor actor, DateTime now) => new()
    {
        Action = action,
        ActorUserId = actor.UserId,
        ActorRole = actor.RemarkRole ?? (actor.IsAdmin ? "Admin" : null),
        ActionAtUtc = now,
        SnapshotBody = remark.Body,
        SnapshotEventDate = remark.EventDate,
        SnapshotType = remark.Type,
        SnapshotStageCode = remark.StageCode,
        SnapshotIsDeleted = remark.IsDeleted
    };

    private static Dictionary<string, string?> Data(ItProjectRemark remark) => new()
    {
        ["ItProjectId"] = remark.ItProjectId.ToString(CultureInfo.InvariantCulture),
        ["RemarkId"] = remark.Id.ToString(CultureInfo.InvariantCulture),
        ["AuthorRole"] = remark.AuthorRole,
        ["EventDate"] = remark.EventDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["Type"] = remark.Type.ToString()
    };

    private static void EnsureActor(ItProjectRemarkActor actor)
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

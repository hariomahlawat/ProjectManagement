using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Services.DocRepo;

public interface IDocRepoAuditService
{
    /// <summary>Adds the audit row and saves it on its own.</summary>
    Task WriteAsync(Guid? documentId, string actorUserId, string eventType, object details, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the audit row to the context without saving, so the caller's next SaveChanges
    /// commits the change and its audit together.
    /// </summary>
    DocRepoAudit Stage(Guid? documentId, string actorUserId, string eventType, object details);

    /// <summary>
    /// Records an access event (a view or a download) at most once per user, document and event
    /// within <paramref name="window"/>. Failures are reported to the caller as false so a
    /// broken audit write never blocks reading a document.
    /// </summary>
    Task<bool> WriteAccessOnceAsync(Guid documentId, string actorUserId, string eventType, TimeSpan window, CancellationToken cancellationToken = default);
}

public sealed class DocRepoAuditService : IDocRepoAuditService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ApplicationDbContext _db;

    public DocRepoAuditService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task WriteAsync(Guid? documentId, string actorUserId, string eventType, object details, CancellationToken cancellationToken = default)
    {
        Stage(documentId, actorUserId, eventType, details);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public DocRepoAudit Stage(Guid? documentId, string actorUserId, string eventType, object details)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("Actor user ID is required", nameof(actorUserId));
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("Event type is required", nameof(eventType));
        }

        var entry = new DocRepoAudit
        {
            DocumentId = documentId,
            ActorUserId = actorUserId,
            EventType = eventType,
            DetailsJson = JsonSerializer.Serialize(details ?? new { }, SerializerOptions),
            OccurredAtUtc = DateTimeOffset.UtcNow
        };
        _db.DocRepoAudits.Add(entry);
        return entry;
    }

    public async Task<bool> WriteAccessOnceAsync(Guid documentId, string actorUserId, string eventType, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var windowStart = DateTimeOffset.UtcNow - window;
        DocRepoAudit? entry = null;
        try
        {
            var alreadyRecorded = await _db.DocRepoAudits
                .AsNoTracking()
                .AnyAsync(audit =>
                    audit.DocumentId == documentId
                    && audit.ActorUserId == actorUserId
                    && audit.EventType == eventType
                    && audit.OccurredAtUtc >= windowStart,
                    cancellationToken);

            if (alreadyRecorded)
            {
                return true;
            }

            entry = Stage(documentId, actorUserId, eventType, new { documentId });
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Leave nothing behind for a later SaveChanges to retry.
            if (entry is not null)
            {
                _db.Entry(entry).State = EntityState.Detached;
            }

            return false;
        }
    }
}

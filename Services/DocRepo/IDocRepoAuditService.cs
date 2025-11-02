using System.Text.Json;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Services.DocRepo;

public interface IDocRepoAuditService
{
    Task WriteAsync(Guid? documentId, string actorUserId, string eventType, object details, CancellationToken cancellationToken = default);
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

        await _db.DocRepoAudits.AddAsync(entry, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Admin;

public sealed record AdminAuditEntry(
    string Action,
    string EntityType,
    string? EntityId = null,
    object? Before = null,
    object? After = null,
    string? Reason = null,
    string Outcome = "Succeeded",
    string Level = "Info",
    string? Message = null,
    string? ActorUserId = null,
    string? ActorName = null,
    string? Origin = null);

public interface IAdminAuditService
{
    Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default);
}

public sealed class AdminAuditService : IAdminAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAuditService _audit;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AdminAuditService(IAuditService audit, IHttpContextAccessor httpContextAccessor)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        var http = _httpContextAccessor.HttpContext;
        var principal = http?.User;
        var actorUserId = entry.ActorUserId ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var actorName = entry.ActorName ?? principal?.Identity?.Name;
        var actorRoles = principal?.Claims
            .Where(claim => claim.Type == ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role)
            .ToArray() ?? Array.Empty<string>();

        var data = new Dictionary<string, string?>
        {
            ["EntityType"] = entry.EntityType,
            ["EntityId"] = entry.EntityId,
            ["ActorUserId"] = actorUserId,
            ["ActorName"] = actorName,
            ["ActorRoles"] = string.Join(',', actorRoles),
            ["Reason"] = entry.Reason,
            ["Outcome"] = entry.Outcome,
            ["TraceId"] = http?.TraceIdentifier,
            ["Origin"] = entry.Origin ?? http?.Request.Path.Value,
            ["Before"] = entry.Before is null ? null : JsonSerializer.Serialize(entry.Before, JsonOptions),
            ["After"] = entry.After is null ? null : JsonSerializer.Serialize(entry.After, JsonOptions)
        };

        return _audit.LogAsync(
            entry.Action,
            entry.Message,
            entry.Level,
            actorUserId,
            actorName,
            data,
            http);
    }
}

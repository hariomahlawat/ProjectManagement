using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ProjectManagement.Services.Admin;

public sealed record AdminAuditEntityLink(string Text, string Href);

public interface IAdminAuditEntityLinkResolver
{
    AdminAuditEntityLink? Resolve(HttpContext httpContext, AdminAuditPayload payload);
}

public sealed class AdminAuditEntityLinkResolver : IAdminAuditEntityLinkResolver
{
    private readonly LinkGenerator _links;

    public AdminAuditEntityLinkResolver(LinkGenerator links)
    {
        _links = links ?? throw new ArgumentNullException(nameof(links));
    }

    public AdminAuditEntityLink? Resolve(HttpContext httpContext, AdminAuditPayload payload)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(payload);

        if (string.IsNullOrWhiteSpace(payload.EntityType)
            || string.IsNullOrWhiteSpace(payload.EntityId))
        {
            return null;
        }

        if (payload.EntityType.Contains("User", StringComparison.OrdinalIgnoreCase))
        {
            var path = _links.GetPathByPage(
                httpContext,
                page: "/Users/Details",
                values: new { area = "Admin", id = payload.EntityId });
            return string.IsNullOrWhiteSpace(path) ? null : new("View user account", path);
        }

        if (string.Equals(payload.EntityType, "Project", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(payload.EntityId, out var projectId))
        {
            var path = _links.GetPathByPage(
                httpContext,
                page: "/Projects/Overview",
                values: new { area = string.Empty, id = projectId });
            return string.IsNullOrWhiteSpace(path) ? null : new("View project", path);
        }

        if (payload.EntityType.Contains("Document", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(payload.EntityId, out var documentId))
        {
            var path = _links.GetPathByPage(
                httpContext,
                page: "/Documents/Manage",
                values: new { area = "DocumentRepository", id = documentId });
            return string.IsNullOrWhiteSpace(path) ? null : new("View document", path);
        }

        return null;
    }
}

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

        if (TryResolveMasterData(httpContext, payload, out var masterDataLink))
        {
            return masterDataLink;
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

    private bool TryResolveMasterData(
        HttpContext httpContext,
        AdminAuditPayload payload,
        out AdminAuditEntityLink? link)
    {
        link = null;
        var entityType = payload.EntityType?.Trim();
        var entityId = payload.EntityId?.Trim();
        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
        {
            return false;
        }

        string? page = entityType.ToLowerInvariant() switch
        {
            "projectcategory" => "/Categories/Edit",
            "technicalcategory" => "/TechnicalCategories/Edit",
            "projecttype" => "/Lookups/ProjectTypes/Edit",
            "sponsoringunit" => "/Lookups/SponsoringUnits/Edit",
            "linedirectorate" => "/Lookups/LineDirectorates/Edit",
            "activitytype" => "/ActivityTypes/Edit",
            _ => null
        };

        string area;
        object values;
        string text;
        if (page is not null && int.TryParse(entityId, out var numericId))
        {
            area = "Admin";
            values = new { area, id = numericId };
            text = "View master-data record";
        }
        else if (string.Equals(entityType, "Holiday", StringComparison.OrdinalIgnoreCase)
                 && int.TryParse(entityId, out var holidayId))
        {
            page = "/Settings/Holidays/Edit";
            values = new { area = string.Empty, id = holidayId };
            text = "View holiday";
        }
        else if (string.Equals(entityType, "Celebration", StringComparison.OrdinalIgnoreCase)
                 && Guid.TryParse(entityId, out var celebrationId))
        {
            page = "/Celebrations/Edit";
            values = new { area = string.Empty, id = celebrationId };
            text = "View celebration";
        }
        else
        {
            return false;
        }

        var path = _links.GetPathByPage(httpContext, page: page!, values: values);
        if (string.IsNullOrWhiteSpace(path)) return false;
        link = new AdminAuditEntityLink(text, path);
        return true;
    }
}

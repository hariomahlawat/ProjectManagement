using Microsoft.AspNetCore.Mvc;

namespace ProjectManagement.Contracts.Notifications;

public sealed record NotificationListRequest(
    [property: FromQuery(Name = "limit")] int? Limit,
    [property: FromQuery(Name = "unreadOnly")] bool? UnreadOnly,
    [property: FromQuery(Name = "projectId")] int? ProjectId,
    [property: FromQuery(Name = "cursor")] string? Cursor,
    [property: FromQuery(Name = "status")] string? Status,
    [property: FromQuery(Name = "search")] string? Search,
    [property: FromQuery(Name = "module")] string? Module,
    [property: FromQuery(Name = "includeMuted")] bool? IncludeMuted,
    [property: FromQuery(Name = "includeFilterOptions")] bool? IncludeFilterOptions);

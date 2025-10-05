using Microsoft.AspNetCore.Mvc;

namespace ProjectManagement.Contracts.Notifications;

public sealed record NotificationListRequest(
    [property: FromQuery(Name = "limit")] int? Limit,
    [property: FromQuery(Name = "unreadOnly")] bool? UnreadOnly,
    [property: FromQuery(Name = "projectId")] int? ProjectId);

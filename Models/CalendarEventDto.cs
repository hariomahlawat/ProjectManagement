using System;

namespace ProjectManagement.Models;

public record EventRequest(
    string Title,
    string? Description,
    EventCategory Category,
    string? Location,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsAllDay);

public record EventUpdateRequest(
    string? Title,
    string? Description,
    EventCategory? Category,
    string? Location,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsAllDay);

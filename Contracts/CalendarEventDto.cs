namespace ProjectManagement.Contracts;

public record CalendarEventDto(
    string Title,
    string? Description,
    string Category,
    string? Location,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsAllDay
);

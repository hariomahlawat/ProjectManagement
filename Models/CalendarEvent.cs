using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

public enum EventCategory : byte
{
    Training = 0,
    Holiday = 1,
    TownHall = 2,
    Hiring = 3,
    Other = 4
}

public class CalendarEvent
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public EventCategory Category { get; set; }

    [MaxLength(160)]
    public string? Location { get; set; }

    public DateTimeOffset StartUtc { get; set; }

    public DateTimeOffset EndUtc { get; set; }

    public bool IsAllDay { get; set; }

    [MaxLength(64)]
    public string? CreatedById { get; set; }

    [MaxLength(64)]
    public string? UpdatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
}


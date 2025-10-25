using System;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;

public sealed class TrainingDeleteRequestDto
{
    public Guid Id { get; init; }

    public Guid TrainingId { get; init; }

    public string TrainingTypeName { get; init; } = string.Empty;

    public string Period { get; init; } = string.Empty;

    public int TotalTrainees { get; init; }

    public string RequestedByUserId { get; init; } = string.Empty;

    public string RequestedByDisplayName { get; init; } = string.Empty;

    public DateTimeOffset RequestedAtUtc { get; init; }

    public string Reason { get; init; } = string.Empty;
}

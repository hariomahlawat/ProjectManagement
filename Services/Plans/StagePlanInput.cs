using System;

namespace ProjectManagement.Services.Plans;

public sealed class StagePlanInput
{
    public required string StageCode { get; init; }
    public DateOnly? PlannedStart { get; init; }
    public DateOnly? PlannedDue { get; init; }
}

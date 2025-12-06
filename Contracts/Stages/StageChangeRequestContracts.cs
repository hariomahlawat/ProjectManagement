using System;
using System.Collections.Generic;

namespace ProjectManagement.Contracts.Stages;

public sealed record StageChangeRequestInput
{
    public int ProjectId { get; init; }
    public string StageCode { get; init; } = string.Empty;
    public string RequestedStatus { get; init; } = string.Empty;
    public DateOnly? RequestedDate { get; init; }
    public string? Note { get; init; }
}

public sealed record StageChangeRequestItemInput
{
    public string StageCode { get; init; } = string.Empty;
    public string RequestedStatus { get; init; } = string.Empty;
    public DateOnly? RequestedDate { get; init; }
    public string? Note { get; init; }
}

public sealed record BatchStageChangeRequestInput
{
    public int ProjectId { get; init; }
    public IReadOnlyCollection<StageChangeRequestItemInput> Stages { get; init; }
        = Array.Empty<StageChangeRequestItemInput>();
}

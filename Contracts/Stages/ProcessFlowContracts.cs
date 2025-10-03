using System.Collections.Generic;

namespace ProjectManagement.Contracts.Stages;

public record ProcessFlowDto(
    string Version,
    IReadOnlyList<ProcessFlowNodeDto> Nodes,
    IReadOnlyList<ProcessFlowEdgeDto> Edges);

public record ProcessFlowNodeDto(
    string Code,
    string Name,
    int Sequence,
    bool Optional,
    string? ParallelGroup,
    IReadOnlyList<string> DependsOn);

public record ProcessFlowEdgeDto(
    string Source,
    string Target);

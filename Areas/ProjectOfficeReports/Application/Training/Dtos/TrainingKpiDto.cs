using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;

public sealed class TrainingKpiDto
{
    public int TotalTrainings { get; init; }

    public int TotalTrainees { get; init; }

    public IReadOnlyList<TrainingTypeKpi> ByType { get; init; } = Array.Empty<TrainingTypeKpi>();
}

public sealed class TrainingTypeKpi
{
    public Guid TypeId { get; init; }

    public string TypeName { get; init; } = string.Empty;

    public int Trainings { get; init; }

    public int Trainees { get; init; }
}

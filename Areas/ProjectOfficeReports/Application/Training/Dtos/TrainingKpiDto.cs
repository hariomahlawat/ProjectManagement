using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;

public sealed class TrainingKpiDto
{
    public int TotalTrainings { get; init; }

    public int TotalTrainees { get; init; }

    public IReadOnlyList<TrainingTypeKpi> ByType { get; init; } = Array.Empty<TrainingTypeKpi>();

    public IReadOnlyList<TechnicalCategoryKpi> ByTechnicalCategory { get; init; } = Array.Empty<TechnicalCategoryKpi>();
}

public sealed class TrainingTypeKpi
{
    public Guid TypeId { get; init; }

    public string TypeName { get; init; } = string.Empty;

    public int Trainings { get; init; }

    public int Trainees { get; init; }
}

public sealed class TechnicalCategoryKpi
{
    public int TechnicalCategoryId { get; init; }

    public string TechnicalCategoryName { get; init; } = string.Empty;

    public int Trainings { get; init; }

    public int Trainees { get; init; }
}

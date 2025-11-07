using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;

public sealed class TrainingKpiDto
{
    public int TotalTrainings { get; init; }

    public int TotalTrainees { get; init; }

    public IReadOnlyList<TrainingKpiByTypeDto> ByType { get; init; } = Array.Empty<TrainingKpiByTypeDto>();

    public IReadOnlyList<TrainingKpiByTechnicalCategoryDto> ByTechnicalCategory { get; init; } =
        Array.Empty<TrainingKpiByTechnicalCategoryDto>();

    public IReadOnlyList<TrainingYearBucketDto> ByTrainingYear { get; init; } = Array.Empty<TrainingYearBucketDto>();
}

public sealed record TrainingKpiByTypeDto(
    Guid TypeId,
    string TypeName,
    int Trainings,
    int Trainees);

public sealed record TrainingKpiByTechnicalCategoryDto(
    int TechnicalCategoryId,
    string TechnicalCategoryName,
    int Trainings,
    int Trainees);

public sealed record TrainingYearBucketDto(
    string TrainingYearLabel,
    int SimulatorTrainings,
    int DroneTrainings,
    int TotalTrainings,
    int TotalTrainees);

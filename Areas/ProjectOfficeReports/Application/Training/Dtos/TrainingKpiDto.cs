using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos
{
    public sealed class TrainingKpiDto
    {
        public int TotalTrainings { get; init; }

        public int TotalTrainees { get; init; }

        // simulator / drone etc.
        public IReadOnlyList<TrainingKpiByTypeDto> ByType { get; init; } =
            Array.Empty<TrainingKpiByTypeDto>();

        // cards under "Simulator technical trainings"
        public IReadOnlyList<TrainingKpiByTechnicalCategoryDto> ByTechnicalCategory { get; init; } =
            Array.Empty<TrainingKpiByTechnicalCategoryDto>();

        // data for the chart
        public IReadOnlyList<TrainingYearBucketDto> ByTrainingYear { get; init; } =
            Array.Empty<TrainingYearBucketDto>();
    }

    // ------------------------------------------------------------
    // KPI by training TYPE (Simulator / Drone / …)
    // extra 3 fields have DEFAULTS so old code keeps working
    // ------------------------------------------------------------
    public sealed record TrainingKpiByTypeDto(
        Guid TypeId,
        string TypeName,
        int Trainings,
        int Trainees,
        int Officers = 0,
        int Jcos = 0,
        int Ors = 0
    );

    // ------------------------------------------------------------
    // KPI by TECHNICAL CATEGORY
    // same idea: defaults
    // ------------------------------------------------------------
    public sealed record TrainingKpiByTechnicalCategoryDto(
        int TechnicalCategoryId,
        string TechnicalCategoryName,
        int Trainings,
        int Trainees,
        int Officers = 0,
        int Jcos = 0,
        int Ors = 0
    );

    // ------------------------------------------------------------
    // chart buckets (unchanged)
    // ------------------------------------------------------------
    public sealed record TrainingYearBucketDto(
        string TrainingYearLabel,
        int SimulatorTrainings,
        int DroneTrainings,
        int TotalTrainings,
        int TotalTrainees
    );
}

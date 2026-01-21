using System;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos
{
    // ------------------------------------------------------------
    // technical category -> project breakdown rows
    // ------------------------------------------------------------
    public sealed record TechnicalCategoryProjectBreakdownDto(
        int ProjectId,
        string ProjectName,
        int TrainingSessions);
}

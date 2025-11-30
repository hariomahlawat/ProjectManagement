using System;
using System.Collections.Generic;

namespace ProjectManagement.ViewModels.Dashboard
{
    // SECTION: My Projects portfolio view models
    public sealed class MyProjectTileVm
    {
        public int ProjectId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;
        public string? CoverUrl { get; init; }
    }

    public sealed class MyProjectCategorySectionVm
    {
        public string CategoryName { get; init; } = string.Empty;
        public int ProjectCount { get; init; }
        public IReadOnlyList<MyProjectTileVm> Projects { get; init; } = Array.Empty<MyProjectTileVm>();
    }

    public enum StagePdcAlertType
    {
        Overdue,
        DueSoon,
        NotSet
    }

    public sealed class StagePdcAlertVm
    {
        public int ProjectId { get; init; }
        public string ProjectName { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;
        public string CurrentStageCode { get; init; } = string.Empty;
        public DateTime? PdcDate { get; init; }
        public StagePdcAlertType AlertType { get; init; }
    }

    public sealed class MyProjectsVm
    {
        public IReadOnlyList<MyProjectCategorySectionVm> Sections { get; init; } = Array.Empty<MyProjectCategorySectionVm>();
        public IReadOnlyList<StagePdcAlertVm> StagePdcAlerts { get; init; } = Array.Empty<StagePdcAlertVm>();
    }
}

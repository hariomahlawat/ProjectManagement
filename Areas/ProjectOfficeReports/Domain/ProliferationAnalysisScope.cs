namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public enum ProliferationAnalysisScope
{
    All = 1,
    TechnicalCategory = 2,
    SelectedProjects = 3
}

public enum ProliferationAnalysisPeriodMode
{
    AllTime = 1,
    SingleYear = 2,
    YearRange = 3,
    CustomDates = 4
}

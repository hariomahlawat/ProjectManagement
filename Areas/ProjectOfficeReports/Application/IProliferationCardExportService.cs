using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application
{
    public interface IProliferationCardExportService
    {
        byte[] BuildProjectsRanking(ProliferationSummaryViewModel summary);
        byte[] BuildYearBreakdown(ProliferationSummaryViewModel summary);
    }
}

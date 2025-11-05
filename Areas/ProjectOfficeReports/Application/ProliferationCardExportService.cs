using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application
{
    public sealed class ProliferationCardExportService : IProliferationCardExportService
    {
        public byte[] BuildProjectsRanking(ProliferationSummaryViewModel summary)
        {
            var builder = new ProliferationProjectsCardExcelWorkbookBuilder();
            return builder.Build(summary);
        }

        public byte[] BuildYearBreakdown(ProliferationSummaryViewModel summary)
        {
            var builder = new ProliferationYearBreakdownCardExcelWorkbookBuilder();
            return builder.Build(summary);
        }
    }
}

using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationCardExportService : IProliferationCardExportService
{
    private readonly ProliferationProjectsCardExcelWorkbookBuilder _projectsBuilder;
    private readonly ProliferationYearBreakdownCardExcelWorkbookBuilder _yearBuilder;

    public ProliferationCardExportService(
        ProliferationProjectsCardExcelWorkbookBuilder projectsBuilder,
        ProliferationYearBreakdownCardExcelWorkbookBuilder yearBuilder)
    {
        _projectsBuilder = projectsBuilder ?? throw new ArgumentNullException(nameof(projectsBuilder));
        _yearBuilder = yearBuilder ?? throw new ArgumentNullException(nameof(yearBuilder));
    }

    public byte[] BuildProjectsRanking(
        ProliferationSummaryViewModel summary,
        ProliferationExportMetadata metadata)
        => _projectsBuilder.Build(summary, metadata);

    public byte[] BuildYearBreakdown(
        ProliferationSummaryViewModel summary,
        ProliferationExportMetadata metadata)
        => _yearBuilder.Build(summary, metadata);
}

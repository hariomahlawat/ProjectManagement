using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Utilities.Reporting
{
    // SECTION: Excel report builder contract
    public interface IProliferationReportExcelWorkbookBuilder
    {
        byte[] Build(
            ProliferationReportKind report,
            IReadOnlyList<(string Key, string Label)> columns,
            IReadOnlyList<IDictionary<string, object?>> rows,
            string title,
            IDictionary<string, string> filters);
    }
}

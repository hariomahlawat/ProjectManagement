namespace ProjectManagement.Services.Ffc.Exports;

public interface IFfcDetailedTableExportService
{
    FfcDetailedTableExportFile BuildWord(FfcDetailedTableExportContext context);

    FfcDetailedTableExportFile BuildExcel(FfcDetailedTableExportContext context);
}

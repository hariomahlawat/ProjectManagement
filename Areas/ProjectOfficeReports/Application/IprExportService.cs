using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Services;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IIprExportService
{
    Task<IprExportFile> ExportAsync(IprFilter filter, CancellationToken cancellationToken);
}

public sealed record IprExportFile(string FileName, string ContentType, byte[] Content)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed class IprExportService : IIprExportService
{
    private readonly IIprReadService _readService;
    private readonly IIprExcelWorkbookBuilder _workbookBuilder;
    private readonly IClock _clock;

    public IprExportService(
        IIprReadService readService,
        IIprExcelWorkbookBuilder workbookBuilder,
        IClock clock)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _workbookBuilder = workbookBuilder ?? throw new ArgumentNullException(nameof(workbookBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IprExportFile> ExportAsync(IprFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var rows = await _readService.GetExportAsync(filter, cancellationToken);
        var content = _workbookBuilder.Build(new IprExcelWorkbookContext(rows));
        var timestamp = _clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"patent-records-{timestamp}.xlsx";

        return new IprExportFile(fileName, IprExportFile.ExcelContentType, content);
    }
}

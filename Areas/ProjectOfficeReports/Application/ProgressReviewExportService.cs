using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Services;
using ProjectManagement.Services.Reports.ProgressReview;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

// =========================================================
// SECTION: Contracts
// =========================================================
public interface IProgressReviewExportService
{
    Task<PdfExport> ExportPdfAsync(
        ProgressReviewVm report,
        DateOnly rangeFrom,
        DateOnly rangeTo,
        CancellationToken cancellationToken);
}

public sealed record PdfExport(string FileName, byte[] Bytes);

// =========================================================
// SECTION: Service implementation
// =========================================================
public sealed class ProgressReviewExportService : IProgressReviewExportService
{
    private readonly IProgressReviewPdfReportBuilder _pdfReportBuilder;
    private readonly IClock _clock;

    public ProgressReviewExportService(IProgressReviewPdfReportBuilder pdfReportBuilder, IClock clock)
    {
        _pdfReportBuilder = pdfReportBuilder ?? throw new ArgumentNullException(nameof(pdfReportBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<PdfExport> ExportPdfAsync(
        ProgressReviewVm report,
        DateOnly rangeFrom,
        DateOnly rangeTo,
        CancellationToken cancellationToken)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var generatedAt = _clock.UtcNow;
        var context = new ProgressReviewPdfReportContext(report, generatedAt, rangeFrom, rangeTo);
        var content = _pdfReportBuilder.Build(context);
        var fileName = BuildFileName(rangeFrom, rangeTo);

        return Task.FromResult(new PdfExport(fileName, content));
    }

    private static string BuildFileName(DateOnly rangeFrom, DateOnly rangeTo)
    {
        var fromSegment = rangeFrom.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var toSegment = rangeTo.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return $"ProgressReview_{fromSegment}_{toSegment}.pdf";
    }
}

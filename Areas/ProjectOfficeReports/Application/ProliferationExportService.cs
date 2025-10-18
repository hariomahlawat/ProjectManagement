using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IProliferationExportService
{
    Task<ProliferationExportResult> ExportAsync(ProliferationExportRequest request, CancellationToken cancellationToken);
}

public sealed class ProliferationExportService : IProliferationExportService
{
    private readonly ILogger<ProliferationExportService> _logger;

    public ProliferationExportService(ILogger<ProliferationExportService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<ProliferationExportResult> ExportAsync(ProliferationExportRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        _logger.LogWarning(
            "Proliferation export requested by {UserId}, but no export builder has been configured.",
            request.RequestedByUserId);

        return Task.FromResult(ProliferationExportResult.Failure("Export is not available yet."));
    }
}

public sealed record ProliferationExportRequest(
    ProliferationSource? Source,
    int? YearFrom,
    int? YearTo,
    int? SponsoringUnitId,
    string? SimulatorUserId,
    string? SearchTerm,
    string RequestedByUserId);

public sealed record ProliferationExportFile(string FileName, byte[] Content, string ContentType)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed record ProliferationExportResult(bool Success, ProliferationExportFile? File, IReadOnlyList<string> Errors)
{
    public static ProliferationExportResult FromFile(ProliferationExportFile file) => new(true, file, Array.Empty<string>());

    public static ProliferationExportResult Failure(params string[] errors)
    {
        if (errors is { Length: > 0 })
        {
            return new ProliferationExportResult(false, null, errors);
        }

        return new ProliferationExportResult(false, null, new[] { "Export failed." });
    }
}

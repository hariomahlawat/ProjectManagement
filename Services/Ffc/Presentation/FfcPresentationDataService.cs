using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Ffc.Presentation;

public sealed class FfcPresentationDataService : IFfcPresentationDataService
{
    private readonly ApplicationDbContext _db;
    private readonly IFfcFootprintService _footprintService;

    public FfcPresentationDataService(
        ApplicationDbContext db,
        IFfcFootprintService footprintService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _footprintService = footprintService ?? throw new ArgumentNullException(nameof(footprintService));
    }

    public async Task<FfcPresentationData> GetAsync(
        FfcPowerPointExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var footprintRequest = request.Scope switch
        {
            FfcExportScope.CompletePortfolio => new FfcFootprintRequest(
                Metric: FfcFootprintMetric.TotalUnits,
                Sort: FfcFootprintSort.TotalUnits),

            _ => new FfcFootprintRequest(
                Year: request.Year,
                CountryId: request.Scope == FfcExportScope.CurrentFilteredPortfolio
                    ? request.CountryId
                    : null,
                Search: request.Search,
                Metric: FfcFootprintMetric.TotalUnits,
                Sort: FfcFootprintSort.TotalUnits)
        };

        var footprint = await _footprintService.GetAsync(footprintRequest, cancellationToken);
        var countries = footprint.Countries.AsEnumerable();

        if (request.Scope == FfcExportScope.SelectedCountries)
        {
            var selectedIds = request.SelectedCountryIds
                .Where(id => id > 0)
                .ToHashSet();

            countries = countries.Where(country => selectedIds.Contains(country.CountryId));
        }

        var selectedCountries = countries
            .OrderByDescending(country => country.TotalUnits)
            .ThenBy(country => country.CountryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var recordIds = selectedCountries
            .SelectMany(country => country.Years)
            .Select(year => year.RecordId)
            .Distinct()
            .ToArray();

        var metadataByRecordId = recordIds.Length == 0
            ? new Dictionary<long, RecordMetadata>()
            : await LoadRecordMetadataAsync(
                recordIds,
                request.IncludeAttachmentRegister,
                cancellationToken);

        var presentationCountries = selectedCountries
            .Select(country =>
            {
                var records = country.Years
                    .OrderByDescending(year => year.Year)
                    .ThenByDescending(year => year.UpdatedAt)
                    .Select(year =>
                    {
                        metadataByRecordId.TryGetValue(year.RecordId, out var metadata);
                        metadata ??= RecordMetadata.Empty(year.RecordId);

                        var projects = request.IncludeProjects
                            ? year.Projects
                                .OrderBy(project => PositionSort(project.Position))
                                .ThenByDescending(project => project.Quantity)
                                .ThenBy(project => project.DisplayName, StringComparer.OrdinalIgnoreCase)
                                .Select(project => new FfcPresentationProject(
                                    project.FfcProjectId,
                                    project.LinkedProjectId,
                                    project.DisplayName,
                                    project.FfcName,
                                    project.Quantity,
                                    project.Position,
                                    project.StageSummary,
                                    request.IncludeProgress ? project.CurrentProgress : null))
                                .ToArray()
                            : Array.Empty<FfcPresentationProject>();

                        return new FfcPresentationRecord(
                            year.RecordId,
                            year.Year,
                            year.ProjectCount,
                            metadata.IpaCompleted,
                            metadata.IpaDate,
                            request.IncludeMilestoneRemarks ? metadata.IpaRemarks : null,
                            metadata.GslCompleted,
                            metadata.GslDate,
                            request.IncludeMilestoneRemarks ? metadata.GslRemarks : null,
                            year.OverallPosition,
                            year.InstalledUnits,
                            year.DeliveredNotInstalledUnits,
                            year.PlannedUnits,
                            year.UpdatedAt,
                            projects,
                            request.IncludeAttachmentRegister
                                ? metadata.Attachments
                                : Array.Empty<FfcPresentationAttachment>());
                    })
                    .ToArray();

                return new FfcPresentationCountry(
                    country.CountryId,
                    country.CountryName,
                    country.IsoCode,
                    country.RecordCount,
                    country.ProjectCount,
                    country.InstalledUnits,
                    country.DeliveredNotInstalledUnits,
                    country.PlannedUnits,
                    country.LastUpdated,
                    records);
            })
            .ToArray();

        var summary = new FfcFootprintSummary(
            presentationCountries.Length,
            presentationCountries.Sum(country => country.RecordCount),
            presentationCountries.Sum(country => country.ProjectCount),
            presentationCountries.Sum(country => country.InstalledUnits),
            presentationCountries.Sum(country => country.DeliveredNotInstalledUnits),
            presentationCountries.Sum(country => country.PlannedUnits));

        var positionDate = presentationCountries.Length == 0
            ? request.RequestedAt
            : presentationCountries.Max(country => country.LastUpdated);

        var title = NormalizeText(request.Title, 120) ?? "FFC Global Portfolio";
        var subtitle = NormalizeText(request.Subtitle, 180)
            ?? $"Status as at {positionDate:dd MMM yyyy}";

        return new FfcPresentationData(
            title,
            subtitle,
            NormalizeText(request.HandlingMarking, 80),
            positionDate,
            request.PresentationType,
            request.IncludeProjects,
            request.IncludeProgress,
            request.IncludeMilestoneRemarks,
            request.IncludeAttachmentRegister,
            summary,
            presentationCountries);
    }

    private async Task<Dictionary<long, RecordMetadata>> LoadRecordMetadataAsync(
        IReadOnlyCollection<long> recordIds,
        bool includeAttachments,
        CancellationToken cancellationToken)
    {
        var recordRows = await _db.FfcRecords
            .AsNoTracking()
            .TagWith("FFC PowerPoint: record metadata")
            .Where(record => recordIds.Contains(record.Id))
            .Select(record => new
            {
                record.Id,
                record.IpaYes,
                record.IpaDate,
                record.IpaRemarks,
                record.GslYes,
                record.GslDate,
                record.GslRemarks
            })
            .ToListAsync(cancellationToken);

        var attachmentsByRecordId = new Dictionary<long, IReadOnlyList<FfcPresentationAttachment>>();
        if (includeAttachments)
        {
            var attachmentRows = await _db.FfcAttachments
                .AsNoTracking()
                .TagWith("FFC PowerPoint: attachment register")
                .Where(attachment => recordIds.Contains(attachment.FfcRecordId))
                .OrderByDescending(attachment => attachment.UploadedAt)
                .Select(attachment => new
                {
                    attachment.FfcRecordId,
                    attachment.Caption,
                    attachment.FilePath,
                    attachment.Kind,
                    attachment.SizeBytes,
                    attachment.UploadedAt
                })
                .ToListAsync(cancellationToken);

            attachmentsByRecordId = attachmentRows
                .GroupBy(attachment => attachment.FfcRecordId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<FfcPresentationAttachment>)group
                        .Select(attachment => new FfcPresentationAttachment(
                            NormalizeText(attachment.Caption, 160)
                                ?? Path.GetFileName(attachment.FilePath)
                                ?? "Supporting file",
                            attachment.Kind.ToString(),
                            attachment.SizeBytes,
                            attachment.UploadedAt))
                        .ToArray());
        }

        return recordRows.ToDictionary(
            row => row.Id,
            row => new RecordMetadata(
                row.Id,
                row.IpaYes,
                row.IpaDate,
                NormalizeText(row.IpaRemarks, 1000),
                row.GslYes,
                row.GslDate,
                NormalizeText(row.GslRemarks, 1000),
                attachmentsByRecordId.GetValueOrDefault(row.Id)
                    ?? Array.Empty<FfcPresentationAttachment>()));
    }

    private static int PositionSort(FfcUnitPosition position) => position switch
    {
        FfcUnitPosition.Installed => 0,
        FfcUnitPosition.DeliveredAwaitingInstallation => 1,
        _ => 2
    };

    private static string? NormalizeText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(
            " ",
            value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..Math.Max(1, maximumLength - 1)] + "…";
    }

    private sealed record RecordMetadata(
        long RecordId,
        bool IpaCompleted,
        DateOnly? IpaDate,
        string? IpaRemarks,
        bool GslCompleted,
        DateOnly? GslDate,
        string? GslRemarks,
        IReadOnlyList<FfcPresentationAttachment> Attachments)
    {
        public static RecordMetadata Empty(long recordId)
            => new(
                recordId,
                false,
                null,
                null,
                false,
                null,
                null,
                Array.Empty<FfcPresentationAttachment>());
    }
}

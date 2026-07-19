using System;
using System.Collections.Generic;

namespace ProjectManagement.Services.Ffc.Presentation;

public enum FfcExportScope
{
    CurrentFilteredPortfolio = 0,
    CompletePortfolio = 1,
    SelectedCountries = 2
}

public enum FfcPresentationType
{
    ExecutiveBrief = 0,
    FullPortfolio = 1
}

public sealed record FfcPowerPointExportRequest(
    FfcExportScope Scope,
    FfcPresentationType PresentationType,
    short? Year,
    long? CountryId,
    string? Search,
    IReadOnlyCollection<long> SelectedCountryIds,
    bool IncludeProjects,
    bool IncludeProgress,
    bool IncludeMilestoneRemarks,
    bool IncludeAttachmentRegister,
    string Title,
    string? Subtitle,
    string? HandlingMarking,
    DateTimeOffset RequestedAt);

public sealed record FfcPowerPointExportResult(
    byte[] Content,
    string FileName,
    string ContentType,
    int SlideCount);

public sealed record FfcPresentationData(
    string Title,
    string Subtitle,
    string? HandlingMarking,
    DateTimeOffset PositionDate,
    FfcPresentationType PresentationType,
    bool IncludeProjects,
    bool IncludeProgress,
    bool IncludeMilestoneRemarks,
    bool IncludeAttachmentRegister,
    FfcFootprintSummary Summary,
    IReadOnlyList<FfcPresentationCountry> Countries);

public sealed record FfcPresentationCountry(
    long CountryId,
    string CountryName,
    string IsoCode,
    int RecordCount,
    int ProjectCount,
    int InstalledUnits,
    int DeliveredNotInstalledUnits,
    int PlannedUnits,
    DateTimeOffset LastUpdated,
    IReadOnlyList<FfcPresentationRecord> Records)
{
    public int TotalUnits => InstalledUnits + DeliveredNotInstalledUnits + PlannedUnits;
}

public sealed record FfcPresentationRecord(
    long RecordId,
    short Year,
    int ProjectCount,
    bool IpaCompleted,
    DateOnly? IpaDate,
    string? IpaRemarks,
    bool GslCompleted,
    DateOnly? GslDate,
    string? GslRemarks,
    string? OverallPosition,
    int InstalledUnits,
    int DeliveredNotInstalledUnits,
    int PlannedUnits,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<FfcPresentationProject> Projects,
    IReadOnlyList<FfcPresentationAttachment> Attachments)
{
    public int TotalUnits => InstalledUnits + DeliveredNotInstalledUnits + PlannedUnits;
}

public sealed record FfcPresentationProject(
    long FfcProjectId,
    int? LinkedProjectId,
    string Name,
    string FfcName,
    int Quantity,
    FfcUnitPosition Position,
    string? StageSummary,
    string? CurrentProgress);

public sealed record FfcPresentationAttachment(
    string DisplayName,
    string Kind,
    long SizeBytes,
    DateTimeOffset UploadedAt);

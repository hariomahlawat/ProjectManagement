using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.Api;

public sealed record MiscActivityListQueryDto
{
    public Guid? ActivityTypeId { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public string? Search { get; init; }

    public bool IncludeDeleted { get; init; }

    public MiscActivitySortField Sort { get; init; } = MiscActivitySortField.OccurrenceDate;

    public bool Desc { get; init; } = true;

    public string? CreatorUserId { get; init; }

    public MiscActivityAttachmentTypeFilter AttachmentType { get; init; } = MiscActivityAttachmentTypeFilter.Any;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

public sealed record MiscActivityListResponseDto(IReadOnlyList<MiscActivityListItemDto> Items);

public sealed record MiscActivityListItemDto(
    Guid Id,
    Guid? ActivityTypeId,
    string? ActivityTypeName,
    string Nomenclature,
    DateOnly OccurrenceDate,
    string? Description,
    string? ExternalLink,
    int MediaCount,
    int ImageCount,
    int DocumentCount,
    bool IsDeleted,
    DateTimeOffset CapturedAtUtc,
    string CapturedByUserId,
    DateTimeOffset? LastModifiedAtUtc,
    string? LastModifiedByUserId,
    string RowVersion);

public sealed record MiscActivityDetailDto(
    Guid Id,
    Guid? ActivityTypeId,
    string? ActivityTypeName,
    string Nomenclature,
    DateOnly OccurrenceDate,
    string? Description,
    string? ExternalLink,
    bool IsDeleted,
    DateTimeOffset CapturedAtUtc,
    string CapturedByUserId,
    DateTimeOffset? LastModifiedAtUtc,
    string? LastModifiedByUserId,
    DateTimeOffset? DeletedUtc,
    string? DeletedByUserId,
    string RowVersion,
    IReadOnlyList<MiscActivityMediaDto> Media);

public sealed record MiscActivityMediaDto(
    Guid Id,
    string OriginalFileName,
    string MediaType,
    long FileSize,
    string? Caption,
    int? Width,
    int? Height,
    DateTimeOffset UploadedAtUtc,
    string UploadedByUserId,
    string RowVersion,
    string StorageKey);

public sealed class MiscActivityCreateDto
{
    public Guid? ActivityTypeId { get; set; }

    public DateOnly OccurrenceDate { get; set; }

    public string Nomenclature { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? ExternalLink { get; set; }
}

public sealed class MiscActivityUpdateDto : MiscActivityCreateDto
{
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class MiscActivityMediaUploadDto
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }

    [FromForm(Name = "caption")]
    public string? Caption { get; set; }

    [FromForm(Name = "rowVersion")]
    public string? RowVersion { get; set; }
}

public sealed record MiscActivityMediaUploadResponseDto(
    MiscActivityMediaDto Media,
    string ActivityRowVersion);

public sealed record ActivityTypeOptionDto(Guid Id, string Name, bool IsActive);

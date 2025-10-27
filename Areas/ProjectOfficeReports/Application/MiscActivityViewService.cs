using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IMiscActivityViewService
{
    Task<MiscActivityIndexViewModel> GetIndexAsync(MiscActivityQueryOptions options, CancellationToken cancellationToken);

    Task<MiscActivityFormViewModel> GetCreateFormAsync(CancellationToken cancellationToken);

    Task<MiscActivityFormViewModel?> GetEditFormAsync(Guid id, CancellationToken cancellationToken);

    Task<MiscActivityDetailViewModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken);

    Task<MiscActivityExportViewModel> GetExportAsync(MiscActivityExportCriteria criteria, CancellationToken cancellationToken);
}

public sealed record MiscActivityExportCriteria(
    Guid? ActivityTypeId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Search,
    bool IncludeDeleted);

public sealed class MiscActivityViewService : IMiscActivityViewService
{
    private readonly IMiscActivityService _activityService;
    private readonly IActivityTypeService _activityTypeService;
    private readonly MiscActivityMediaOptions _mediaOptions;

    public MiscActivityViewService(
        IMiscActivityService activityService,
        IActivityTypeService activityTypeService,
        IOptions<MiscActivityMediaOptions> mediaOptions)
    {
        _activityService = activityService ?? throw new ArgumentNullException(nameof(activityService));
        _activityTypeService = activityTypeService ?? throw new ArgumentNullException(nameof(activityTypeService));
        if (mediaOptions is null) throw new ArgumentNullException(nameof(mediaOptions));
        _mediaOptions = mediaOptions.Value ?? throw new ArgumentNullException(nameof(mediaOptions));
    }

    public async Task<MiscActivityIndexViewModel> GetIndexAsync(
        MiscActivityQueryOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeQueryOptions(options);
        var activities = await _activityService.SearchAsync(normalized, cancellationToken);
        var activityTypes = await _activityTypeService.GetAllAsync(includeInactive: true, cancellationToken);

        var filter = new MiscActivityIndexFilterViewModel
        {
            ActivityTypeId = normalized.ActivityTypeId,
            StartDate = normalized.StartDate,
            EndDate = normalized.EndDate,
            Search = normalized.SearchText,
            IncludeDeleted = normalized.IncludeDeleted,
            Sort = normalized.SortField,
            SortDescending = normalized.SortDescending,
            ActivityTypeOptions = BuildActivityTypeOptions(activityTypes, normalized.ActivityTypeId)
        };

        var items = activities
            .Select(MapListItem)
            .ToList();

        return new MiscActivityIndexViewModel
        {
            Filter = filter,
            Activities = items
        };
    }

    public async Task<MiscActivityFormViewModel> GetCreateFormAsync(CancellationToken cancellationToken)
    {
        var activityTypes = await _activityTypeService.GetAllAsync(includeInactive: true, cancellationToken);
        return new MiscActivityFormViewModel
        {
            ActivityTypeOptions = BuildActivityTypeOptions(activityTypes, null)
        };
    }

    public async Task<MiscActivityFormViewModel?> GetEditFormAsync(Guid id, CancellationToken cancellationToken)
    {
        var activity = await _activityService.FindAsync(id, cancellationToken);
        if (activity is null)
        {
            return null;
        }

        var activityTypes = await _activityTypeService.GetAllAsync(includeInactive: true, cancellationToken);
        return new MiscActivityFormViewModel
        {
            Id = activity.Id,
            ActivityTypeId = activity.ActivityTypeId,
            OccurrenceDate = activity.OccurrenceDate,
            Nomenclature = activity.Nomenclature,
            Description = activity.Description,
            ExternalLink = activity.ExternalLink,
            RowVersion = Convert.ToBase64String(activity.RowVersion),
            ActivityTypeOptions = BuildActivityTypeOptions(activityTypes, activity.ActivityTypeId)
        };
    }

    public async Task<MiscActivityDetailViewModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var activity = await _activityService.FindAsync(id, cancellationToken);
        if (activity is null)
        {
            return null;
        }

        var upload = new MiscActivityMediaUploadViewModel
        {
            RowVersion = Convert.ToBase64String(activity.RowVersion),
            MaxFileSizeBytes = _mediaOptions.MaxFileSizeBytes,
            AllowedContentTypes = _mediaOptions.AllowedContentTypes
        };

        var media = activity.Media
            .OrderByDescending(x => x.UploadedAtUtc)
            .ThenBy(x => x.OriginalFileName, StringComparer.OrdinalIgnoreCase)
            .Select(MapMedia)
            .ToList();

        return new MiscActivityDetailViewModel
        {
            Id = activity.Id,
            ActivityTypeId = activity.ActivityTypeId,
            ActivityTypeName = activity.ActivityType?.Name,
            Nomenclature = activity.Nomenclature,
            OccurrenceDate = activity.OccurrenceDate,
            Description = activity.Description,
            ExternalLink = activity.ExternalLink,
            IsDeleted = activity.DeletedUtc.HasValue,
            CapturedAtUtc = activity.CapturedAtUtc,
            CapturedByUserId = activity.CapturedByUserId,
            LastModifiedAtUtc = activity.LastModifiedAtUtc,
            LastModifiedByUserId = activity.LastModifiedByUserId,
            DeletedUtc = activity.DeletedUtc,
            DeletedByUserId = activity.DeletedByUserId,
            RowVersion = Convert.ToBase64String(activity.RowVersion),
            Media = media,
            Upload = upload
        };
    }

    public async Task<MiscActivityExportViewModel> GetExportAsync(
        MiscActivityExportCriteria criteria,
        CancellationToken cancellationToken)
    {
        var activityTypes = await _activityTypeService.GetAllAsync(includeInactive: true, cancellationToken);
        var trimmedSearch = string.IsNullOrWhiteSpace(criteria.Search) ? null : criteria.Search.Trim();
        return new MiscActivityExportViewModel
        {
            ActivityTypeId = criteria.ActivityTypeId,
            StartDate = criteria.StartDate,
            EndDate = criteria.EndDate,
            Search = trimmedSearch,
            IncludeDeleted = criteria.IncludeDeleted,
            ActivityTypeOptions = BuildActivityTypeOptions(activityTypes, criteria.ActivityTypeId)
        };
    }

    private static MiscActivityQueryOptions NormalizeQueryOptions(MiscActivityQueryOptions options)
    {
        var trimmedSearch = string.IsNullOrWhiteSpace(options.SearchText) ? null : options.SearchText.Trim();
        return new MiscActivityQueryOptions(
            options.ActivityTypeId,
            options.StartDate,
            options.EndDate,
            trimmedSearch,
            options.IncludeDeleted,
            options.SortField,
            options.SortDescending);
    }

    private static MiscActivityListItemViewModel MapListItem(MiscActivityListItem item)
    {
        return new MiscActivityListItemViewModel
        {
            Id = item.Id,
            ActivityTypeId = item.ActivityTypeId,
            ActivityTypeName = item.ActivityTypeName,
            Nomenclature = item.Nomenclature,
            OccurrenceDate = item.OccurrenceDate,
            Description = item.Description,
            ExternalLink = item.ExternalLink,
            MediaCount = item.MediaCount,
            IsDeleted = item.IsDeleted,
            CapturedAtUtc = item.CapturedAtUtc,
            CapturedByUserId = item.CapturedByUserId,
            LastModifiedAtUtc = item.LastModifiedAtUtc,
            LastModifiedByUserId = item.LastModifiedByUserId,
            RowVersion = Convert.ToBase64String(item.RowVersion)
        };
    }

    private static MiscActivityMediaViewModel MapMedia(ActivityMedia media)
    {
        return new MiscActivityMediaViewModel
        {
            Id = media.Id,
            OriginalFileName = media.OriginalFileName,
            MediaType = media.MediaType,
            FileSize = media.FileSize,
            Caption = media.Caption,
            Width = media.Width,
            Height = media.Height,
            UploadedAtUtc = media.UploadedAtUtc,
            UploadedByUserId = media.UploadedByUserId,
            RowVersion = Convert.ToBase64String(media.RowVersion),
            StorageKey = media.StorageKey
        };
    }

    private static IReadOnlyList<SelectListItem> BuildActivityTypeOptions(
        IEnumerable<ActivityType> activityTypes,
        Guid? selectedId)
    {
        return activityTypes
            .Select(type => new SelectListItem
            {
                Value = type.Id.ToString(),
                Text = type.Name,
                Selected = selectedId.HasValue && type.Id == selectedId.Value,
                Disabled = !type.IsActive
            })
            .ToList();
    }
}

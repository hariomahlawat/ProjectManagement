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
        var totalCount = await _activityService.CountAsync(normalized, cancellationToken);
        var activities = await _activityService.SearchAsync(normalized, cancellationToken);
        var activityTypes = await _activityTypeService.GetAllAsync(includeInactive: true, cancellationToken);
        var creators = await _activityService.GetCreatorsAsync(cancellationToken);

        var userIds = activities
            .SelectMany(item => new[] { item.CapturedByUserId, item.LastModifiedByUserId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var userDisplayNames = userIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : (IReadOnlyDictionary<string, string>)await _activityService.GetUserDisplayNamesAsync(userIds, cancellationToken);

        var pageNumber = normalized.PageNumber <= 0 ? 1 : normalized.PageNumber;
        var pageSize = normalized.PageSize <= 0 ? 25 : normalized.PageSize;
        var totalPages = pageSize <= 0
            ? 1
            : Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));

        var filter = new MiscActivityIndexFilterViewModel
        {
            ActivityTypeId = normalized.ActivityTypeId,
            StartDate = normalized.StartDate,
            EndDate = normalized.EndDate,
            Search = normalized.SearchText,
            IncludeDeleted = normalized.IncludeDeleted,
            Sort = normalized.SortField,
            SortDescending = normalized.SortDescending,
            CreatorUserId = normalized.CapturedByUserId,
            AttachmentType = normalized.AttachmentType,
            PageNumber = pageNumber,
            PageSize = pageSize,
            ActivityTypeOptions = BuildActivityTypeOptions(activityTypes, normalized.ActivityTypeId),
            CreatorOptions = BuildCreatorOptions(creators, normalized.CapturedByUserId),
            AttachmentTypeOptions = BuildAttachmentTypeOptions(normalized.AttachmentType)
        };

        var items = activities
            .Select(item => MapListItem(item, userDisplayNames))
            .ToList();

        return new MiscActivityIndexViewModel
        {
            Filter = filter,
            Activities = items,
            Pagination = new MiscActivityIndexPaginationViewModel
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages
            }
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

        var userIds = new List<string?>
        {
            activity.CapturedByUserId,
            activity.LastModifiedByUserId,
            activity.DeletedByUserId
        };

        userIds.AddRange(activity.Media.Select(m => m.UploadedByUserId));

        var displayNames = await _activityService.GetUserDisplayNamesAsync(
            userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!),
            cancellationToken);

        var upload = new MiscActivityMediaUploadViewModel
        {
            RowVersion = Convert.ToBase64String(activity.RowVersion),
            MaxFileSizeBytes = _mediaOptions.MaxFileSizeBytes,
            AllowedContentTypes = _mediaOptions.AllowedContentTypes
        };

        var media = activity.Media
            .OrderByDescending(x => x.UploadedAtUtc)
            .ThenBy(x => x.OriginalFileName, StringComparer.OrdinalIgnoreCase)
            .Select(x => MapMedia(x, displayNames))
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
            CapturedByDisplayName = ResolveDisplayName(displayNames, activity.CapturedByUserId),
            LastModifiedAtUtc = activity.LastModifiedAtUtc,
            LastModifiedByUserId = activity.LastModifiedByUserId,
            LastModifiedByDisplayName = ResolveDisplayName(displayNames, activity.LastModifiedByUserId),
            DeletedUtc = activity.DeletedUtc,
            DeletedByUserId = activity.DeletedByUserId,
            DeletedByDisplayName = ResolveDisplayName(displayNames, activity.DeletedByUserId),
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
        var trimmedCreator = string.IsNullOrWhiteSpace(options.CapturedByUserId)
            ? null
            : options.CapturedByUserId.Trim();
        var pageSize = options.PageSize <= 0 ? 25 : Math.Min(options.PageSize, 100);
        var pageNumber = options.PageNumber <= 0 ? 1 : options.PageNumber;

        return new MiscActivityQueryOptions(
            options.ActivityTypeId,
            options.StartDate,
            options.EndDate,
            trimmedSearch,
            options.IncludeDeleted,
            options.SortField,
            options.SortDescending,
            trimmedCreator,
            options.AttachmentType,
            pageNumber,
            pageSize);
    }

    private static MiscActivityListItemViewModel MapListItem(
        MiscActivityListItem item,
        IReadOnlyDictionary<string, string> displayNames)
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
            ImageCount = item.ImageCount,
            DocumentCount = item.DocumentCount,
            IsDeleted = item.IsDeleted,
            CapturedAtUtc = item.CapturedAtUtc,
            CapturedByUserId = item.CapturedByUserId,
            CapturedByDisplayName = ResolveDisplayName(displayNames, item.CapturedByUserId),
            LastModifiedAtUtc = item.LastModifiedAtUtc,
            LastModifiedByUserId = item.LastModifiedByUserId,
            LastModifiedByDisplayName = ResolveDisplayName(displayNames, item.LastModifiedByUserId),
            RowVersion = Convert.ToBase64String(item.RowVersion)
        };
    }

    private static MiscActivityMediaViewModel MapMedia(
        ActivityMedia media,
        IReadOnlyDictionary<string, string> displayNames)
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
            UploadedByDisplayName = ResolveDisplayName(displayNames, media.UploadedByUserId),
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

    private static IReadOnlyList<SelectListItem> BuildCreatorOptions(
        IReadOnlyList<MiscActivityCreatorOption> creators,
        string? selectedId)
    {
        var options = new List<SelectListItem>
        {
            new("All creators", string.Empty)
        };

        foreach (var creator in creators.OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            options.Add(new SelectListItem(creator.DisplayName, creator.UserId)
            {
                Selected = !string.IsNullOrWhiteSpace(selectedId) && string.Equals(creator.UserId, selectedId, StringComparison.OrdinalIgnoreCase)
            });
        }

        return options;
    }

    private static IReadOnlyList<SelectListItem> BuildAttachmentTypeOptions(MiscActivityAttachmentTypeFilter selected)
    {
        return new List<SelectListItem>
        {
            new("All attachments", MiscActivityAttachmentTypeFilter.Any.ToString()) { Selected = selected == MiscActivityAttachmentTypeFilter.Any },
            new("Images", MiscActivityAttachmentTypeFilter.Images.ToString()) { Selected = selected == MiscActivityAttachmentTypeFilter.Images },
            new("Documents", MiscActivityAttachmentTypeFilter.Documents.ToString()) { Selected = selected == MiscActivityAttachmentTypeFilter.Documents },
            new("Without attachments", MiscActivityAttachmentTypeFilter.WithoutAttachments.ToString()) { Selected = selected == MiscActivityAttachmentTypeFilter.WithoutAttachments }
        };
    }

    private static string ResolveDisplayName(IReadOnlyDictionary<string, string> displayNames, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return string.Empty;
        }

        return displayNames.TryGetValue(userId, out var name) ? name : userId;
    }
}

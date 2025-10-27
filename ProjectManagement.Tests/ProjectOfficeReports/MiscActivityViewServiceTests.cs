using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class MiscActivityViewServiceTests
{
    [Fact]
    public async Task GetIndexAsync_ReturnsMappedViewModel()
    {
        var activityTypeId = Guid.NewGuid();
        var listItem = new MiscActivityListItem(
            Guid.NewGuid(),
            activityTypeId,
            "Capability Building",
            "Networking Workshop",
            new DateOnly(2024, 1, 20),
            "Description",
            "https://example.test",
            3,
            2,
            1,
            false,
            DateTimeOffset.UtcNow.AddDays(-2),
            "user-1",
            DateTimeOffset.UtcNow.AddDays(-1),
            "user-2",
            new byte[] { 1, 2, 3 });

        var activityTypes = new List<ActivityType>
        {
            new()
            {
                Id = activityTypeId,
                Name = "Capability Building",
                IsActive = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Legacy",
                IsActive = false
            }
        };

        var activityService = new StubMiscActivityService
        {
            ListItems = new[] { listItem },
            Creators = new[] { new MiscActivityCreatorOption("user-1", "User One") },
            UserDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["user-1"] = "User One",
                ["user-2"] = "User Two"
            }
        };

        var activityTypeService = new StubActivityTypeService
        {
            ActivityTypes = activityTypes
        };

        var viewService = CreateService(activityService, activityTypeService);
        var query = new MiscActivityQueryOptions(
            activityTypeId,
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            "  workshop  ",
            false,
            MiscActivitySortField.Nomenclature,
            false,
            null,
            MiscActivityAttachmentTypeFilter.Any,
            1,
            25);

        var result = await viewService.GetIndexAsync(query, CancellationToken.None);

        Assert.Equal("workshop", activityService.LastQueryOptions?.SearchText);
        Assert.Equal(activityTypeId, result.Filter.ActivityTypeId);
        Assert.Equal(new DateOnly(2024, 1, 1), result.Filter.StartDate);
        Assert.Equal(new DateOnly(2024, 1, 31), result.Filter.EndDate);
        Assert.Equal("workshop", result.Filter.Search);
        Assert.Single(result.Activities);
        var item = result.Activities[0];
        Assert.Equal(listItem.Id, item.Id);
        Assert.Equal("Capability Building", item.ActivityTypeName);
        Assert.Equal("Networking Workshop", item.Nomenclature);
        Assert.Equal("AQID", item.RowVersion);
        Assert.Equal(2, item.ImageCount);
        Assert.Equal(1, item.DocumentCount);
        Assert.Equal("User One", item.CapturedByDisplayName);
        Assert.Equal("User Two", item.LastModifiedByDisplayName);
        Assert.Equal(2, result.Filter.ActivityTypeOptions.Count);
        Assert.True(result.Filter.ActivityTypeOptions[0].Selected);
        Assert.Equal(2, result.Filter.CreatorOptions.Count);
        Assert.Equal(MiscActivityAttachmentTypeFilter.Any, result.Filter.AttachmentType);
        Assert.Equal(1, result.Pagination.PageNumber);
        Assert.Equal(25, result.Pagination.PageSize);
        Assert.Equal(1, result.Pagination.TotalCount);
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsDetailWithUploadOptions()
    {
        var activityType = new ActivityType
        {
            Id = Guid.NewGuid(),
            Name = "Engagement",
            IsActive = true
        };

        var activity = new MiscActivity
        {
            Id = Guid.NewGuid(),
            ActivityTypeId = activityType.Id,
            ActivityType = activityType,
            Nomenclature = "Community Outreach",
            OccurrenceDate = new DateOnly(2024, 2, 15),
            Description = "Details",
            ExternalLink = "https://example.test",
            CapturedAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
            CapturedByUserId = "capturer",
            LastModifiedAtUtc = DateTimeOffset.UtcNow.AddDays(-3),
            LastModifiedByUserId = "modifier",
            DeletedUtc = DateTimeOffset.UtcNow.AddDays(-1),
            DeletedByUserId = "deleter",
            RowVersion = new byte[] { 9, 9, 9 }
        };

        activity.Media.Add(new ActivityMedia
        {
            Id = Guid.NewGuid(),
            ActivityId = activity.Id,
            OriginalFileName = "report.pdf",
            MediaType = "application/pdf",
            FileSize = 1024,
            Caption = "Report",
            UploadedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            UploadedByUserId = "capturer",
            RowVersion = new byte[] { 5, 4, 3 },
            StorageKey = "key/report.pdf"
        });

        var activityService = new StubMiscActivityService
        {
            Activity = activity,
            UserDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["capturer"] = "Capture R",
                ["modifier"] = "Modifier",
                ["deleter"] = "Deleter"
            }
        };

        var activityTypeService = new StubActivityTypeService();
        var viewService = CreateService(activityService, activityTypeService);

        var result = await viewService.GetDetailAsync(activity.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(activity.Id, result!.Id);
        Assert.Equal("Engagement", result.ActivityTypeName);
        Assert.True(result.IsDeleted);
        Assert.Single(result.Media);
        Assert.Equal("report.pdf", result.Media[0].OriginalFileName);
        Assert.Equal("BQQD", result.Media[0].RowVersion);
        Assert.Equal("Capture R", result.CapturedByDisplayName);
        Assert.Equal("Modifier", result.LastModifiedByDisplayName);
        Assert.Equal("Deleter", result.DeletedByDisplayName);
        Assert.Equal("Capture R", result.Media[0].UploadedByDisplayName);
        Assert.Equal("CQkJ", result.RowVersion);
        Assert.Equal(viewServiceOptions.MaxFileSizeBytes, result.Upload.MaxFileSizeBytes);
        Assert.Equal(viewServiceOptions.AllowedContentTypes, result.Upload.AllowedContentTypes);
        Assert.Equal("CQkJ", result.Upload.RowVersion);
    }

    [Fact]
    public async Task GetEditFormAsync_ReturnsExistingValues()
    {
        var activityType = new ActivityType
        {
            Id = Guid.NewGuid(),
            Name = "Site Visit",
            IsActive = true
        };

        var activity = new MiscActivity
        {
            Id = Guid.NewGuid(),
            ActivityTypeId = activityType.Id,
            ActivityType = activityType,
            Nomenclature = "Plant Inspection",
            OccurrenceDate = new DateOnly(2024, 4, 10),
            Description = "Inspection details",
            ExternalLink = "https://inspection.test",
            RowVersion = new byte[] { 7, 6, 5 }
        };

        var activityService = new StubMiscActivityService
        {
            Activity = activity
        };

        var activityTypeService = new StubActivityTypeService
        {
            ActivityTypes = new[] { activityType }
        };

        var viewService = CreateService(activityService, activityTypeService);

        var result = await viewService.GetEditFormAsync(activity.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(activityType.Id, result!.ActivityTypeId);
        Assert.Equal("Plant Inspection", result.Nomenclature);
        Assert.Equal("BwYF", result.RowVersion);
        Assert.Single(result.ActivityTypeOptions);
        Assert.True(result.ActivityTypeOptions[0].Selected);
    }

    [Fact]
    public async Task GetExportAsync_TrimsSearch()
    {
        var activityTypeService = new StubActivityTypeService
        {
            ActivityTypes = new List<ActivityType>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Name = "Capability",
                    IsActive = true
                }
            }
        };

        var viewService = CreateService(new StubMiscActivityService(), activityTypeService);

        var criteria = new MiscActivityExportCriteria(
            activityTypeService.ActivityTypes[0].Id,
            new DateOnly(2024, 5, 1),
            new DateOnly(2024, 5, 31),
            "  export  ",
            true);

        var result = await viewService.GetExportAsync(criteria, CancellationToken.None);

        Assert.Equal("export", result.Search);
        Assert.True(result.IncludeDeleted);
        Assert.Single(result.ActivityTypeOptions);
        Assert.True(result.ActivityTypeOptions[0].Selected);
    }

    private static MiscActivityViewService CreateService(
        StubMiscActivityService activityService,
        StubActivityTypeService activityTypeService)
    {
        var options = Options.Create(new MiscActivityMediaOptions
        {
            MaxFileSizeBytes = viewServiceOptions.MaxFileSizeBytes,
            AllowedContentTypes = viewServiceOptions.AllowedContentTypes
        });

        return new MiscActivityViewService(activityService, activityTypeService, options);
    }

    private sealed class StubMiscActivityService : IMiscActivityService
    {
        public IReadOnlyList<MiscActivityListItem> ListItems { get; set; } = Array.Empty<MiscActivityListItem>();

        public MiscActivity? Activity { get; set; }

        public MiscActivityQueryOptions? LastQueryOptions { get; private set; }

        public IReadOnlyList<MiscActivityCreatorOption> Creators { get; set; } = Array.Empty<MiscActivityCreatorOption>();

        public IReadOnlyDictionary<string, string> UserDisplayNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<MiscActivityListItem>> SearchAsync(MiscActivityQueryOptions options, CancellationToken cancellationToken)
        {
            LastQueryOptions = options;
            return Task.FromResult(ListItems);
        }

        public Task<int> CountAsync(MiscActivityQueryOptions options, CancellationToken cancellationToken)
            => Task.FromResult(ListItems.Count);

        public Task<IReadOnlyList<MiscActivityExportRow>> ExportAsync(MiscActivityQueryOptions options, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<MiscActivity?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            if (Activity is not null && Activity.Id == id)
            {
                return Task.FromResult<MiscActivity?>(Activity);
            }

            return Task.FromResult<MiscActivity?>(null);
        }

        public Task<MiscActivityMutationResult> CreateAsync(MiscActivityCreateRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<MiscActivityMutationResult> UpdateAsync(Guid id, MiscActivityUpdateRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<MiscActivityDeletionResult> DeleteAsync(Guid id, byte[] rowVersion, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ActivityMediaUploadResult> UploadMediaAsync(ActivityMediaUploadRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ActivityMediaDeletionResult> DeleteMediaAsync(ActivityMediaDeletionRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<MiscActivityCreatorOption>> GetCreatorsAsync(CancellationToken cancellationToken)
            => Task.FromResult(Creators);

        public Task<IReadOnlyDictionary<string, string>> GetUserDisplayNamesAsync(IEnumerable<string> userIds, CancellationToken cancellationToken)
        {
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in userIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (UserDisplayNames.TryGetValue(id, out var name))
                {
                    results[id] = name;
                }
                else if (!results.ContainsKey(id))
                {
                    results[id] = id;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, string>>(results);
        }
    }

    private sealed class StubActivityTypeService : IActivityTypeService
    {
        public IReadOnlyList<ActivityType> ActivityTypes { get; set; } = Array.Empty<ActivityType>();

        public Task<IReadOnlyList<ActivityType>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken)
            => Task.FromResult(ActivityTypes);

        public Task<IReadOnlyList<ActivityTypeSummary>> GetSummariesAsync(CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ActivityType?> FindAsync(Guid id, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ActivityTypeMutationResult> CreateAsync(string name, string? description, int ordinal, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ActivityTypeMutationResult> UpdateAsync(Guid id, string name, string? description, bool isActive, int ordinal, byte[] rowVersion, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ActivityTypeDeletionResult> DeleteAsync(Guid id, byte[] rowVersion, CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }

    private static readonly MiscActivityMediaOptions viewServiceOptions = new()
    {
        MaxFileSizeBytes = 42,
        AllowedContentTypes = new[] { "image/png", "application/pdf" }
    };
}

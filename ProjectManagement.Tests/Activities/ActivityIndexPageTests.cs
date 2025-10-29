using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;
using ProjectManagement.Pages.Activities;
using ProjectManagement.Services.Activities;
using Xunit;

namespace ProjectManagement.Tests.Activities;

public sealed class ActivityIndexPageTests
{
    [Fact]
    public async Task OnGetAsync_BuildsViewModelWithManagerPermissions()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new ActivityListResult(new List<ActivityListItem>
        {
            new(1,
                "Engagement A",
                "Briefing",
                1,
                "HQ",
                now.AddDays(1),
                now.AddDays(1).AddHours(2),
                now.AddDays(-2),
                "user-1",
                "Avery Manager",
                "avery@example.test",
                3,
                1,
                1,
                1,
                false)
        }, 1, 1, 25, ActivityListSort.ScheduledStart, true);

        var activityService = new StubActivityService(result);
        var typeService = new StubActivityTypeService(new List<ActivityType>
        {
            new() { Id = 1, Name = "Briefing", CreatedByUserId = "seed" }
        });
        var exportService = new StubActivityExportService();
        var deleteRequestService = new StubActivityDeleteRequestService();

        var services = new ServiceCollection().BuildServiceProvider();
        var user = new ApplicationUser { Id = "user-1", UserName = "manager" };
        using var userManager = new StubUserManager(user, services, "Admin");

        var page = new IndexModel(activityService, typeService, exportService, deleteRequestService, userManager);
        ConfigurePage(page, CreatePrincipal("user-1", new[] { new Claim(ClaimTypes.Role, "Admin") }));

        var actionResult = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(actionResult);
        Assert.NotNull(page.ViewModel);
        Assert.True(page.CanCreateActivities);
        Assert.True(page.CanExportActivities);
        var row = Assert.Single(page.ViewModel!.Rows);
        Assert.True(row.CanEdit);
        Assert.True(row.CanDelete);
        Assert.Equal("Engagement A", row.Title);
        Assert.Equal("Briefing", row.ActivityType);
        Assert.Equal(3, row.AttachmentCount);
    }

    [Fact]
    public async Task OnGetAsync_AllowsProjectOfficerToManage()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new ActivityListResult(new List<ActivityListItem>
        {
            new(5,
                "Stakeholder Sync",
                "Briefing",
                3,
                "HQ",
                now.AddDays(3),
                null,
                now.AddDays(-1),
                "po-user",
                "Poppy Officer",
                "poppy@example.test",
                2,
                0,
                1,
                1,
                false)
        }, 1, 1, 25, ActivityListSort.ScheduledStart, true);

        var activityService = new StubActivityService(result);
        var typeService = new StubActivityTypeService(new List<ActivityType>
        {
            new() { Id = 3, Name = "Briefing", CreatedByUserId = "seed" }
        });
        var exportService = new StubActivityExportService();
        var deleteRequestService = new StubActivityDeleteRequestService();

        var services = new ServiceCollection().BuildServiceProvider();
        var user = new ApplicationUser { Id = "po-user", UserName = "projectofficer" };
        using var userManager = new StubUserManager(user, services, "Project Officer");

        var page = new IndexModel(activityService, typeService, exportService, deleteRequestService, userManager);
        ConfigurePage(page, CreatePrincipal("po-user", new[] { new Claim(ClaimTypes.Role, "Project Officer") }));

        var actionResult = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(actionResult);
        Assert.NotNull(page.ViewModel);
        Assert.True(page.CanCreateActivities);
        var row = Assert.Single(page.ViewModel!.Rows);
        Assert.True(row.CanEdit);
        Assert.True(row.CanDelete);
    }

    [Fact]
    public async Task OnGetAsync_DisablesManagementForOtherUser()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new ActivityListResult(new List<ActivityListItem>
        {
            new(2,
                "Workshop",
                "Training",
                2,
                null,
                now.AddDays(2),
                null,
                now.AddDays(-3),
                "owner-2",
                "Olivia Owner",
                "olivia@example.test",
                1,
                0,
                1,
                0,
                false)
        }, 1, 1, 25, ActivityListSort.ScheduledStart, true);

        var activityService = new StubActivityService(result);
        var typeService = new StubActivityTypeService(new List<ActivityType>
        {
            new() { Id = 2, Name = "Training", CreatedByUserId = "seed" }
        });
        var exportService = new StubActivityExportService();
        var deleteRequestService = new StubActivityDeleteRequestService();

        var services = new ServiceCollection().BuildServiceProvider();
        var user = new ApplicationUser { Id = "viewer-1", UserName = "viewer" };
        using var userManager = new StubUserManager(user, services);

        var page = new IndexModel(activityService, typeService, exportService, deleteRequestService, userManager);
        ConfigurePage(page, CreatePrincipal("viewer-1", null));

        var actionResult = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(actionResult);
        Assert.NotNull(page.ViewModel);
        Assert.False(page.CanCreateActivities);
        var row = Assert.Single(page.ViewModel!.Rows);
        Assert.False(row.CanEdit);
        Assert.False(row.CanDelete);
    }

    [Fact]
    public async Task OnPostExportAsync_UsesFiltersAndReturnsFile()
    {
        var activityService = new StubActivityService(new ActivityListResult(Array.Empty<ActivityListItem>(), 0, 1, 25, ActivityListSort.ScheduledStart, true));
        var typeService = new StubActivityTypeService(Array.Empty<ActivityType>());
        var exportFile = new ActivityExportResult("MiscActivities_20240101_1200.xlsx", ActivityExportService.ExcelContentType, new byte[] { 0x01, 0x02 });
        var exportService = new StubActivityExportService(exportFile);
        var deleteRequestService = new StubActivityDeleteRequestService();

        var services = new ServiceCollection().BuildServiceProvider();
        var user = new ApplicationUser { Id = "viewer", UserName = "viewer" };
        using var userManager = new StubUserManager(user, services);

        var page = new IndexModel(activityService, typeService, exportService, deleteRequestService, userManager)
        {
            SortBy = ActivityListSort.Title,
            SortDir = "asc",
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 1, 31),
            ActivityTypeId = 7,
            AttachmentType = ActivityAttachmentTypeFilter.Photo
        };
        ConfigurePage(page, CreatePrincipal("viewer", null));

        var result = await page.OnPostExportAsync(CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal(exportFile.ContentType, fileResult.ContentType);
        Assert.Equal(exportFile.FileName, fileResult.FileDownloadName);
        Assert.Equal(exportFile.Content, fileResult.FileContents);

        Assert.NotNull(exportService.LastRequest);
        var request = exportService.LastRequest!;
        Assert.Equal(ActivityListSort.Title, request.Sort);
        Assert.False(request.SortDescending);
        Assert.Equal(new DateOnly(2024, 1, 1), request.FromDate);
        Assert.Equal(new DateOnly(2024, 1, 31), request.ToDate);
        Assert.Equal(7, request.ActivityTypeId);
        Assert.Equal(ActivityAttachmentTypeFilter.Photo, request.AttachmentType);
    }

    [Fact]
    public async Task OnPostExportAsync_ShowsToastWhenNoResults()
    {
        var activityService = new StubActivityService(new ActivityListResult(Array.Empty<ActivityListItem>(), 0, 1, 25, ActivityListSort.ScheduledStart, true));
        var typeService = new StubActivityTypeService(Array.Empty<ActivityType>());
        var exportService = new StubActivityExportService(null);
        var deleteRequestService = new StubActivityDeleteRequestService();

        var services = new ServiceCollection().BuildServiceProvider();
        var user = new ApplicationUser { Id = "viewer", UserName = "viewer" };
        using var userManager = new StubUserManager(user, services);

        var page = new IndexModel(activityService, typeService, exportService, deleteRequestService, userManager);
        ConfigurePage(page, CreatePrincipal("viewer", null));

        var result = await page.OnPostExportAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("No activities match the selected filters.", page.TempData?["ToastMessage"]);
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, IEnumerable<Claim>? additionalClaims)
    {
        var identityClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };

        if (additionalClaims is not null)
        {
            identityClaims.AddRange(additionalClaims);
        }

        return new ClaimsPrincipal(new ClaimsIdentity(identityClaims, "TestAuth"));
    }

    private static void ConfigurePage(IndexModel page, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());

        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };

        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
    }

    private sealed class StubActivityService : IActivityService
    {
        private readonly ActivityListResult _result;

        public StubActivityService(ActivityListResult result)
        {
            _result = result;
        }

        public Task<Activity> CreateAsync(ActivityInput input, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<Activity> UpdateAsync(int activityId, ActivityInput input, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task DeleteAsync(int activityId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<Activity?> GetAsync(int activityId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<IReadOnlyList<Activity>> ListByTypeAsync(int activityTypeId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ActivityListResult> ListAsync(ActivityListRequest request, CancellationToken cancellationToken = default) => Task.FromResult(_result);

        public Task<IReadOnlyList<ActivityAttachmentMetadata>> GetAttachmentMetadataAsync(int activityId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ActivityAttachment> AddAttachmentAsync(int activityId, ActivityAttachmentUpload upload, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task RemoveAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class StubActivityTypeService : IActivityTypeService
    {
        private readonly IReadOnlyList<ActivityType> _types;

        public StubActivityTypeService(IReadOnlyList<ActivityType> types)
        {
            _types = types;
        }

        public Task<ActivityType> CreateAsync(ActivityTypeInput input, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ActivityType> UpdateAsync(int activityTypeId, ActivityTypeInput input, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<IReadOnlyList<ActivityType>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(_types);
    }

    private sealed class StubActivityExportService : IActivityExportService
    {
        private readonly ActivityExportResult? _result;

        public StubActivityExportService(ActivityExportResult? result = null)
        {
            _result = result;
        }

        public ActivityExportRequest? LastRequest { get; private set; }

        public Task<ActivityExportResult?> ExportAsync(ActivityExportRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubActivityDeleteRequestService : IActivityDeleteRequestService
    {
        public Task<int> RequestAsync(int activityId, string? reason, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task ApproveAsync(int requestId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RejectAsync(int requestId, string? reason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ActivityDeleteRequestSummary>> GetPendingAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ActivityDeleteRequestSummary>>(Array.Empty<ActivityDeleteRequestSummary>());
    }

    private sealed class StubUserManager : UserManager<ApplicationUser>
    {
        public StubUserManager(ApplicationUser user, IServiceProvider services, params string[] roles)
            : base(new StubUserStore(),
                   Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                   new PasswordHasher<ApplicationUser>(),
                   Array.Empty<IUserValidator<ApplicationUser>>(),
                   Array.Empty<IPasswordValidator<ApplicationUser>>(),
                   new UpperInvariantLookupNormalizer(),
                   new IdentityErrorDescriber(),
                   services,
                   NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            _user = user;
            _roles = new List<string>(roles);
        }

        private readonly ApplicationUser _user;
        private readonly IList<string> _roles;

        public override Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal) => Task.FromResult<ApplicationUser?>(_user);

        public override Task<IList<string>> GetRolesAsync(ApplicationUser user) => Task.FromResult(_roles);
    }

    private sealed class StubUserStore : IUserStore<ApplicationUser>
    {
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);

        public void Dispose()
        {
        }

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id!);

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}

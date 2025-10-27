using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.MiscActivities;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class MiscActivitiesPageModelTests
{
    [Fact]
    public async Task Index_OnGet_PopulatesViewModelAndPermissions()
    {
        var viewModel = new MiscActivityIndexViewModel
        {
            Filter = new MiscActivityIndexFilterViewModel
            {
                PageNumber = 1,
                PageSize = 25,
                ActivityTypeOptions = Array.Empty<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>(),
                CreatorOptions = Array.Empty<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>(),
                AttachmentTypeOptions = Array.Empty<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>()
            },
            Activities = new List<MiscActivityListItemViewModel>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Nomenclature = "Engagement",
                    OccurrenceDate = new DateOnly(2024, 5, 1),
                    MediaCount = 0,
                    CapturedByUserId = "user-1",
                    CapturedByDisplayName = "User One",
                    RowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3 })
                }
            },
            Pagination = new MiscActivityIndexPaginationViewModel
            {
                PageNumber = 1,
                PageSize = 25,
                TotalCount = 1,
                TotalPages = 1
            }
        };

        var viewService = new StubViewService
        {
            IndexViewModel = viewModel
        };

        var authorization = new StubAuthorizationService(ProjectOfficeReportsPolicies.ManageMiscActivities);
        var page = new IndexModel(viewService, authorization);
        ConfigurePageContext(page);

        await page.OnGetAsync(CancellationToken.None);

        Assert.True(page.CanManage);
        Assert.Equal(1, page.ViewModel.Pagination.TotalCount);
        Assert.Equal("Engagement", page.ViewModel.Activities[0].Nomenclature);
    }

    [Fact]
    public async Task Create_OnPostAsync_WhenSuccessRedirects()
    {
        var viewService = new StubViewService
        {
            CreateForm = new MiscActivityFormViewModel
            {
                ActivityTypeOptions = Array.Empty<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>()
            }
        };

        var activityService = new StubActivityService
        {
            CreateResult = MiscActivityMutationResult.Success(new MiscActivity(), new byte[] { 1 })
        };

        var page = new CreateModel(viewService, activityService)
        {
            Form = new MiscActivityFormViewModel
            {
                ActivityTypeId = Guid.NewGuid(),
                OccurrenceDate = new DateOnly(2024, 4, 15),
                Nomenclature = "Workshop"
            }
        };
        ConfigurePageContext(page);

        var result = await page.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Details", redirect.PageName);
        Assert.Equal("Activity created.", page.TempData["Flash"]);
    }

    [Fact]
    public async Task Details_OnPostUploadAsync_WhenUnauthorized_ReturnsForbid()
    {
        var viewService = new StubViewService
        {
            DetailView = new MiscActivityDetailViewModel
            {
                Id = Guid.NewGuid(),
                Nomenclature = "Activity",
                OccurrenceDate = new DateOnly(2024, 3, 10),
                Upload = new MiscActivityMediaUploadViewModel
                {
                    RowVersion = Convert.ToBase64String(new byte[] { 1 }),
                    MaxFileSizeBytes = 1_000,
                    AllowedContentTypes = new[] { "application/pdf" }
                }
            }
        };

        var activityService = new StubActivityService();
        var authorization = new StubAuthorizationService();
        var page = new DetailsModel(viewService, activityService, authorization)
        {
            Upload = new MiscActivityMediaUploadViewModel
            {
                RowVersion = Convert.ToBase64String(new byte[] { 1 })
            }
        };
        ConfigurePageContext(page);

        var result = await page.OnPostUploadAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }

    private static void ConfigurePageContext(PageModel page)
    {
        page.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[0], "Test"))
            },
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        page.TempData = new TempDataDictionary(page.PageContext.HttpContext, MockTempDataProvider.Instance);
    }

    private sealed class StubViewService : IMiscActivityViewService
    {
        public MiscActivityIndexViewModel IndexViewModel { get; set; } = new();

        public MiscActivityFormViewModel CreateForm { get; set; } = new();

        public MiscActivityFormViewModel? EditForm { get; set; }
            = new MiscActivityFormViewModel { ActivityTypeOptions = Array.Empty<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>() };

        public MiscActivityDetailViewModel? DetailView { get; set; }
            = new MiscActivityDetailViewModel();

        public Task<MiscActivityIndexViewModel> GetIndexAsync(MiscActivityQueryOptions options, CancellationToken cancellationToken)
            => Task.FromResult(IndexViewModel);

        public Task<MiscActivityFormViewModel> GetCreateFormAsync(CancellationToken cancellationToken)
            => Task.FromResult(CreateForm);

        public Task<MiscActivityFormViewModel?> GetEditFormAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(EditForm);

        public Task<MiscActivityDetailViewModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(DetailView);

        public Task<MiscActivityExportViewModel> GetExportAsync(MiscActivityExportCriteria criteria, CancellationToken cancellationToken)
            => Task.FromResult(new MiscActivityExportViewModel());
    }

    private sealed class StubActivityService : IMiscActivityService
    {
        public MiscActivityMutationResult CreateResult { get; set; }
            = MiscActivityMutationResult.Success(new MiscActivity(), new byte[] { 1 });

        public Task<int> CountAsync(MiscActivityQueryOptions options, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<IReadOnlyList<MiscActivityListItem>> SearchAsync(MiscActivityQueryOptions options, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MiscActivityListItem>>(Array.Empty<MiscActivityListItem>());

        public Task<IReadOnlyList<MiscActivityExportRow>> ExportAsync(MiscActivityQueryOptions options, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MiscActivityExportRow>>(Array.Empty<MiscActivityExportRow>());

        public Task<MiscActivity?> FindAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult<MiscActivity?>(new MiscActivity { Id = id });

        public Task<MiscActivityMutationResult> CreateAsync(MiscActivityCreateRequest request, CancellationToken cancellationToken)
            => Task.FromResult(CreateResult);

        public Task<MiscActivityMutationResult> UpdateAsync(Guid id, MiscActivityUpdateRequest request, CancellationToken cancellationToken)
            => Task.FromResult(MiscActivityMutationResult.Success(new MiscActivity { Id = id }, request.RowVersion));

        public Task<MiscActivityDeletionResult> DeleteAsync(Guid id, byte[] rowVersion, CancellationToken cancellationToken)
            => Task.FromResult(MiscActivityDeletionResult.Success());

        public Task<ActivityMediaUploadResult> UploadMediaAsync(ActivityMediaUploadRequest request, CancellationToken cancellationToken)
            => Task.FromResult(ActivityMediaUploadResult.Unauthorized());

        public Task<ActivityMediaDeletionResult> DeleteMediaAsync(ActivityMediaDeletionRequest request, CancellationToken cancellationToken)
            => Task.FromResult(ActivityMediaDeletionResult.Success(request.ActivityRowVersion));

        public Task<IReadOnlyList<MiscActivityCreatorOption>> GetCreatorsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MiscActivityCreatorOption>>(Array.Empty<MiscActivityCreatorOption>());

        public Task<IReadOnlyDictionary<string, string>> GetUserDisplayNamesAsync(IEnumerable<string> userIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }

    private sealed class StubAuthorizationService : IAuthorizationService
    {
        private readonly HashSet<string> _policies;

        public StubAuthorizationService(params string[] allowedPolicies)
        {
            _policies = new HashSet<string>(allowedPolicies ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(_policies.Contains(policyName) ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }

    private sealed class MockTempDataProvider : ITempDataProvider
    {
        public static MockTempDataProvider Instance { get; } = new();

        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}

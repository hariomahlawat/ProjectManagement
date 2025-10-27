using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
                1)
        }, 1, 1, 25, ActivityListSort.ScheduledStart, true);

        var activityService = new StubActivityService(result);
        var typeService = new StubActivityTypeService(new List<ActivityType>
        {
            new() { Id = 1, Name = "Briefing", CreatedByUserId = "seed" }
        });

        var services = new ServiceCollection().BuildServiceProvider();
        var user = new ApplicationUser { Id = "user-1", UserName = "manager" };
        using var userManager = new StubUserManager(user, services, "Admin");

        var page = new IndexModel(activityService, typeService, userManager)
        {
            PageContext = CreatePageContext("user-1", new[] { new Claim(ClaimTypes.Role, "Admin") })
        };

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
                0)
        }, 1, 1, 25, ActivityListSort.ScheduledStart, true);

        var activityService = new StubActivityService(result);
        var typeService = new StubActivityTypeService(new List<ActivityType>
        {
            new() { Id = 2, Name = "Training", CreatedByUserId = "seed" }
        });

        var services = new ServiceCollection().BuildServiceProvider();
        var user = new ApplicationUser { Id = "viewer-1", UserName = "viewer" };
        using var userManager = new StubUserManager(user, services);

        var page = new IndexModel(activityService, typeService, userManager)
        {
            PageContext = CreatePageContext("viewer-1")
        };

        var actionResult = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(actionResult);
        Assert.NotNull(page.ViewModel);
        Assert.False(page.CanCreateActivities);
        var row = Assert.Single(page.ViewModel!.Rows);
        Assert.False(row.CanEdit);
        Assert.False(row.CanDelete);
    }

    private static PageContext CreatePageContext(string userId, IEnumerable<Claim>? additionalClaims = null)
    {
        var identityClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };

        if (additionalClaims is not null)
        {
            identityClaims.AddRange(additionalClaims);
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(identityClaims, "TestAuth"))
        };

        return new PageContext
        {
            HttpContext = httpContext
        };
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
}

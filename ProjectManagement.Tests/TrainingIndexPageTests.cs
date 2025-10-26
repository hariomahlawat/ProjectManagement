using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class TrainingIndexPageTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnGetAsync_SetsCanApproveTrainingTracker(bool authorized)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(dbOptions);

        var optionsSnapshot = new StubOptionsSnapshot<TrainingTrackerOptions>(new TrainingTrackerOptions
        {
            Enabled = true
        });

        var readService = new TrainingTrackerReadService(db);
        var exportService = new StubTrainingExportService();
        var services = new ServiceCollection().BuildServiceProvider();
        using var userManager = new StubUserManager(new ApplicationUser
        {
            Id = "approver-1",
            UserName = "approver"
        }, services);

        var authorizationService = new StubAuthorizationService(authorized);

        var page = new IndexModel(optionsSnapshot, readService, exportService, userManager, authorizationService)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "approver-1")
                    }, "Test"))
                }
            }
        };

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(authorized, page.CanApproveTrainingTracker);
    }

    private sealed class StubTrainingExportService : ITrainingExportService
    {
        public Task<TrainingExportResult> ExportAsync(TrainingExportRequest request, CancellationToken cancellationToken)
            => Task.FromResult(TrainingExportResult.Failure("Not implemented."));
    }

    private sealed class StubAuthorizationService : IAuthorizationService
    {
        private readonly bool _shouldAuthorize;

        public StubAuthorizationService(bool shouldAuthorize)
        {
            _shouldAuthorize = shouldAuthorize;
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(_shouldAuthorize ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            if (_shouldAuthorize && string.Equals(policyName, ProjectOfficeReportsPolicies.ApproveTrainingTracker, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthorizationResult.Success());
            }

            return Task.FromResult(AuthorizationResult.Failed());
        }
    }

    private sealed class StubOptionsSnapshot<TOptions> : IOptionsSnapshot<TOptions>
        where TOptions : class
    {
        private readonly TOptions _value;

        public StubOptionsSnapshot(TOptions value)
        {
            _value = value;
        }

        public TOptions Value => _value;

        public TOptions Get(string? name) => _value;
    }

    private sealed class StubUserManager : UserManager<ApplicationUser>
    {
        private readonly ApplicationUser _user;

        public StubUserManager(ApplicationUser user, IServiceProvider services)
            : base(
                new StubUserStore(),
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                services,
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            _user = user;
        }

        public override Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal)
            => Task.FromResult<ApplicationUser?>(_user);
    }

    private sealed class StubUserStore : IUserStore<ApplicationUser>
    {
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public void Dispose()
        {
        }

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult<ApplicationUser?>(null);

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
            => Task.FromResult<ApplicationUser?>(null);

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.UserName);

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.Id!);

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.UserName);

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);
    }
}

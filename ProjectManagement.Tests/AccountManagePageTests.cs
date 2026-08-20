using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Identity.Pages.Account.Manage;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AccountManagePageTests
{
    [Fact]
    public async Task OnGetAsync_LoadsAssignedRoles()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(context),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services,
            new Logger<UserManager<ApplicationUser>>(new LoggerFactory()));

        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "user.one@example.com",
            FullName = "User One"
        };

        await userManager.CreateAsync(user);

        context.Roles.AddRange(
            new IdentityRole
            {
                Id = "role-1",
                Name = "ProjectOfficer",
                NormalizedName = "PROJECTOFFICER"
            },
            new IdentityRole
            {
                Id = "role-2",
                Name = "Reviewer",
                NormalizedName = "REVIEWER"
            });

        await context.SaveChangesAsync();

        await userManager.AddToRoleAsync(user, "ProjectOfficer");
        await userManager.AddToRoleAsync(user, "Reviewer");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty)
            }, "TestAuth"))
        };

        var page = new IndexModel(userManager, new NullMediaPersonUserLinkService())
        {
            PageContext = new PageContext(new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor()))
        };

        var result = await page.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(2, page.Roles.Count);
        Assert.True(page.Roles.SequenceEqual(new[] { "ProjectOfficer", "Reviewer" }));
    }
    private sealed class NullMediaPersonUserLinkService : IMediaPersonUserLinkService
    {
        public Task<MediaPersonUserLinkInfo?> GetForPersonAsync(Guid personId, CancellationToken cancellationToken)
            => Task.FromResult<MediaPersonUserLinkInfo?>(null);

        public Task<MediaPersonUserLinkInfo?> GetForUserAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult<MediaPersonUserLinkInfo?>(null);

        public Task<MediaUserPhotoIdentityLink?> GetPhotoIdentityForUserAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult<MediaUserPhotoIdentityLink?>(null);

        public Task<MediaUserPhotoIdentityLink?> TryGetPhotoIdentityForUserAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult<MediaUserPhotoIdentityLink?>(null);

        public Task<IReadOnlyList<MediaPrismUserOption>> SearchUsersAsync(string? query, int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MediaPrismUserOption>>(Array.Empty<MediaPrismUserOption>());

        public Task<MediaPersonUserLinkInfo> LinkAsync(Guid personId, string userId, string linkedByUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task SetAvatarPreferenceAsync(string userId, bool usePortraitAsAvatar, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ReportIncorrectLinkAsync(string userId, string reason, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ResolveLinkConcernAsync(Guid personId, string resolvedByUserId, string resolution, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UnlinkAsync(Guid personId, string unlinkedByUserId, string reason, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

}

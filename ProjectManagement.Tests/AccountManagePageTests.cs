using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
        await using var context = CreateApplicationContext();
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        using var userManager = CreateUserManager(context, services);
        var user = await CreateUserAsync(userManager);

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

        var page = CreatePage(userManager, new NullMediaPersonUserLinkService(), user);
        var result = await page.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(2, page.Roles.Count);
        Assert.True(page.Roles.SequenceEqual(new[] { "ProjectOfficer", "Reviewer" }));
        Assert.Equal("U", page.UserInitials);
    }

    [Fact]
    public async Task UsePhotosPortrait_UsesExplicitEnableCommand_AndTrustsVerifiedServerState()
    {
        await using var context = CreateApplicationContext();
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        using var userManager = CreateUserManager(context, services);
        var user = await CreateUserAsync(userManager);
        var links = new RecordingMediaPersonUserLinkService(hasPortrait: true, initialPreference: false);
        var page = CreatePage(userManager, links, user);

        var result = await page.OnPostUsePhotosPortraitAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.True(links.LastRequestedState);
        Assert.True(links.Current.UsePortraitAsAvatar);
        Assert.True(links.Current.ShouldUsePortraitAsAvatar);
        Assert.Contains("now being used", page.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(page.ErrorMessage);
    }


    [Fact]
    public async Task UsePhotosPortrait_DoesNotReportSuccess_WhenAuthoritativeStateDoesNotChange()
    {
        await using var context = CreateApplicationContext();
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        using var userManager = CreateUserManager(context, services);
        var user = await CreateUserAsync(userManager);
        var links = new RecordingMediaPersonUserLinkService(
            hasPortrait: true,
            initialPreference: false,
            applyRequestedState: false);
        var page = CreatePage(userManager, links, user);

        var result = await page.OnPostUsePhotosPortraitAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.True(links.LastRequestedState);
        Assert.False(links.Current.UsePortraitAsAvatar);
        Assert.Null(page.StatusMessage);
        Assert.Contains("could not verify", page.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UseInitials_UsesExplicitDisableCommand_AndVerifiesPersistedOffState()
    {
        await using var context = CreateApplicationContext();
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        using var userManager = CreateUserManager(context, services);
        var user = await CreateUserAsync(userManager);
        var links = new RecordingMediaPersonUserLinkService(hasPortrait: true, initialPreference: true);
        var page = CreatePage(userManager, links, user);

        var result = await page.OnPostUseInitialsAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.False(links.LastRequestedState);
        Assert.False(links.Current.UsePortraitAsAvatar);
        Assert.False(links.Current.ShouldUsePortraitAsAvatar);
        Assert.Contains("initials", page.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(page.ErrorMessage);
    }

    private static ApplicationDbContext CreateApplicationContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static UserManager<ApplicationUser> CreateUserManager(
        ApplicationDbContext context,
        IServiceProvider services)
        => new(
            new UserStore<ApplicationUser>(context),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services,
            new Logger<UserManager<ApplicationUser>>(new LoggerFactory()));

    private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> userManager)
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "user.one@example.com",
            FullName = "User One"
        };
        var result = await userManager.CreateAsync(user);
        Assert.True(result.Succeeded);
        return user;
    }

    private static IndexModel CreatePage(
        UserManager<ApplicationUser> userManager,
        IMediaPersonUserLinkService links,
        ApplicationUser user)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty)
            }, "TestAuth"))
        };

        return new IndexModel(userManager, links)
        {
            PageContext = new PageContext(new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor()))
        };
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

        public Task<MediaUserPhotoIdentityLink> SetAvatarPreferenceAsync(string userId, bool usePortraitAsAvatar, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ReportIncorrectLinkAsync(string userId, string reason, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ResolveLinkConcernAsync(Guid personId, string resolvedByUserId, string resolution, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UnlinkAsync(Guid personId, string unlinkedByUserId, string reason, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RecordingMediaPersonUserLinkService : IMediaPersonUserLinkService
    {
        private readonly Guid _personId = Guid.NewGuid();
        private readonly bool _applyRequestedState;

        public RecordingMediaPersonUserLinkService(
            bool hasPortrait,
            bool initialPreference,
            bool applyRequestedState = true)
        {
            _applyRequestedState = applyRequestedState;
            Current = new MediaUserPhotoIdentityLink(
                _personId,
                "Test Photos Identity",
                hasPortrait,
                initialPreference,
                HasOpenConcern: false,
                ConcernReason: null,
                ConcernRaisedAtUtc: null);
        }

        public bool LastRequestedState { get; private set; }
        public MediaUserPhotoIdentityLink Current { get; private set; }

        public Task<MediaPersonUserLinkInfo?> GetForPersonAsync(Guid personId, CancellationToken cancellationToken)
            => Task.FromResult<MediaPersonUserLinkInfo?>(null);

        public Task<MediaPersonUserLinkInfo?> GetForUserAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult<MediaPersonUserLinkInfo?>(null);

        public Task<MediaUserPhotoIdentityLink?> GetPhotoIdentityForUserAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult<MediaUserPhotoIdentityLink?>(Current);

        public Task<MediaUserPhotoIdentityLink?> TryGetPhotoIdentityForUserAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult<MediaUserPhotoIdentityLink?>(Current);

        public Task<IReadOnlyList<MediaPrismUserOption>> SearchUsersAsync(string? query, int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MediaPrismUserOption>>(Array.Empty<MediaPrismUserOption>());

        public Task<MediaPersonUserLinkInfo> LinkAsync(Guid personId, string userId, string linkedByUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<MediaUserPhotoIdentityLink> SetAvatarPreferenceAsync(
            string userId,
            bool usePortraitAsAvatar,
            CancellationToken cancellationToken)
        {
            LastRequestedState = usePortraitAsAvatar;
            if (_applyRequestedState)
            {
                Current = Current with { UsePortraitAsAvatar = usePortraitAsAvatar };
            }
            return Task.FromResult(Current);
        }

        public Task ReportIncorrectLinkAsync(string userId, string reason, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ResolveLinkConcernAsync(Guid personId, string resolvedByUserId, string resolution, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UnlinkAsync(Guid personId, string unlinkedByUserId, string reason, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}

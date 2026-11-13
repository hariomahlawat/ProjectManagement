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
using ProjectManagement.Services.Accounts;
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
    public async Task OnGetAsync_ShowsWhoTheAccountBelongsTo()
    {
        await using var context = CreateApplicationContext();
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        using var userManager = CreateUserManager(context, services);
        var user = await CreateUserAsync(userManager);
        user.Rank = "Lt Col";
        // 00:10 UTC on 12 Nov is 05:40 the same day in India.
        user.LastLoginUtc = new DateTime(2026, 11, 12, 0, 10, 0, DateTimeKind.Utc);
        await userManager.UpdateAsync(user);

        var page = CreatePage(userManager, new NullMediaPersonUserLinkService(), user);
        await page.OnGetAsync();

        Assert.Equal("Lt Col User One", page.DisplayName);
        Assert.Equal("user.one@example.com", page.UserName);
        Assert.Equal("12 Nov 2026, 05:40", page.LastSignInText);
    }

    [Fact]
    public async Task OnGetAsync_ListsTheUsersOwnRecentSignIns_AndMarksThisAddress()
    {
        await using var context = CreateApplicationContext();
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        using var userManager = CreateUserManager(context, services);
        var user = await CreateUserAsync(userManager);
        var history = new FixedSignInHistory(
            new AccountSignIn(new DateTimeOffset(2026, 11, 12, 0, 10, 0, TimeSpan.Zero), "10.1.2.3", "Chrome 142 on Windows"),
            new AccountSignIn(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero), "10.1.2.9", "Edge 141 on Windows"),
            new AccountSignIn(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero), null, "Unknown browser"));

        var page = CreatePage(userManager, new NullMediaPersonUserLinkService(), user, history, remoteAddress: "10.1.2.3");
        await page.OnGetAsync();

        Assert.Equal("user-1", history.RequestedUserId);
        Assert.Equal(IndexModel.RecentSignInCount, history.RequestedCount);
        Assert.Equal(3, page.RecentSignIns.Count);
        // 00:10 UTC is 05:40 in India.
        Assert.Equal("12 Nov 2026, 05:40", page.RecentSignIns[0].When);
        Assert.Equal("Chrome 142 on Windows", page.RecentSignIns[0].Client);
        Assert.True(page.RecentSignIns[0].FromThisAddress);
        Assert.False(page.RecentSignIns[1].FromThisAddress);
        Assert.Null(page.RecentSignIns[2].Ip);
        Assert.False(page.RecentSignIns[2].FromThisAddress);
    }

    [Fact]
    public async Task OnGetAsync_FallsBackToTheUserName_AndNoSignInTime()
    {
        await using var context = CreateApplicationContext();
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        using var userManager = CreateUserManager(context, services);
        var user = await CreateUserAsync(userManager);
        user.FullName = string.Empty;
        await userManager.UpdateAsync(user);

        var page = CreatePage(userManager, new NullMediaPersonUserLinkService(), user);
        await page.OnGetAsync();

        Assert.Equal("user.one@example.com", page.DisplayName);
        Assert.Null(page.LastSignInText);
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

    private sealed class FixedSignInHistory : IAccountSignInHistoryService
    {
        private readonly IReadOnlyList<AccountSignIn> _signIns;

        public FixedSignInHistory(params AccountSignIn[] signIns) => _signIns = signIns;

        public string? RequestedUserId { get; private set; }

        public int RequestedCount { get; private set; }

        public Task<IReadOnlyList<AccountSignIn>> GetRecentAsync(string userId, int count, CancellationToken cancellationToken)
        {
            RequestedUserId = userId;
            RequestedCount = count;
            return Task.FromResult(_signIns);
        }
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
        ApplicationUser user,
        IAccountSignInHistoryService? signIns = null,
        string? remoteAddress = null)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty)
            }, "TestAuth"))
        };

        if (remoteAddress is not null)
        {
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteAddress);
        }

        return new IndexModel(userManager, links, signIns ?? new FixedSignInHistory())
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

        public Task<IReadOnlyDictionary<string, Guid>> TryGetAvatarPersonIdsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, Guid>>(new Dictionary<string, Guid>());

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

        public Task<IReadOnlyDictionary<string, Guid>> TryGetAvatarPersonIdsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, Guid>>(new Dictionary<string, Guid>());

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

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Contracts.Notifications;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Tests.Infrastructure;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class NotificationEndpointTests
{
    private const string UserId = "notification-api-user";

    [Fact]
    public async Task AuthenticatedLayout_RendersUsableAntiforgeryToken()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/Notifications");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(
            new Regex("<meta\\s+name=\"csrf-token\"\\s+content=\"[^\"]+\"", RegexOptions.IgnoreCase),
            html);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders));
        Assert.NotNull(setCookieHeaders);
        Assert.Contains(
            setCookieHeaders!,
            header => header.Contains("PMAntiforgery", StringComparison.Ordinal));
    }


    [Fact]
    public async Task NotificationApi_AnonymousRequestReturns401WithoutLoginRedirect()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var response = await client.GetAsync("/api/notifications/count");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task NotificationMutation_RejectsMissingAntiforgeryToken()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/notifications/read",
            new NotificationIdsRequest(new[] { 1 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NotificationApi_ReturnsPageContractAndProcessesBulkRead()
    {
        using var factory = CreateFactory();
        var notificationId = await SeedNotificationAsync(factory, projectId: null);
        using var client = CreateAuthenticatedClient(factory);
        var token = await GetAntiforgeryTokenAsync(client);

        var page = await client.GetFromJsonAsync<NotificationPageDto>(
            "/api/notifications?limit=10&includeFilterOptions=true");

        Assert.NotNull(page);
        Assert.Contains(page.Items, item => item.Id == notificationId);
        Assert.Equal(1, page.UnreadCount);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/notifications/read")
        {
            Content = JsonContent.Create(new NotificationIdsRequest(new[] { notificationId }))
        };
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await client.SendAsync(request);
        var mutation = await response.Content.ReadFromJsonAsync<NotificationMutationDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(mutation);
        Assert.True(mutation.IsRead);
        Assert.Contains(notificationId, mutation.NotificationIds);
        Assert.Equal(1, mutation.AffectedCount);
        Assert.Equal(0, mutation.UnreadCount);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Notifications.SingleAsync(item => item.Id == notificationId);
        Assert.NotNull(stored.ReadUtc);
        Assert.NotNull(stored.SeenUtc);
    }

    [Fact]
    public async Task SeenEndpoint_UpdatesOnlyTheAuthenticatedUsersNotification()
    {
        using var factory = CreateFactory();
        var notificationId = await SeedNotificationAsync(factory, projectId: null);
        using var client = CreateAuthenticatedClient(factory);
        var token = await GetAntiforgeryTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/notifications/seen")
        {
            Content = JsonContent.Create(new NotificationIdsRequest(new[] { notificationId }))
        };
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await client.SendAsync(request);
        var mutation = await response.Content.ReadFromJsonAsync<NotificationSeenDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(mutation);
        Assert.Contains(notificationId, mutation.NotificationIds);
        Assert.Equal(1, mutation.AffectedCount);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotNull((await db.Notifications.SingleAsync(item => item.Id == notificationId)).SeenUtc);
    }

    [Fact]
    public async Task ProjectMute_ReturnsDetailedMutationAndClosesUnreadBacklog()
    {
        using var factory = CreateFactory();
        var notificationId = await SeedNotificationAsync(factory, projectId: 42);
        using var client = CreateAuthenticatedClient(factory);
        var token = await GetAntiforgeryTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/notifications/projects/42/mute");
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await client.SendAsync(request);
        var mutation = await response.Content.ReadFromJsonAsync<NotificationProjectMuteDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(mutation);
        Assert.True(mutation.IsMuted);
        Assert.Equal(42, mutation.ProjectId);
        Assert.Contains(notificationId, mutation.ChangedNotificationIds);
        Assert.Equal(0, mutation.UnreadCount);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.UserProjectMutes.AnyAsync(item => item.UserId == UserId && item.ProjectId == 42));
        Assert.NotNull((await db.Notifications.SingleAsync(item => item.Id == notificationId)).ReadUtc);
    }


    [Fact]
    public async Task NotificationApi_ExposesUnreadReadAllUnmuteAndLegacyRoutes()
    {
        using var factory = CreateFactory();
        var firstId = await SeedNotificationAsync(factory, projectId: 43);
        var secondId = await SeedNotificationAsync(factory, projectId: null);
        using var client = CreateAuthenticatedClient(factory);
        var token = await GetAntiforgeryTokenAsync(client);

        var markRead = await SendMutationAsync(
            client,
            HttpMethod.Post,
            "/api/notifications/read",
            token,
            new NotificationIdsRequest(new[] { firstId }));
        Assert.Equal(HttpStatusCode.OK, markRead.StatusCode);

        var markUnread = await SendMutationAsync(
            client,
            HttpMethod.Post,
            "/api/notifications/unread",
            token,
            new NotificationIdsRequest(new[] { firstId }));
        var unreadMutation = await markUnread.Content.ReadFromJsonAsync<NotificationMutationDto>();
        Assert.Equal(HttpStatusCode.OK, markUnread.StatusCode);
        Assert.NotNull(unreadMutation);
        Assert.False(unreadMutation.IsRead);
        Assert.Contains(firstId, unreadMutation.NotificationIds);

        var legacyRead = await SendMutationAsync(
            client,
            HttpMethod.Post,
            $"/api/notifications/{firstId}/read",
            token);
        Assert.Equal(HttpStatusCode.OK, legacyRead.StatusCode);

        var readAll = await SendMutationAsync(
            client,
            HttpMethod.Post,
            "/api/notifications/read-all",
            token);
        var readAllMutation = await readAll.Content.ReadFromJsonAsync<NotificationMutationDto>();
        Assert.Equal(HttpStatusCode.OK, readAll.StatusCode);
        Assert.NotNull(readAllMutation);
        Assert.True(readAllMutation.AppliesToAll);
        Assert.True(readAllMutation.IsRead);
        Assert.Equal(0, readAllMutation.UnreadCount);

        var mute = await SendMutationAsync(
            client,
            HttpMethod.Post,
            "/api/notifications/projects/43/mute",
            token);
        Assert.Equal(HttpStatusCode.OK, mute.StatusCode);

        var unmute = await SendMutationAsync(
            client,
            HttpMethod.Delete,
            "/api/notifications/projects/43/mute",
            token);
        var unmuteMutation = await unmute.Content.ReadFromJsonAsync<NotificationProjectMuteDto>();
        Assert.Equal(HttpStatusCode.OK, unmute.StatusCode);
        Assert.NotNull(unmuteMutation);
        Assert.False(unmuteMutation.IsMuted);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.UserProjectMutes.AnyAsync(item => item.UserId == UserId && item.ProjectId == 43));
        Assert.NotNull((await db.Notifications.SingleAsync(item => item.Id == secondId)).ReadUtc);
    }

    [Fact]
    public async Task NotificationGetEndpoints_ArePrivateAndReturnNoStoreHeaders()
    {
        using var factory = CreateFactory();
        await SeedNotificationAsync(factory, projectId: null);
        using var client = CreateAuthenticatedClient(factory);

        var pageResponse = await client.GetAsync("/api/notifications?limit=10");
        var countResponse = await client.GetAsync("/api/notifications/count");

        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, countResponse.StatusCode);
        Assert.Contains("no-store", pageResponse.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", countResponse.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static NotificationApiFactory CreateFactory() => new();


    private static async Task<HttpResponseMessage> SendMutationAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string token,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static HttpClient CreateAuthenticatedClient(NotificationApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-User", UserId);
        return client;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/Notifications");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var match = Regex.Match(
            html,
            "<meta\\s+name=\"csrf-token\"\\s+content=\"(?<token>[^\"]+)\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "The authenticated layout did not render the antiforgery request token.");
        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    private static async Task<int> SeedNotificationAsync(NotificationApiFactory factory, int? projectId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (projectId.HasValue && !await db.Projects.AnyAsync(project => project.Id == projectId.Value))
        {
            db.Projects.Add(new Project
            {
                Id = projectId.Value,
                Name = "Notification Test Project",
                LeadPoUserId = UserId,
                CreatedByUserId = UserId,
            });
        }

        var notification = new Notification
        {
            RecipientUserId = UserId,
            ProjectId = projectId,
            Kind = NotificationKind.StageStatusChanged,
            Module = "Projects",
            EventType = "StageStatusChanged",
            Title = "Stage updated",
            Summary = "A project stage was updated.",
            CreatedUtc = DateTime.UtcNow,
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification.Id;
    }

    private sealed class NotificationApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UsePrismTestInfrastructure("notification-api");
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                        options.DefaultScheme = "Test";
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers["X-Test-User"].ToString();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Name, "Notification Test User"),
                },
                Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}

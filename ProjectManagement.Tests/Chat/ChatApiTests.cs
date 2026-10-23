using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Data;
using ProjectManagement.Hubs;
using ProjectManagement.Models;
using ProjectManagement.Tests.Infrastructure;
using Xunit;

namespace ProjectManagement.Tests.Chat;

/// <summary>
/// The chat HTTP API end to end: authentication, antiforgery, the switch-off 404, rate limits,
/// and what is pushed to whom. Runs on SQLite (the services use ExecuteUpdate/ExecuteDelete and
/// transactions, which the InMemory provider lacks); production is PostgreSQL.
/// </summary>
[Collection(ChatApiCollection.Name)]
public sealed class ChatApiTests
{
    [Fact]
    public async Task AnonymousRequests_Get401()
    {
        await using var host = await ChatHost.CreateAsync();
        using var client = host.Anonymous();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/chat/contacts")).StatusCode);
    }

    [Theory]
    [InlineData("/api/chat/messages")]
    [InlineData("/api/chat/status")]
    [InlineData("/api/chat/groups")]
    [InlineData("/api/chat/with/bob/read")]
    public async Task Writes_WithoutTheAntiforgeryToken_AreRefused(string path)
    {
        await using var host = await ChatHost.CreateAsync();
        using var client = host.ClientFor("alice");

        var response = await client.PostAsJsonAsync(path, new { recipientUserId = "bob", clientMessageId = Guid.NewGuid(), body = "hi", message = "x", title = "x", memberUserIds = new[] { "bob" }, upToMessageId = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Hub.Pushes);
    }

    [Fact]
    public async Task Sending_StoresTheMessage_AndPushesItToBothPeopleOnly()
    {
        await using var host = await ChatHost.CreateAsync();
        using var client = host.ClientFor("alice");
        var token = await host.TokenAsync(client);

        var response = await host.PostAsync(client, token, "/api/chat/messages", new ChatSendRequest("bob", Guid.NewGuid(), "Lunch at one?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var push = Assert.Single(host.Hub.Pushes);
        Assert.Equal(nameof(IChatClient.ReceiveMessage), push.Method);
        Assert.Equal(new[] { "alice", "bob" }, push.Targets.OrderBy(t => t));
    }

    [Fact]
    public async Task GroupTraffic_GoesToMembers_OnGroupEvents()
    {
        await using var host = await ChatHost.CreateAsync();
        using var client = host.ClientFor("alice");
        var token = await host.TokenAsync(client);

        var created = await host.PostAsync(client, token, "/api/chat/groups", new ChatGroupCreateRequest("Desk", new[] { "bob" }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var group = await created.Content.ReadFromJsonAsync<ChatGroupDto>();
        host.Hub.Pushes.Clear();

        var sent = await host.PostAsync(client, token, $"/api/chat/groups/{group!.Id}/messages", new ChatGroupSendRequest(Guid.NewGuid(), "Agenda"));

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var push = Assert.Single(host.Hub.Pushes);
        Assert.Equal(nameof(IChatClient.ReceiveGroupMessage), push.Method);
        Assert.Equal(new[] { "alice", "bob" }, push.Targets.OrderBy(t => t));

        // Someone outside the group gets 410, not 404 (which the browser reads as "chat is off").
        using var carol = host.ClientFor("carol");
        var carolToken = await host.TokenAsync(carol);
        var refused = await host.PostAsync(carol, carolToken, $"/api/chat/groups/{group.Id}/messages", new ChatGroupSendRequest(Guid.NewGuid(), "let me in"));
        Assert.Equal(HttpStatusCode.Gone, refused.StatusCode);
    }

    [Fact]
    public async Task WhenChatIsSwitchedOff_EveryEndpointSays404_WithTheSwitchedOffHeader()
    {
        await using var host = await ChatHost.CreateAsync(new Dictionary<string, string?> { ["Chat:Enabled"] = "false" });
        using var client = host.ClientFor("alice");

        var response = await client.GetAsync("/api/chat/contacts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Chat-Disabled", out var values));
        Assert.Equal("true", Assert.Single(values!));
    }

    [Fact]
    public async Task AnUnknownChatPath_Is404_WithoutTheSwitchedOffHeader()
    {
        await using var host = await ChatHost.CreateAsync();
        using var client = host.ClientFor("alice");

        var response = await client.GetAsync("/api/chat/groups/5.5/messages");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Chat-Disabled"));
    }

    [Fact]
    public async Task StatusChanges_AreRateLimitedPerUser()
    {
        await using var host = await ChatHost.CreateAsync(new Dictionary<string, string?>
        {
            ["Chat:ChangePermitLimit"] = "2",
            ["Chat:ChangeWindowSeconds"] = "600",
        });
        using var client = host.ClientFor("alice");
        var token = await host.TokenAsync(client);

        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(client, token, "/api/chat/status", new ChatStatusRequest("one"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(client, token, "/api/chat/status", new ChatStatusRequest("two"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.PostAsync(client, token, "/api/chat/status", new ChatStatusRequest("three"))).StatusCode);
        // Group changes share the allowance.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.PostAsync(client, token, "/api/chat/groups", new ChatGroupCreateRequest("Desk", new[] { "bob" }))).StatusCode);
        Assert.Equal(2, host.Hub.Pushes.Count(p => p.Method == nameof(IChatClient.StatusChanged)));

        // Another person has their own allowance; sending messages is limited separately.
        using var bob = host.ClientFor("bob");
        var bobToken = await host.TokenAsync(bob);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(bob, bobToken, "/api/chat/status", new ChatStatusRequest("free"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(client, token, "/api/chat/messages", new ChatSendRequest("bob", Guid.NewGuid(), "still fine"))).StatusCode);
    }

    [Fact]
    public async Task Searches_AreRateLimitedPerUser()
    {
        await using var host = await ChatHost.CreateAsync(new Dictionary<string, string?>
        {
            ["Chat:SearchPermitLimit"] = "1",
            ["Chat:SearchWindowSeconds"] = "600",
        });
        using var client = host.ClientFor("alice");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/chat/search?q=lunch")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/chat/search?q=lunch")).StatusCode);
    }

    private sealed class ChatHost : IAsyncDisposable
    {
        private const string UserHeader = "X-Chat-Test-User";

        private readonly SqliteConnection _connection;
        private readonly WebApplicationFactory<Program> _factory;

        private ChatHost(SqliteConnection connection, WebApplicationFactory<Program> factory, RecordingHubContext hub)
        {
            _connection = connection;
            _factory = factory;
            Hub = hub;
        }

        public RecordingHubContext Hub { get; }

        public static async Task<ChatHost> CreateAsync(IDictionary<string, string?>? settings = null)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options))
            {
                await db.Database.EnsureCreatedAsync();
                db.Users.AddRange(Human("alice", "Alice Anand"), Human("bob", "Bob Bhatia"), Human("carol", "Carol Chauhan"));
                await db.SaveChangesAsync();
            }

            var hub = new RecordingHubContext();
            var started = new StartupGate();
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UsePrismTestInfrastructure("chat-api");
                if (settings is not null)
                {
                    builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
                }

                builder.ConfigureServices(services =>
                {
                    PrismTestWebHostBuilderExtensions.RemoveDbContextRegistrations<ApplicationDbContext>(services);
                    services.AddDbContext<ApplicationDbContext>(options =>
                    {
                        // Startup's migration boundary runs on InMemory; requests use SQLite.
                        if (started.IsOpen)
                        {
                            options.UseSqlite(connection);
                        }
                        else
                        {
                            options.UseInMemoryDatabase("chat-api-startup-" + Guid.NewGuid().ToString("N"));
                        }
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHubContext<ChatHub, IChatClient>>();
                    services.AddSingleton<IHubContext<ChatHub, IChatClient>>(hub);
                    services.AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = "ChatTest";
                            options.DefaultChallengeScheme = "ChatTest";
                            options.DefaultScheme = "ChatTest";
                        })
                        .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("ChatTest", _ => { });
                });
            });

            _ = factory.Server;
            started.IsOpen = true;
            return new ChatHost(connection, factory, hub);
        }

        public HttpClient ClientFor(string userId)
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            client.DefaultRequestHeaders.Add(UserHeader, userId);
            return client;
        }

        public HttpClient Anonymous()
            => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

        /// <summary>The request token the layout renders for this client's antiforgery cookie.</summary>
        public async Task<string> TokenAsync(HttpClient client)
        {
            var response = await client.GetAsync("/Chat");
            var html = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET /Chat answered {(int)response.StatusCode} {response.Headers.Location}");
            var match = Regex.Match(html, "<meta\\s+name=\"csrf-token\"\\s+content=\"(?<token>[^\"]+)\"", RegexOptions.IgnoreCase);
            Assert.True(match.Success, "The layout did not render the antiforgery request token.");
            return WebUtility.HtmlDecode(match.Groups["token"].Value);
        }

        public async Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string token, string path, T body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-CSRF-TOKEN", token);
            return await client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            await _factory.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static ApplicationUser Human(string id, string name) => new()
        {
            Id = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            FullName = name,
            AccountKind = UserAccountKind.Human,
            MustChangePassword = false,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        private sealed class StartupGate
        {
            public volatile bool IsOpen;
        }

        private sealed class HeaderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public HeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
                : base(options, logger, encoder)
            {
            }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                var userId = Request.Headers[UserHeader].ToString();
                if (string.IsNullOrEmpty(userId))
                {
                    return Task.FromResult(AuthenticateResult.NoResult());
                }

                var principal = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Name, userId) },
                    // The layout renders the antiforgery token only for an Identity sign-in.
                    IdentityConstants.ApplicationScheme));
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "ChatTest")));
            }
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ChatApiCollection
{
    public const string Name = "Chat API (isolated host)";
}

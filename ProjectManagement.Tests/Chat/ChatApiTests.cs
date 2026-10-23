using System;
using System.Collections.Generic;
using System.IO;
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
using ProjectManagement.Services.Chat;
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
    public async Task Pages_LoadEveryChatScriptInOrder_AndEachIsServed()
    {
        await using var host = await ChatHost.CreateAsync();
        using var client = host.ClientFor("alice");

        var html = await client.GetStringAsync("/Chat");
        var scripts = Regex.Matches(html, "src=\"(?<src>/js/chat/[\\w-]+\\.js)(\\?[^\"]*)?\"")
            .Select(m => m.Groups["src"].Value)
            .ToList();

        Assert.Equal(
            new[] { "core", "app", "events", "dock", "windows", "conversation", "files", "page" }.Select(n => $"/js/chat/{n}.js"),
            scripts);
        Assert.DoesNotContain("/js/chat.js", html);
        foreach (var src in scripts)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(src)).StatusCode);
        }
    }

    [Fact]
    public async Task Previews_ShowRecordsTheCallerMayOpen_AndAreBounded()
    {
        await using var host = await ChatHost.CreateAsync();
        await using (var db = host.NewContext())
        {
            db.Projects.Add(new Project { Id = 7, Name = "Simulator Upgrade", CreatedByUserId = "alice", LeadPoUserId = "bob" });
            db.ActionTasks.Add(new ActionTaskItem
            {
                Id = 3, Title = "Private task", Description = "-", CreatedByUserId = "carol", AssignedToUserId = "carol",
                CreatedByRole = "HoD", AssignedToRole = "Project Officer", AssignedOn = DateTime.UtcNow, DueDate = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var client = host.ClientFor("alice");
        var cards = await client.GetFromJsonAsync<ChatLinkPreviewDto[]>(
            "/api/chat/previews?link=" + Uri.EscapeDataString("/Projects/Overview/7")
            + "&link=" + Uri.EscapeDataString("/ActionTasks/Details/3")
            + "&link=" + Uri.EscapeDataString("/Projects/Overview/404"));

        var card = Assert.Single(cards!); // alice has no Action Tracker role: the task stays hidden
        Assert.Equal("Simulator Upgrade", card.Title);
        Assert.Equal("Project officer: Bob Bhatia", card.Subtitle);

        var tooMany = "/api/chat/previews?" + string.Join("&", Enumerable.Range(1, 11).Select(i => "link=%2FProjects%2FOverview%2F" + i));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(tooMany)).StatusCode);
    }

    [Fact]
    public async Task Attachments_AreAbsentWhileSwitchedOff()
    {
        // Switched off explicitly: appsettings.json now ships with attachments on.
        await using var host = await ChatHost.CreateAsync(new Dictionary<string, string?> { ["Chat:Attachments:Enabled"] = "false" });
        using var client = host.ClientFor("alice");

        var response = await client.GetAsync("/api/chat/attachments/usage");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Chat-Disabled"), "chat itself is on");
        Assert.Contains("data-attachments=\"off\"", await client.GetStringAsync("/Chat"));
    }

    [Fact]
    public async Task Attachments_UploadSendAndDownload_WithEachDownloadChecked()
    {
        await using var host = await ChatHost.CreateAsync(new Dictionary<string, string?> { ["Chat:Attachments:Enabled"] = "true" });
        using var alice = host.ClientFor("alice");
        var token = await host.TokenAsync(alice);
        Assert.Contains("data-attachments=\"on\"", await alice.GetStringAsync("/Chat"));

        // Without the antiforgery token, nothing is stored.
        using (var anonymousForm = PdfForm("minutes.pdf"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsync("/api/chat/attachments", anonymousForm)).StatusCode);
        }

        using var form = PdfForm("minutes.pdf");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/chat/attachments") { Content = form };
        upload.Headers.Add("X-CSRF-TOKEN", token);
        var uploaded = await alice.SendAsync(upload);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var file = (await uploaded.Content.ReadFromJsonAsync<ChatAttachmentDto>())!;
        Assert.Equal("minutes.pdf", file.FileName);

        var sent = await host.PostAsync(alice, token, "/api/chat/messages", new ChatSendRequest("bob", Guid.NewGuid(), "", new[] { file.Id }));
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var push = Assert.Single(host.Hub.Pushes, p => p.Method == nameof(IChatClient.ReceiveMessage));
        Assert.Equal("minutes.pdf", Assert.Single(((ChatMessageDto)push.Payload!).Attachments!).FileName);

        using var bob = host.ClientFor("bob");
        var download = await bob.GetAsync($"/api/chat/attachments/{file.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("minutes.pdf", download.Content.Headers.ContentDisposition.FileNameStar ?? download.Content.Headers.ContentDisposition.FileName);
        Assert.Contains("no-store", download.Headers.CacheControl!.ToString());
        Assert.StartsWith("%PDF-", await download.Content.ReadAsStringAsync());

        using var carol = host.ClientFor("carol");
        Assert.Equal(HttpStatusCode.Gone, (await carol.GetAsync($"/api/chat/attachments/{file.Id}")).StatusCode);

        // Only administrators see storage totals.
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/chat/attachments/totals")).StatusCode);
    }

    private static async Task<long> UploadAndSendAsync(ChatHost host, HttpClient client, string token, string to, string name)
    {
        using var form = PdfForm(name);
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/chat/attachments") { Content = form };
        upload.Headers.Add("X-CSRF-TOKEN", token);
        var file = (await (await client.SendAsync(upload)).Content.ReadFromJsonAsync<ChatAttachmentDto>())!;
        var sent = await host.PostAsync(client, token, "/api/chat/messages", new ChatSendRequest(to, Guid.NewGuid(), "", new[] { file.Id }));
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        return file.Id;
    }

    [Fact]
    public async Task MyFiles_ListRemoveRestore_OverHttp()
    {
        await using var host = await ChatHost.CreateAsync(new Dictionary<string, string?> { ["Chat:Attachments:Enabled"] = "true" });
        using var alice = host.ClientFor("alice");
        var token = await host.TokenAsync(alice);
        var id = await UploadAndSendAsync(host, alice, token, "bob", "plan.pdf");

        var mine = await alice.GetFromJsonAsync<ChatMyFilesDto>("/api/chat/attachments/mine");
        Assert.Equal("plan.pdf", Assert.Single(mine!.Files).FileName);
        Assert.Equal("Bob Bhatia", mine.Files[0].ConversationName);

        host.Hub.Pushes.Clear();
        var removed = await host.PostAsync(alice, token, "/api/chat/attachments/mine/remove", new ChatFileIdsRequest(new[] { id }));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var push = Assert.Single(host.Hub.Pushes);
        Assert.Equal(nameof(IChatClient.AttachmentRemoved), push.Method);
        Assert.Equal(new[] { "alice", "bob" }, push.Targets.OrderBy(t => t));

        var restored = await host.PostAsync(alice, token, "/api/chat/attachments/mine/restore", new ChatFileIdsRequest(new[] { id }));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(nameof(IChatClient.AttachmentRestored), host.Hub.Pushes.Last().Method);

        var nothing = await host.PostAsync(alice, token, "/api/chat/attachments/mine/remove-older", new ChatRemoveOlderRequest(30));
        Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode); // nothing that old
    }

    [Fact]
    public async Task AdminChatStorage_IsForAdministrators_PreviewConfirmAndUndo()
    {
        await using var host = await ChatHost.CreateAsync(new Dictionary<string, string?> { ["Chat:Attachments:Enabled"] = "true" });
        using var alice = host.ClientFor("alice");
        var token = await host.TokenAsync(alice);
        var id = await UploadAndSendAsync(host, alice, token, "bob", "secret-plans.pdf");

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/Admin/Chat/Storage")).StatusCode);

        using var admin = host.ClientFor("carol", "Admin");
        var preview = await admin.GetStringAsync("/Admin/Chat/Storage?preview=true");
        Assert.Contains("This removes", preview);
        Assert.Contains("Alice Anand", preview);
        Assert.DoesNotContain("secret-plans", preview); // sizes and people only
        var formToken = Regex.Match(preview, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<t>[^\"]+)\"").Groups["t"].Value;
        Assert.False(string.IsNullOrEmpty(formToken));

        // Without typing REMOVE nothing happens.
        var unconfirmed = await admin.PostAsync("/Admin/Chat/Storage?handler=Remove", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = formToken, ["Confirmation"] = "yes",
        }));
        Assert.Equal(HttpStatusCode.Redirect, unconfirmed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/chat/attachments/{id}")).StatusCode);

        var confirmed = await admin.PostAsync("/Admin/Chat/Storage?handler=Remove", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = formToken, ["Confirmation"] = "REMOVE",
        }));
        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await alice.GetAsync($"/api/chat/attachments/{id}")).StatusCode);

        var page = await admin.GetStringAsync("/Admin/Chat/Storage");
        Assert.Contains("Removed 1 file(s)", page);
        Assert.Contains("handler=Undo", page);
        var batchId = Regex.Match(page, "name=\"batchId\" value=\"(?<id>[0-9a-f-]+)\"").Groups["id"].Value;
        var undo = await admin.PostAsync("/Admin/Chat/Storage?handler=Undo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = formToken, ["batchId"] = batchId,
        }));
        Assert.Equal(HttpStatusCode.Redirect, undo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/chat/attachments/{id}")).StatusCode);
    }

    private static MultipartFormDataContent PdfForm(string name)
    {
        var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"));
        bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(bytes, "file", name);
        return form;
    }

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
        private const string RolesHeader = "X-Chat-Test-Roles";

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
            var uploadRoot = Path.Combine(Path.GetTempPath(), "prism-chat-api-" + Guid.NewGuid().ToString("N"));
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UsePrismTestInfrastructure("chat-api");
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["ProjectPhotos:StorageRoot"] = uploadRoot }));
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
                    services.RemoveAll<IChatDiskSpace>();
                    services.AddSingleton<IChatDiskSpace>(new PlentyOfDisk());
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

        public HttpClient ClientFor(string userId, params string[] roles)
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            client.DefaultRequestHeaders.Add(UserHeader, userId);
            if (roles.Length > 0)
            {
                client.DefaultRequestHeaders.Add(RolesHeader, string.Join(",", roles));
            }

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

        public ApplicationDbContext NewContext()
            => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

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

        private sealed class PlentyOfDisk : IChatDiskSpace
        {
            public long? AvailableBytes(string path) => 500L * 1024 * 1024 * 1024;
        }

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

                var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Name, userId) };
                claims.AddRange(Request.Headers[RolesHeader].ToString()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(role => new Claim(ClaimTypes.Role, role)));
                var principal = new ClaimsPrincipal(new ClaimsIdentity(
                    claims,
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

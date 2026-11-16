using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Admin.Pages.CollageStickers;
using ProjectManagement.Features.Collage;
using ProjectManagement.Services.Admin;
using ProjectManagement.Tests.Tools;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Administration › Crests and logos through the real PRISM host: a signed-in user who is not an
/// administrator is refused every change with 403, even with a valid antiforgery token, while an
/// administrator gets past authorization; and an upload over the request limit gets the page's
/// plain message, not the framework's bare 413.
/// </summary>
public sealed class CollageStickersAdminHostTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string PagePath = "/Admin/CollageStickers";
    private const string UserHeader = "X-Test-User";
    private const int RequestLimit = (int)IndexModel.RequestLimitBytes;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly RecordingAudit _audit = new();

    public CollageStickersAdminHostTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseCollageTestHost("collage-stickers-admin");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAdminAuditService>(_audit);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                    options.DefaultForbidScheme = TestAuthHandler.Scheme;
                    options.DefaultScheme = TestAuthHandler.Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            });
        });
    }

    public static TheoryData<string> Handlers => new() { "Add", "Rename", "Move", "Retire", "Restore" };

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task A_signed_in_user_who_is_not_an_administrator_is_refused_each_change(string handler)
    {
        var response = await PostAsync("member", handler, SmallForm(handler));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_audit.Entries);
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task An_administrator_gets_past_authorization_and_the_antiforgery_check(string handler)
    {
        var response = await PostAsync("admin", handler, SmallForm(handler));

        // The handler ran and answered with the page's own redirect and message.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith(PagePath, response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_the_antiforgery_token_an_administrator_is_refused()
    {
        var client = CreateClient("admin");

        var response = await client.PostAsync(PagePath + "?handler=Retire", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = "1" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_upload_over_the_request_limit_gets_the_plain_message_and_an_audit_entry()
    {
        var client = CreateClient("admin");
        var form = new MultipartFormDataContent
        {
            { new StringContent("Crest"), "name" },
            { new ByteArrayContent(new byte[RequestLimit + 1]), "picture", "crest.png" }
        };

        var response = await PostAsync("admin", "Add", form, client);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // The message travels in the TempData cookie, which the client keeps.
        var page = await client.GetAsync(PagePath);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains(HtmlEncoder.Default.Encode(CollageStickerService.TooLargeMessage), await page.Content.ReadAsStringAsync());
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("CollageStickerRefused", "Refused", "Warning", "admin"), (entry.Action, entry.Outcome, entry.Level, entry.ActorUserId));
    }

    [Fact]
    public async Task An_oversize_upload_from_a_user_who_is_not_an_administrator_is_still_refused_with_403()
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(new byte[RequestLimit + 1]), "picture", "crest.png" }
        };

        var response = await PostAsync("member", "Add", form);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_audit.Entries);
    }

    private static MultipartFormDataContent SmallForm(string handler)
    {
        var form = new MultipartFormDataContent { { new StringContent("Crest"), "name" } };
        if (handler == "Add")
        {
            form.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "picture", "crest.png");
        }
        else
        {
            form.Add(new StringContent("1"), "id");
            form.Add(new StringContent("true"), "up");
        }
        return form;
    }

    /// <summary>
    /// Posts the form with a valid antiforgery token and cookie for this user, made by the host's
    /// own antiforgery service, so a refusal is about who the user is and not a missing token.
    /// </summary>
    private Task<HttpResponseMessage> PostAsync(string user, string handler, MultipartFormDataContent form, HttpClient? client = null)
    {
        var antiforgery = _factory.Services.GetRequiredService<IAntiforgery>();
        var options = _factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        var tokens = antiforgery.GetTokens(new DefaultHttpContext
        {
            RequestServices = _factory.Services,
            User = TestAuthHandler.Principal(user)
        });
        form.Add(new StringContent(tokens.RequestToken!), options.FormFieldName);

        var request = new HttpRequestMessage(HttpMethod.Post, PagePath + "?handler=" + handler) { Content = form };
        request.Headers.Add("Cookie", options.Cookie.Name + "=" + tokens.CookieToken);
        return (client ?? CreateClient(user)).SendAsync(request);
    }

    private HttpClient CreateClient(string user)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(UserHeader, user);
        return client;
    }

    private sealed class RecordingAudit : IAdminAuditService
    {
        public List<AdminAuditEntry> Entries { get; } = new();

        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            lock (Entries) Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "Test";

        public static ClaimsPrincipal Principal(string user)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user),
                new(ClaimTypes.Name, user)
            };
            if (user == "admin") claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers[UserHeader].ToString();
            return Task.FromResult(string.IsNullOrEmpty(user)
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(Principal(user), Scheme)));
        }
    }
}

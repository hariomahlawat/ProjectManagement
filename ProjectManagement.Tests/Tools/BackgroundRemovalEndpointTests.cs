using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.PhotoTools;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// The two Remove background endpoints over HTTP (PR 3 of
/// docs/ux/photo-tools-remove-background-fixes.md): signed-in only, the antiforgery check, the
/// answers for each refusal, no caching, and that a full queue or a server that is not set up is
/// answered before the photo is read. The model itself is replaced by a stand-in service.
/// </summary>
public sealed class BackgroundRemovalEndpointTests
{
    private static readonly byte[] SomePhoto = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };

    [Fact]
    public async Task Status_NeedsSignIn_AndIsNeverCached()
    {
        await using var host = await Host.StartAsync(signedIn: false);
        var anonymous = await host.Client.GetAsync(PhotoToolsApi.StatusPath);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await using var signedIn = await Host.StartAsync();
        var response = await signedIn.Client.GetAsync(PhotoToolsApi.StatusPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Status_ReportsTheLimitsInForce_NotOutOfRangeSettings()
    {
        await using var host = await Host.StartAsync(options: o =>
        {
            o.MaximumSide = 100_000;
            o.MaximumUploadBytes = -5;
        });

        var body = await host.Client.GetStringAsync(PhotoToolsApi.StatusPath);

        Assert.Contains("\"maximumSide\":4096", body, StringComparison.Ordinal);
        Assert.Contains("\"maximumBytes\":1048576", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mask_RefusesAFailedAntiforgeryCheck()
    {
        await using var host = await Host.StartAsync(antiforgeryPasses: false);
        var response = await host.Client.PostAsync(PhotoToolsApi.MaskPath, Photo("image/jpeg"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.Service.PhotosReceived);
    }

    [Fact]
    public async Task Mask_RefusesOtherTypes_AndPhotosOverTheLimit()
    {
        await using var host = await Host.StartAsync(options: o => o.MaximumUploadBytes = 1024 * 1024);

        var gif = await host.Client.PostAsync(PhotoToolsApi.MaskPath, Photo("image/gif"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, gif.StatusCode);

        var large = new ByteArrayContent(new byte[1024 * 1024 + 1]);
        large.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var tooLarge = await host.Client.PostAsync(PhotoToolsApi.MaskPath, large);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal(0, host.Service.PhotosReceived);
    }

    [Fact]
    public async Task Mask_AnswersBusy_BeforeReadingThePhoto()
    {
        await using var host = await Host.StartAsync();
        host.Service.Room = false;

        var response = await host.Client.PostAsync(PhotoToolsApi.MaskPath, Photo("image/jpeg"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal(0, host.Service.PhotosReceived);
    }

    [Fact]
    public async Task Mask_AnswersNotSetUp_BeforeReadingThePhoto()
    {
        await using var host = await Host.StartAsync();
        host.Service.Ready = false;

        var response = await host.Client.PostAsync(PhotoToolsApi.MaskPath, Photo("image/jpeg"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("not set up", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, host.Service.PhotosReceived);
    }

    [Fact]
    public async Task Mask_ReadsThePhotoWhole_AndAnswersWithAnUncachedMask()
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.PostAsync(PhotoToolsApi.MaskPath, Photo("image/jpeg"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(SomePhoto, host.Service.LastPhoto);
    }

    [Theory]
    [InlineData(5L, 1024L * 1024)]
    [InlineData(20L * 1024 * 1024, 20L * 1024 * 1024)]
    [InlineData(500L * 1024 * 1024, 64L * 1024 * 1024)]
    public void EffectiveUploadBytes_StaysBetween1And64MB(long setting, long expected)
    {
        Assert.Equal(expected, new BackgroundRemovalOptions { MaximumUploadBytes = setting }.EffectiveUploadBytes);
    }

    [Theory]
    [InlineData(10, 256)]
    [InlineData(2048, 2048)]
    [InlineData(9000, 4096)]
    public void EffectiveSide_StaysBetween256And4096(int setting, int expected)
    {
        Assert.Equal(expected, new BackgroundRemovalOptions { MaximumSide = setting }.EffectiveSide);
    }

    private static ByteArrayContent Photo(string type)
    {
        var content = new ByteArrayContent(SomePhoto);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return content;
    }

    /// <summary>A small web host with only the Photo Tools endpoints, a stand-in sign-in and antiforgery check.</summary>
    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private Host(WebApplication app, FakeService service)
        {
            _app = app;
            Service = service;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public FakeService Service { get; }

        public static async Task<Host> StartAsync(
            bool signedIn = true,
            bool antiforgeryPasses = true,
            Action<BackgroundRemovalOptions>? options = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.Configure<BackgroundRemovalOptions>(o =>
            {
                o.Enabled = true;
                options?.Invoke(o);
            });
            var service = new FakeService();
            builder.Services.AddSingleton<IBackgroundRemovalService>(service);
            builder.Services.AddSingleton<IAntiforgery>(new FakeAntiforgery(antiforgeryPasses));
            builder.Services
                .AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestSignIn>("Test", o => o.ClaimsIssuer = signedIn ? "yes" : "no");
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapPhotoToolsApi();
            await app.StartAsync();
            return new Host(app, service);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    private sealed class TestSignIn(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Options.ClaimsIssuer != "yes")
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "tester") }, "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private sealed class FakeAntiforgery(bool passes) : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => new("r", "c", "f", "h");

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => new("r", "c", "f", "h");

        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(passes);

        public void SetCookieTokenAndHeader(HttpContext httpContext)
        {
        }

        public Task ValidateRequestAsync(HttpContext httpContext)
            => passes ? Task.CompletedTask : throw new AntiforgeryValidationException("Not valid.");
    }

    public sealed class FakeService : IBackgroundRemovalService
    {
        public bool Ready { get; set; } = true;

        public bool Room { get; set; } = true;

        public int PhotosReceived { get; private set; }

        public byte[]? LastPhoto { get; private set; }

        public BackgroundModelReadiness Readiness() =>
            new(Ready ? BackgroundModelState.Ready : BackgroundModelState.Disabled, Ready ? "model.onnx" : null);

        public bool HasRoom() => Room;

        public Task<BackgroundRemovalResult> MaskAsync(byte[] photoBytes, CancellationToken cancellationToken)
        {
            PhotosReceived += 1;
            LastPhoto = photoBytes;
            return Task.FromResult(new BackgroundRemovalResult(BackgroundRemovalOutcome.Done, new byte[] { 0x89, 0x50, 0x4E, 0x47 }, 2, 2));
        }
    }
}

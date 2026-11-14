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
        Assert.Equal(0, host.Service.PlacesHeld);
    }

    [Fact]
    public async Task Mask_GivesItsPlaceBack_WhenThePhotoNeverArrivesInFull()
    {
        await using var host = await Host.StartAsync();
        var content = new StreamContent(new CutShortStream(SomePhoto, declared: 100));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Headers.ContentLength = 100;

        try
        {
            await host.Client.PostAsync(PhotoToolsApi.MaskPath, content);
        }
        catch (HttpRequestException)
        {
            // The test server may report the cut-short body to the client as well.
        }

        Assert.Equal(0, host.Service.PhotosReceived);
        Assert.Equal(0, host.Service.PlacesHeld);
    }

    [Fact]
    public async Task Mask_StopsAnUploadThatTakesTooLong_AndGivesItsPlaceBack()
    {
        await using var host = await Host.StartAsync(options: o => o.TimeoutSeconds = 5);
        var content = new StreamContent(new TricklingStream());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Headers.ContentLength = 1000;

        HttpResponseMessage? response = null;
        try
        {
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            response = await host.Client.PostAsync(PhotoToolsApi.MaskPath, content, wait.Token);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            // The test server may also report the abandoned body to the client.
        }

        if (response is not null)
        {
            Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
        }

        Assert.Equal(0, host.Service.PhotosReceived);
        Assert.Equal(0, host.Service.PlacesHeld);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(1, 5)]
    [InlineData(10_000, 600)]
    public void UploadDeadline_IsTheTimeLimitForOnePhoto(int timeoutSeconds, int expectedSeconds)
    {
        var deadline = PhotoToolsApi.UploadDeadline(new BackgroundRemovalOptions { TimeoutSeconds = timeoutSeconds });
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), deadline);
    }

    /// <summary>A body that sends one byte and then nothing more, as an upload kept open on purpose.</summary>
    private sealed class TricklingStream : Stream
    {
        private bool _sent;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                buffer.Span[0] = 0xFF;
                return 1;
            }

            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A body that ends before its declared length, as an upload that stalls and drops.</summary>
    private sealed class CutShortStream(byte[] data, int declared) : MemoryStream(data)
    {
        public override long Length => declared;
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

        public int PlacesHeld { get; private set; }

        public IDisposable? TryTakePlace()
        {
            if (!Room)
            {
                return null;
            }

            PlacesHeld += 1;
            return new Given(this);
        }

        public Task<BackgroundRemovalResult> MaskAsync(byte[] photoBytes, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The endpoint must take its place before reading the photo.");

        public Task<BackgroundRemovalResult> MaskAsync(byte[] photoBytes, IDisposable place, CancellationToken cancellationToken)
        {
            PhotosReceived += 1;
            LastPhoto = photoBytes;
            return Task.FromResult(new BackgroundRemovalResult(BackgroundRemovalOutcome.Done, new byte[] { 0x89, 0x50, 0x4E, 0x47 }, 2, 2));
        }

        private sealed class Given(FakeService owner) : IDisposable
        {
            public void Dispose() => owner.PlacesHeld -= 1;
        }
    }
}

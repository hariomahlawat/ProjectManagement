using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Features.PhotoTools;
using SkiaSharp;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// "Place the face for me" in Passport photo (PR 3 of docs/ux/photo-tools-passport-done-for-you.md):
/// the service around the Photos library's detector, and the endpoint over HTTP. The detector is a
/// stand-in here; it never runs the recogniser and keeps nothing.
/// </summary>
public sealed class PassportFaceTests
{
    // ----- The service -----

    [Fact]
    public async Task Locate_ReturnsTheLargestFaceAndItsFivePoints()
    {
        var engine = new FakeEngine { Face = SomeFace() };
        var service = ServiceWith(engine);

        var result = await service.LocateAsync(Jpeg(64, 48), CancellationToken.None);

        Assert.Equal(PassportFaceOutcome.Found, result.Outcome);
        Assert.Equal((64, 48), (result.ImageWidth, result.ImageHeight));
        Assert.Equal(5, result.Face!.Points.Count);
        Assert.Equal(1, engine.Calls);
    }

    [Fact]
    public async Task Locate_SaysNoFace_WhenTheDetectorFindsNone()
    {
        var service = ServiceWith(new FakeEngine { Face = null });
        Assert.Equal(PassportFaceOutcome.NoFace, (await service.LocateAsync(Jpeg(32, 32), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Locate_IsOff_WithoutTheDetector_OrWhenItIsNotReady()
    {
        Assert.Equal(PassportFaceOutcome.Unavailable, (await ServiceWith(null).LocateAsync(Jpeg(32, 32), CancellationToken.None)).Outcome);
        Assert.False(await ServiceWith(null).IsAvailableAsync(CancellationToken.None));

        var notReady = new FakeEngine { Ready = false };
        Assert.Equal(PassportFaceOutcome.Unavailable, (await ServiceWith(notReady).LocateAsync(Jpeg(32, 32), CancellationToken.None)).Outcome);
        Assert.Equal(0, notReady.Calls);
    }

    [Fact]
    public async Task Locate_RefusesWhatIsNotAJpgPngOrWebp_OrTooLarge_BeforeDecoding()
    {
        var engine = new FakeEngine { Face = SomeFace() };
        var service = ServiceWith(engine, o => o.MaximumSide = 256);

        Assert.Equal(PassportFaceOutcome.NotAPhoto, (await service.LocateAsync(new byte[] { 1, 2, 3 }, CancellationToken.None)).Outcome);
        Assert.Equal(PassportFaceOutcome.TooLarge, (await service.LocateAsync(Jpeg(300, 20), CancellationToken.None)).Outcome);
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task Locate_AnswersBusy_WhenTwoAreAlreadyRunning()
    {
        var engine = new FakeEngine { Face = SomeFace(), Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = ServiceWith(engine);

        var first = service.LocateAsync(Jpeg(16, 16), CancellationToken.None);
        var second = service.LocateAsync(Jpeg(16, 16), CancellationToken.None);
        var third = await service.LocateAsync(Jpeg(16, 16), CancellationToken.None);
        Assert.Equal(PassportFaceOutcome.Busy, third.Outcome);

        engine.Hold.SetResult();
        Assert.Equal(PassportFaceOutcome.Found, (await first).Outcome);
        Assert.Equal(PassportFaceOutcome.Found, (await second).Outcome);
    }

    // ----- The endpoint -----

    [Fact]
    public async Task Face_NeedsSignIn_AndTheAntiforgeryToken()
    {
        await using (var anonymous = await Host.StartAsync(new FakeEngine { Face = SomeFace() }, signedIn: false))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Client.PostAsync(PhotoToolsApi.FacePath, Photo())).StatusCode);
        }

        await using var host = await Host.StartAsync(new FakeEngine { Face = SomeFace() }, antiforgeryPasses: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsync(PhotoToolsApi.FacePath, Photo())).StatusCode);
        Assert.Equal(0, host.Engine!.Calls);
    }

    [Fact]
    public async Task Face_AnswersTheBoxAndFivePoints_NeverCached()
    {
        await using var host = await Host.StartAsync(new FakeEngine { Face = SomeFace() });

        var response = await host.Client.PostAsync(PhotoToolsApi.FacePath, Photo());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("found").GetBoolean());
        Assert.Equal(5, json.RootElement.GetProperty("points").GetArrayLength());
        Assert.Equal(20, json.RootElement.GetProperty("face").GetProperty("width").GetDouble());
    }

    [Fact]
    public async Task Face_SaysNotSetUp_WithoutTheDetector_BeforeReadingThePhoto()
    {
        await using var host = await Host.StartAsync(new FakeEngine { Ready = false });

        var response = await host.Client.PostAsync(PhotoToolsApi.FacePath, Photo());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("not set up", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_SaysWhetherTheFaceCanBePlaced()
    {
        await using (var on = await Host.StartAsync(new FakeEngine()))
        {
            Assert.Contains("\"faceAvailable\":true", await on.Client.GetStringAsync(PhotoToolsApi.StatusPath), StringComparison.Ordinal);
        }

        await using var off = await Host.StartAsync(null);
        Assert.Contains("\"faceAvailable\":false", await off.Client.GetStringAsync(PhotoToolsApi.StatusPath), StringComparison.Ordinal);
    }

    // ----- Helpers -----

    private static LocatedFace SomeFace() => new(
        10, 8, 20, 24, 0.95,
        new[] { new FacePoint(15, 15), new FacePoint(25, 15), new FacePoint(20, 20), new FacePoint(16, 26), new FacePoint(24, 26) });

    private static PassportFaceService ServiceWith(FakeEngine? engine, Action<BackgroundRemovalOptions>? configure = null)
    {
        var services = new ServiceCollection();
        if (engine is not null)
        {
            services.AddSingleton<IFaceLocationEngine>(engine);
        }

        var options = new BackgroundRemovalOptions();
        configure?.Invoke(options);
        return new PassportFaceService(services.BuildServiceProvider(), new FixedMonitor(options), NullLogger<PassportFaceService>.Instance);
    }

    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Gray);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    private static ByteArrayContent Photo()
    {
        var content = new ByteArrayContent(Jpeg(64, 48));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return content;
    }

    private sealed class FakeEngine : IFaceLocationEngine
    {
        public bool Ready { get; init; } = true;
        public LocatedFace? Face { get; init; }
        public TaskCompletionSource? Hold { get; init; }
        public int Calls;

        public Task<FaceDetectorReadiness> DetectorReadinessAsync(CancellationToken cancellationToken)
            => Task.FromResult(new FaceDetectorReadiness(true, Ready, Ready ? FaceReadinessState.Ready : FaceReadinessState.ModelsMissing,
                Ready ? "Ready" : "Missing", Ready ? "detector.onnx" : null, DateTimeOffset.UtcNow, Array.Empty<FaceReadinessCheck>()));

        public async Task<FaceLocation> LocateLargestFaceAsync(byte[] imageBytes, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (Hold is not null)
            {
                await Hold.Task;
            }

            using var bitmap = SKBitmap.Decode(imageBytes);
            return new FaceLocation(bitmap.Width, bitmap.Height, Face);
        }
    }

    private sealed class FixedMonitor(BackgroundRemovalOptions value) : IOptionsMonitor<BackgroundRemovalOptions>
    {
        public BackgroundRemovalOptions CurrentValue => value;
        public BackgroundRemovalOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<BackgroundRemovalOptions, string?> listener) => null;
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private Host(WebApplication app, FakeEngine? engine)
        {
            _app = app;
            Engine = engine;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public FakeEngine? Engine { get; }

        public static async Task<Host> StartAsync(FakeEngine? engine, bool signedIn = true, bool antiforgeryPasses = true)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.Configure<BackgroundRemovalOptions>(_ => { });
            builder.Services.AddSingleton<IBackgroundRemovalService>(new OffRemoval());
            builder.Services.AddSingleton<IPassportFaceService, PassportFaceService>();
            if (engine is not null)
            {
                builder.Services.AddSingleton<IFaceLocationEngine>(engine);
            }

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
            return new Host(app, engine);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    /// <summary>Remove background switched off: only the face part is under test here.</summary>
    private sealed class OffRemoval : IBackgroundRemovalService
    {
        public BackgroundModelReadiness Readiness() => new(BackgroundModelState.Disabled, null);

        public Task<BackgroundRemovalResult> MaskAsync(byte[] photoBytes, CancellationToken cancellationToken)
            => Task.FromResult(new BackgroundRemovalResult(BackgroundRemovalOutcome.Unavailable));

        public Task<BackgroundRemovalResult> MaskAsync(byte[] photoBytes, IDisposable place, CancellationToken cancellationToken)
            => Task.FromResult(new BackgroundRemovalResult(BackgroundRemovalOutcome.Unavailable));

        public IDisposable? TryTakePlace() => null;
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
}

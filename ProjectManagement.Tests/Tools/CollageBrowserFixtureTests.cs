using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Features.Collage;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services.Imaging;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using SkiaSharp;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// The Collage Studio browser tests (tests/browser) serve the page without PRISM. So that they
/// fail where production would, the test page sends PRISM's own production security headers and
/// answers like the collage API (docs/ux/collage-review-2-fixes.md, PR 12). Both come from here:
/// these tests read them from the real host and API and check the files the test page uses are
/// the same. The headers are those of the page itself, signed in, under Production settings
/// (docs/ux/collage-review-3-fixes.md, PR 14). The samples include one crest, whose stored
/// picture is written too. When a header or an answer changes on purpose, run the tests with
/// PRISM_UPDATE_BROWSER_FIXTURES=1 to write the files again, and commit them.
/// </summary>
public sealed class CollageBrowserFixtureTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string HeadersFile = "production-headers.json";
    private const string SamplesFile = "collage-api-samples.json";
    private const string CrestFile = "crest.png";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly WebApplicationFactory<Program> _factory;

    public CollageBrowserFixtureTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task The_test_page_sends_the_production_security_headers()
    {
        using var production = _factory.WithWebHostBuilder(builder => builder
            .UseCollageTestHost("collage-fixture-headers")
            .UseEnvironment("Production")
            .ConfigureTestServices(services => services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = SignedIn.Scheme;
                    options.DefaultAuthenticateScheme = SignedIn.Scheme;
                    options.DefaultChallengeScheme = SignedIn.Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, SignedIn>(SignedIn.Scheme, _ => { })));
        var client = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Tools/Collage");

        // The page itself, as a signed-in user sees it, not its sign-in redirect.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Collage Studio", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "Content-Security-Policy", "Cross-Origin-Opener-Policy", "Cross-Origin-Resource-Policy", "X-Content-Type-Options" })
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                headers[name] = string.Join(", ", values);
            }
        }

        Assert.True(headers.ContainsKey("Content-Security-Policy"), "PRISM sends a Content-Security-Policy");
        Assert.DoesNotContain("'unsafe-inline'", headers["Content-Security-Policy"].Split("style-src-attr")[0], StringComparison.Ordinal);
        AssertFixture(HeadersFile, JsonSerializer.Serialize(headers, Web));
    }

    [Fact]
    public async Task The_test_page_answers_like_the_collage_api()
    {
        var links = _factory.WithWebHostBuilder(builder => builder.UseCollageTestHost("collage-fixture-api"));
        var linkGenerator = links.Services.GetRequiredService<LinkGenerator>();

        var samples = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["libraryOff"] = await LibraryAsync(enabled: false, linkGenerator, links.Services),
            ["libraryOn"] = await LibraryAsync(enabled: true, linkGenerator, links.Services),
        };
        var (stickers, picture, crest) = await StickersAsync();
        samples["stickers"] = stickers;
        samples["stickerPicture"] = picture;

        AssertFixture(SamplesFile, JsonSerializer.Serialize(samples, Web));
        AssertPictureFixture(CrestFile, crest);
    }

    private static async Task<object?> LibraryAsync(bool enabled, LinkGenerator links, IServiceProvider services)
    {
        var options = Options.Create(new MediaLibraryOptions());
        options.Value.Enabled = enabled;
        options.Value.Catalogue.Enabled = enabled;
        var context = new DefaultHttpContext { RequestServices = services };
        var library = new SampleLibrary();
        var result = await CollageApi.LibraryPhotosAsync(null, null, null, 1, context, library, options, links, CancellationToken.None);
        return Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
    }

    /// <summary>
    /// The sticker list with one crest uploaded through the real service, the headers its picture
    /// is served with, and the stored picture itself (PRISM's own PNG, re-encoded on upload).
    /// </summary>
    private static async Task<(object? List, object Picture, byte[] Png)> StickersAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "collage-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
            var roots = new TestUploadRootProvider(root);
            var service = new CollageStickerService(db, roots, new UploadPathResolver(roots), new ProjectManagement.Tests.Activities.NoOpAdminAuditService(),
                FakeClock.AtUtc(new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero)));
            var added = await service.AddAsync(new MemoryStream(DrawCrest()), "signals-crest.png", "Signals crest", "corps badge", "admin");
            Assert.True(added.Succeeded, added.Message);

            var list = await CollageApi.StickersAsync(new DefaultHttpContext(), service, CancellationToken.None);
            var context = new DefaultHttpContext();
            var served = Assert.IsType<PhysicalFileHttpResult>(await CollageApi.PictureAsync(added.Sticker!.Id, context, service, CancellationToken.None));
            var picture = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["Cache-Control"] = context.Response.Headers.CacheControl.ToString(),
                ["Content-Type"] = served.ContentType,
                ["ETag"] = context.Response.Headers.ETag.ToString()
            };
            return (Assert.IsAssignableFrom<IValueHttpResult>(list).Value, picture, await File.ReadAllBytesAsync(served.FileName));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// A crest drawn without smoothing, on a clear background, so its pixels are the same on every
    /// computer: a navy shield with a gold band.
    /// </summary>
    private static byte[] DrawCrest()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(240, 280, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
        using (var shield = new SKPath())
        using (var navy = new SKPaint { Color = new SKColor(0x1F, 0x3A, 0x68), IsAntialias = false })
        using (var gold = new SKPaint { Color = new SKColor(0xD4, 0xA0, 0x17), IsAntialias = false })
        {
            shield.MoveTo(20, 20);
            shield.LineTo(220, 20);
            shield.LineTo(220, 160);
            shield.LineTo(120, 260);
            shield.LineTo(20, 160);
            shield.Close();
            canvas.DrawPath(shield, navy);
            canvas.DrawRect(new SKRect(20, 100, 220, 130), gold);
        }
        return PrismImageCodec.EncodePng(bitmap);
    }

    /// <summary>
    /// Compares a committed picture with PRISM's by its pixels (a PNG encoder may write the same
    /// pixels as different bytes); writes it instead when asked.
    /// </summary>
    private static void AssertPictureFixture(string name, byte[] png)
    {
        var path = Path.Combine(FindProjectRoot(), "tests", "browser", "fixtures", name);
        if (Environment.GetEnvironmentVariable("PRISM_UPDATE_BROWSER_FIXTURES") == "1")
        {
            File.WriteAllBytes(path, png);
            return;
        }

        Assert.True(File.Exists(path), $"{name} is missing: run the tests with PRISM_UPDATE_BROWSER_FIXTURES=1 and commit it.");
        using var committed = PrismImageCodec.DecodeAsStored(File.ReadAllBytes(path));
        using var current = PrismImageCodec.DecodeAsStored(png);
        Assert.True(
            (committed.Width, committed.Height) == (current.Width, current.Height) && committed.Bytes.AsSpan().SequenceEqual(current.Bytes),
            $"tests/browser/fixtures/{name} no longer matches the picture PRISM stores. If the change is meant, run the tests with "
            + "PRISM_UPDATE_BROWSER_FIXTURES=1 and commit the file.");
    }

    /// <summary>
    /// Compares the committed fixture with what PRISM gives now; writes it instead when asked.
    /// </summary>
    private static void AssertFixture(string name, string json)
    {
        var path = Path.Combine(FindProjectRoot(), "tests", "browser", "fixtures", name);
        var expected = json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        if (Environment.GetEnvironmentVariable("PRISM_UPDATE_BROWSER_FIXTURES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected);
            return;
        }

        Assert.True(File.Exists(path), $"{name} is missing: run the tests with PRISM_UPDATE_BROWSER_FIXTURES=1 and commit it.");
        var committed = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(committed == expected,
            $"tests/browser/fixtures/{name} no longer matches PRISM. If the change is meant, run the tests with "
            + "PRISM_UPDATE_BROWSER_FIXTURES=1 and commit the file; the browser tests then use the new value.");
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Project root could not be located.");
    }

    /// <summary>Every request is from one signed-in user who is not an administrator.</summary>
    private sealed class SignedIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "FixtureUser";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "fixture-user"),
                new Claim(ClaimTypes.Name, "fixture-user")
            }, Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }

    /// <summary>Three activity photos, as the library lists them; their pictures use /Photos/Media.</summary>
    private sealed class SampleLibrary : IMediaLibraryQueryService
    {
        public Task<MediaLibraryQueryResult> SearchAsync(MediaLibraryQuery query, CancellationToken cancellationToken)
        {
            var items = Enumerable.Range(1, 3).Select(id => new MediaLibraryQueryItem(
                id, new Guid($"00000000-0000-0000-0000-00000000000{id}"), MediaAssetOrigin.ActivityPhoto, MediaAssetKind.Photo,
                MediaClassification.Photograph, $"activity-photo:{id}", "5", "activity:5", "activity:5", "A project",
                string.Empty, "Activities", $"Library {id}", null, null, new Guid($"00000000-0000-0000-0000-00000000010{id}"),
                $"library-{id}.jpg", 1000, DateTimeOffset.UnixEpoch, 1200, 900, null, false, id, 1, "1",
                Array.Empty<MediaLibraryPersonSummary>(), 0, Array.Empty<MediaLibraryAlbumSummary>())).ToArray();
            return Task.FromResult(new MediaLibraryQueryResult(items,
                new[] { new MediaLibraryProjectOption(12, "A project") },
                Array.Empty<MediaLibraryPersonOption>(),
                new[] { 2026 },
                new MediaLibraryStatistics(items.Length, items.Length, 0, 1),
                1, CollageApi.LibraryPageSize, false, true, true, true, true,
                Array.Empty<MediaLibraryQueryWarning>(), null));
        }
    }
}

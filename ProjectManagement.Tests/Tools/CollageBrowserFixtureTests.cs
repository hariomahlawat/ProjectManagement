using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Features.Collage;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// The Collage Studio browser tests (tests/browser) serve the page without PRISM. So that they
/// fail where production would, the test page sends PRISM's own production security headers and
/// answers like the collage API (docs/ux/collage-review-2-fixes.md, PR 12). Both come from here:
/// these tests read them from the real host and API and check the files the test page uses are
/// the same. When a header or an answer changes on purpose, run the tests with
/// PRISM_UPDATE_BROWSER_FIXTURES=1 to write the files again, and commit them.
/// </summary>
public sealed class CollageBrowserFixtureTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string HeadersFile = "production-headers.json";
    private const string SamplesFile = "collage-api-samples.json";

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
            .UseEnvironment("Production"));
        var client = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Tools/Collage");

        // The page itself needs sign-in; the headers are the same on its sign-in redirect.
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
            ["stickers"] = await StickersAsync()
        };

        AssertFixture(SamplesFile, JsonSerializer.Serialize(samples, Web));
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

    private static async Task<object?> StickersAsync()
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
            var result = await CollageApi.StickersAsync(new DefaultHttpContext(), service, CancellationToken.None);
            return Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
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

using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Services.Ffc.Presentation;
using SkiaSharp;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcPresentationMapRendererTests
{
    [Fact]
    public void Render_CreatesHighResolutionPngForActiveCountries()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", "PresentationRoot");
        var renderer = new FfcPresentationMapRenderer(new TestEnvironment(root));
        var countries = new[]
        {
            Country(1, "Ethiopia", "ETH", 13),
            Country(2, "Myanmar", "MMR", 10),
            Country(3, "France", "FRA", 1)
        };

        var content = renderer.Render(countries, width: 1200, height: 760);

        Assert.True(content.Length > 20_000);
        using var bitmap = SKBitmap.Decode(content);
        Assert.NotNull(bitmap);
        Assert.Equal(1200, bitmap!.Width);
        Assert.Equal(760, bitmap.Height);
    }

    private static FfcPresentationCountry Country(
        long id,
        string name,
        string iso,
        int units)
        => new(
            id,
            name,
            iso,
            1,
            1,
            0,
            0,
            units,
            new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero),
            Array.Empty<FfcPresentationRecord>());

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = Path.Combine(contentRootPath, "wwwroot");
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; } = "ProjectManagement.Tests";
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}

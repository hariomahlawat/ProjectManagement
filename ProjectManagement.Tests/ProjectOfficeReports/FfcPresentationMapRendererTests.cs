using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
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


    [Fact]
    public void RenderFocused_CreatesTighterBriefingMapAtRequestedSize()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", "PresentationRoot");
        IFfcPresentationMapRenderer renderer = new FfcPresentationMapRenderer(new TestEnvironment(root));
        var countries = new[]
        {
            Country(1, "Ethiopia", "ETH", 13),
            Country(2, "Myanmar", "MMR", 10),
            Country(3, "France", "FRA", 1)
        };

        var content = renderer.RenderFocused(countries, width: 1700, height: 1020);

        Assert.True(content.Length > 20_000);
        using var bitmap = SKBitmap.Decode(content);
        Assert.NotNull(bitmap);
        Assert.Equal(1700, bitmap!.Width);
        Assert.Equal(1020, bitmap.Height);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RenderBriefingBand_CreatesTheSlideBandInEitherTheme(bool dark)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", "PresentationRoot");
        IFfcPresentationMapRenderer renderer = new FfcPresentationMapRenderer(new TestEnvironment(root));
        var countries = new[]
        {
            Country(1, "Ethiopia", "ETH", 13),
            Country(2, "Tanzania, United Republic of", "TZA", 1),
            Country(3, "Myanmar", "MMR", 0)
        };

        var content = renderer.RenderBriefingBand(countries, width: 2426, height: 640, dark);

        using var bitmap = SKBitmap.Decode(content);
        Assert.NotNull(bitmap);
        Assert.Equal(2426, bitmap!.Width);
        Assert.Equal(640, bitmap.Height);
    }

    [Fact]
    public void BandShade_GivesACountryWithNoUnitsTheLightestShade()
    {
        // Codex review on #3410: zero fell through every band to the strongest shade.
        var shade = typeof(FfcPresentationMapRenderer).GetMethod("BandShade", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var name in new[] { "DarkBand", "LightBand" })
        {
            var palette = typeof(FfcPresentationMapRenderer).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var ramp = (SKColor[])palette.GetType().GetProperty("Ramp")!.GetValue(palette)!;
            Assert.Equal(ramp[0], (SKColor)shade.Invoke(null, new[] { palette, 0, 13 })!);
            Assert.Equal(ramp[0], (SKColor)shade.Invoke(null, new[] { palette, 1, 13 })!);
            Assert.Equal(ramp[^1], (SKColor)shade.Invoke(null, new[] { palette, 13, 13 })!);
        }
    }

    [Theory]
    [InlineData("Tanzania, United Republic of", "Tanzania")]
    [InlineData("  Iran, Islamic Republic of ", "Iran")]
    [InlineData("Sri Lanka", "Sri Lanka")]
    [InlineData("Korea, Republic of", "South Korea")]
    [InlineData("Korea, Democratic People's Republic of", "North Korea")]
    [InlineData("Congo, Democratic Republic of the", "DR Congo")]
    [InlineData("Congo", "Congo")]
    public void ShortCountryName_ShowsTheEverydayName(string name, string expected)
        => Assert.Equal(expected, FfcPresentationMapRenderer.ShortCountryName(name));

    [Fact]
    public void ShortCountryNames_NeverGivesTwoCountriesOneName()
    {
        // Codex review on #3410: the Congos and the Koreas stay apart; any other clash keeps the
        // full names.
        Assert.Equal(
            new[] { "Congo", "DR Congo", "South Korea", "North Korea" },
            FfcPresentationMapRenderer.ShortCountryNames(new[]
            {
                "Congo", "Congo, Democratic Republic of the", "Korea, Republic of", "Korea, Democratic People's Republic of"
            }));
        Assert.Equal(
            new[] { "Ruritania, Kingdom of", "Ruritania, Republic of", "Ethiopia" },
            FfcPresentationMapRenderer.ShortCountryNames(new[] { "Ruritania, Kingdom of", "Ruritania, Republic of", "Ethiopia" }));
    }

    [Fact]
    public void BuildLegendRanges_CreatesFourBalancedBandsForCurrentPortfolioMaximum()
    {
        var method = typeof(FfcPresentationMapRenderer).GetMethod(
            "BuildLegendRanges",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = Assert.IsAssignableFrom<IEnumerable>(method!.Invoke(null, new object[] { 13 }));
        var labels = result
            .Cast<object>()
            .Select(item => item.GetType().GetProperty("Label")?.GetValue(item)?.ToString())
            .ToArray();

        Assert.Equal(new[] { "1–3", "4–6", "7–9", "10–13" }, labels);
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

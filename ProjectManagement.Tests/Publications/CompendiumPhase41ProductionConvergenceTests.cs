using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase41ProductionConvergenceTests
{
    [Fact]
    public void PhysicalPaginationContract_ReservesAtLeastOneBodyLine()
    {
        Assert.True(CompendiumLayoutMetrics.PhysicalPaginationReservePoints >= 10f);
        Assert.Equal("physical-a4-v45", CompendiumBuildIdentity.PdfContract);
    }

    [Fact]
    public void FontContract_RequiresEveryDmSansFaceFromOneOfflineDirectory()
    {
        using var root = new TemporaryDirectory();
        var fontDirectory = Path.Combine(root.Path, "wwwroot", "fonts", "publications", "dm-sans");
        Directory.CreateDirectory(fontDirectory);

        foreach (var file in PublicationFontContract.RequiredDmSansFiles)
        {
            File.WriteAllBytes(Path.Combine(fontDirectory, file), new byte[] { 1 });
        }

        var resolution = PublicationFontContract.InspectDmSans(root.Path, null);

        Assert.True(resolution.IsAvailable);
        Assert.Equal(Path.GetFullPath(fontDirectory), resolution.DirectoryPath);
        Assert.Empty(resolution.MissingFiles);
    }

    [Fact]
    public void FontContract_DoesNotAcceptAPartialDmSansDeployment()
    {
        using var root = new TemporaryDirectory();
        var fontDirectory = Path.Combine(root.Path, "wwwroot", "fonts", "publications", "dm-sans");
        Directory.CreateDirectory(fontDirectory);
        File.WriteAllBytes(Path.Combine(fontDirectory, "DMSans-Regular.ttf"), new byte[] { 1 });

        var resolution = PublicationFontContract.InspectDmSans(root.Path, null);

        Assert.False(resolution.IsAvailable);
        var exception = PublicationFontContract.CreateMissingFontException(resolution);
        Assert.Contains("complete bundled DM Sans", exception.Message);
        Assert.Contains(PublicationFontContract.ExternalFontRootEnvironmentVariable, exception.Message);
    }

    [Theory]
    [InlineData(CompendiumPdfGenerationStage.PublicationRead)]
    [InlineData(CompendiumPdfGenerationStage.CoverResolution)]
    [InlineData(CompendiumPdfGenerationStage.FontInitialization)]
    [InlineData(CompendiumPdfGenerationStage.PagePlanning)]
    [InlineData(CompendiumPdfGenerationStage.PdfLayout)]
    [InlineData(CompendiumPdfGenerationStage.PdfDrawing)]
    [InlineData(CompendiumPdfGenerationStage.PdfVerification)]
    public void TypedFailure_PreservesItsGenerationStage(CompendiumPdfGenerationStage stage)
    {
        var exception = new CompendiumPdfGenerationException(stage, "safe message");

        Assert.Equal(stage, exception.Stage);
        Assert.Equal("safe message", exception.Message);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "prism-compendium-phase41-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}

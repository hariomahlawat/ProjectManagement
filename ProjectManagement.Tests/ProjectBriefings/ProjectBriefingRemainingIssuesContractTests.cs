using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingRemainingIssuesContractTests
{
    [Fact]
    public void UpdateSheet_UsesReadablePaginationCropFillAndContinuationAwareEstimate()
    {
        var composer = Read("ProjectBriefingSlideComposer.UpdateSheet.cs");
        var dataService = Read("ProjectBriefingDataService.cs");

        Assert.Contains("BRIEF OF THE PROJECT — CONTINUED", composer, StringComparison.Ordinal);
        Assert.Contains("allowAutoFit: false", composer, StringComparison.Ordinal);
        Assert.Contains("new UpdateSheetBriefTypography(12.0", composer, StringComparison.Ordinal);
        Assert.Contains("PrepareUpdateSheetPhoto", composer, StringComparison.Ordinal);
        Assert.Contains(".Crop(crop)", composer, StringComparison.Ordinal);
        Assert.Contains("EstimateProjectUpdateSheetSlides", dataService, StringComparison.Ordinal);
    }

    private static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", fileName);
        Assert.True(File.Exists(path), $"Project briefing contract file was not copied to test output: {path}");
        return File.ReadAllText(path);
    }
}

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
        var planner = Read("ProjectBriefingUpdateSheetPlanner.cs");

        Assert.Contains("BRIEF OF THE PROJECT — CONTINUED", composer, StringComparison.Ordinal);
        Assert.Contains("allowAutoFit: false", composer, StringComparison.Ordinal);
        Assert.Contains("new(12.0, 14.4, 4.0)", planner, StringComparison.Ordinal);
        Assert.Contains("BriefPages", composer, StringComparison.Ordinal);
        Assert.Contains("PrepareUpdateSheetPhoto", composer, StringComparison.Ordinal);
        Assert.Contains(".Crop(crop)", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingUpdateSheetPlanner.EstimateSlideCount", dataService, StringComparison.Ordinal);
    }

    private static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", fileName);
        Assert.True(File.Exists(path), $"Project briefing contract file was not copied to test output: {path}");
        return File.ReadAllText(path);
    }
}

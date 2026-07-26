using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class HistoricalStageDeliveryContractTests
{
    [Fact]
    public void TerminalProjects_RenderRemarksAsTheAccessibleInitialPanel()
    {
        var viewModel = ReadRepoFile("ViewModels", "ProjectPortfolioPresentationVm.cs");
        var workspace = ReadRepoFile("Pages", "Projects", "_ProjectLifecycleWorkspace.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "projects", "overview.js");

        Assert.Contains("public string DefaultPanel", viewModel, StringComparison.Ordinal);
        Assert.Contains("? TimelinePanelName", viewModel, StringComparison.Ordinal);
        Assert.Contains(": RemarksPanelName", viewModel, StringComparison.Ordinal);
        Assert.Contains("data-default-panel=\"@defaultPanel\"", workspace, StringComparison.Ordinal);
        Assert.Contains("var remarksInitiallyActive = !timelineInitiallyActive;", workspace, StringComparison.Ordinal);
        Assert.Contains("aria-hidden=\"@(remarksInitiallyActive ? \"false\" : \"true\")\"", workspace, StringComparison.Ordinal);
        Assert.Contains("const initial = override || getStored();", script, StringComparison.Ordinal);
        Assert.Contains("return defaultPanel;", script, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricalEntry_IsRestrictedAuditedAndUsesStandardProjectStages()
    {
        var handler = ReadRepoFile("Pages", "Projects", "Timeline", "Historical.cshtml.cs");
        var service = ReadRepoFile("Services", "Stages", "HistoricalStageRecordService.cs");
        var workspace = ReadRepoFile("Pages", "Projects", "_ProjectLifecycleWorkspace.cshtml");

        Assert.Contains("[Authorize(Roles = \"Admin,HoD\")]", handler, StringComparison.Ordinal);
        Assert.Contains("_db.ProjectStages", service, StringComparison.Ordinal);
        Assert.Contains("_db.StageChangeLogs", service, StringComparison.Ordinal);
        Assert.Contains("Projects.HistoricalStageHistoryUpdated", service, StringComparison.Ordinal);
        Assert.Contains("private const string StageLogAction = \"Backfill\";", service, StringComparison.Ordinal);
        Assert.Contains("RelationalTransactionScope.CreateAsync", service, StringComparison.Ordinal);
        Assert.Contains("ResolveTerminalDateUpperBound", service, StringComparison.Ordinal);
        Assert.Contains("project.IsDeleted", service, StringComparison.Ordinal);
        Assert.Contains("project.IsLegacy", service, StringComparison.Ordinal);
        Assert.Contains("ProjectLifecycleStatus.Completed", service, StringComparison.Ordinal);
        Assert.Contains("ProjectLifecycleStatus.Cancelled", service, StringComparison.Ordinal);
        Assert.Contains("data-bs-target=\"#offcanvasHistoricalStages\"", workspace, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Pages/Projects/Timeline/Historical.cshtml")]
    [InlineData("Pages/Projects/Timeline/Historical.cshtml.cs")]
    [InlineData("Pages/Projects/Timeline/_HistoricalStageForm.cshtml")]
    [InlineData("Services/Stages/HistoricalStageRecordService.cs")]
    [InlineData("ViewModels/HistoricalStageEditorVm.cs")]
    [InlineData("wwwroot/js/projects/historical-stage-editor.js")]
    [InlineData("wwwroot/js/projects/cover-photo-fallback.js")]
    [InlineData("ProjectManagement.Tests/HistoricalStageRecordServiceTests.cs")]
    [InlineData("ProjectManagement.Tests/ProjectOverviewLifecycleTests.cs")]
    [InlineData("ProjectManagement.Tests/ProjectPhotoPageTests.cs")]
    public void ReplacementManifest_IncludesHistoricalStageDeliveryContract(string requiredPath)
    {
        var manifest = ReadRepoFile("REPLACEMENT-MANIFEST.txt");
        var entries = manifest.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Contains(requiredPath, entries);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, Path.Combine(relativePath));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file: {Path.Combine(relativePath)}");
    }
}

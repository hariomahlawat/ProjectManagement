using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProcessJourneyPresentationContractTests
{
    [Fact]
    public void ProcessPage_UsesJourneyOnlyWithoutPermanentHeaderChrome()
    {
        var page = ReadRepoFile("Pages", "Process", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "process-flow.js");

        Assert.Contains("data-process-world", page, StringComparison.Ordinal);
        Assert.Contains("data-mode=\"journey\"", page, StringComparison.Ordinal);
        Assert.Contains("data-process-workspace", page, StringComparison.Ordinal);
        Assert.Contains("data-process-theme=\"light\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-mode-button", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Complete map", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("process-commandbar", page, StringComparison.Ordinal);
        Assert.DoesNotContain("process-workspace__identity", page, StringComparison.Ordinal);
        Assert.DoesNotContain("six governance phases", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("process-phase-strip", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("const PHASES", script, StringComparison.Ordinal);
        Assert.DoesNotContain("STAGE_PURPOSES", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessJourney_ProvidesFloatingUtilitiesAndLightDefaultTheme()
    {
        var page = ReadRepoFile("Pages", "Process", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "process-flow.js");
        var styles = ReadRepoFile("wwwroot", "css", "process-flow.css");

        Assert.Contains("process-utility-dock", page, StringComparison.Ordinal);
        Assert.Contains("data-action=\"open-stage-search\"", page, StringComparison.Ordinal);
        Assert.Contains("data-action=\"toggle-theme\"", page, StringComparison.Ordinal);
        Assert.Contains("data-action=\"toggle-fullscreen\"", page, StringComparison.Ordinal);
        Assert.Contains("data-fullscreen-exit", page, StringComparison.Ordinal);
        Assert.Contains("data-stage-search-dialog", page, StringComparison.Ordinal);
        Assert.Contains("data-process-introduction", page, StringComparison.Ordinal);
        Assert.Contains("function applyTheme", script, StringComparison.Ordinal);
        Assert.Contains("prism.process.theme", script, StringComparison.Ordinal);
        Assert.Contains("function syncWorkspaceHeight", script, StringComparison.Ordinal);
        Assert.Contains("--process-guide-width: clamp(350px, 22vw, 455px)", styles, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr) var(--process-guide-width)", styles, StringComparison.Ordinal);
        Assert.Contains("process-page[data-process-theme=\"dark\"]", styles, StringComparison.Ordinal);
        Assert.Contains("process-stage-search-dialog", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessJourney_RetainsProgressiveDisclosureAndWideMonitorContext()
    {
        var script = ReadRepoFile("wwwroot", "js", "process-flow.js");
        var styles = ReadRepoFile("wwwroot", "css", "process-flow.css");

        Assert.Contains("function journeyTiersFor", script, StringComparison.Ordinal);
        Assert.Contains("function branchClusterFor", script, StringComparison.Ordinal);
        Assert.Contains("function expandJourneyContextForWideViewport", script, StringComparison.Ordinal);
        Assert.Contains("process-route-signal", styles, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 1800px)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 2400px)", styles, StringComparison.Ordinal);
        Assert.Contains("ResizeObserver", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessPage_UsesFullWidthUnifiedEdgeToEdgeWorkspace()
    {
        var page = ReadRepoFile("Pages", "Process", "Index.cshtml");
        var styles = ReadRepoFile("wwwroot", "css", "process-flow.css");

        Assert.Contains("ViewData[\"PageShell\"] = \"workspace\"", page, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"UseFullWidth\"] = true", page, StringComparison.Ordinal);
        Assert.Contains("process-experience", page, StringComparison.Ordinal);
        Assert.Contains("gap: 0", styles, StringComparison.Ordinal);
        Assert.Contains("border-left: 1px solid #d8e1ec", styles, StringComparison.Ordinal);
        Assert.Contains("height: var(--process-available-height", styles, StringComparison.Ordinal);
    }


    [Fact]
    public void ProcessJourney_UsesAuthoritativeTerminalTopologyAndStructuredChecklistRendering()
    {
        var page = ReadRepoFile("Pages", "Process", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "process-flow.js");
        var styles = ReadRepoFile("wwwroot", "css", "process-flow.css");

        Assert.DoesNotContain("Capability complete", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("state.endpoint", script, StringComparison.Ordinal);
        Assert.Contains("if (detour.successor)", script, StringComparison.Ordinal);
        Assert.Contains("single optional continuation", script, StringComparison.Ordinal);
        Assert.Contains("function renderChecklistText", script, StringComparison.Ordinal);
        Assert.Contains("function splitInlineNumberedList", script, StringComparison.Ordinal);
        Assert.Contains("stage-checklist__sublist", script, StringComparison.Ordinal);
        Assert.Contains("Formatting is applied automatically", page, StringComparison.Ordinal);
        Assert.Contains("stage-checklist__content", styles, StringComparison.Ordinal);
        Assert.Contains("stage-checklist__title", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessFlow_TecAndBenchmarkingRunInParallelBeforeCommercialOpening()
    {
        var seeder = ReadRepoFile("Data", "StageFlowSeeder.cs");

        Assert.Contains(
            "D(StageCodes.TEC, StageCodes.BID, version)",
            seeder,
            StringComparison.Ordinal);
        Assert.Contains(
            "D(StageCodes.BM, StageCodes.BID, version)",
            seeder,
            StringComparison.Ordinal);
        Assert.Contains(
            "D(StageCodes.COB, StageCodes.TEC, version)",
            seeder,
            StringComparison.Ordinal);
        Assert.Contains(
            "D(StageCodes.COB, StageCodes.BM, version)",
            seeder,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "D(StageCodes.BM, StageCodes.TEC, version)",
            seeder,
            StringComparison.Ordinal);

        Assert.Contains(
            "D(StageCodes.EAS, StageCodes.COB, version)",
            seeder,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "D(StageCodes.EAS, StageCodes.PNC, version)",
            seeder,
            StringComparison.Ordinal);

        var topologyMigration = ReadRepoFile(
            "Migrations",
            "20261207180000_RefineProcurementJourneyTopology.cs");
        Assert.Contains("EAS", topologyMigration, StringComparison.Ordinal);
        Assert.Contains("PNC", topologyMigration, StringComparison.Ordinal);

        var migrationIds = ReadRepoFile("Migrations", "immutable-migration-ids.txt");
        Assert.Contains(
            "20261207180000_RefineProcurementJourneyTopology",
            migrationIds,
            StringComparison.Ordinal);
        var migration = ReadRepoFile(
            "Migrations",
            "20261207170000_RedesignProcurementJourney.cs");
        Assert.Contains("'BM', 'BID'", migration, StringComparison.Ordinal);
        Assert.Contains("'COB', 'TEC'", migration, StringComparison.Ordinal);
        Assert.Contains("'COB', 'BM'", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void PurposeEditing_IsRestrictedToAdminAndHoD()
    {
        var policies = ReadRepoFile("Configuration", "Policies.cs");
        var program = ReadRepoFile("Program.cs");

        Assert.Contains("RoleNames.Admin", policies, StringComparison.Ordinal);
        Assert.Contains("RoleNames.HoD", policies, StringComparison.Ordinal);
        Assert.Contains("Policies.Checklist.EditPurpose", program, StringComparison.Ordinal);
        Assert.Contains("PurposeUpdated", program, StringComparison.Ordinal);
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

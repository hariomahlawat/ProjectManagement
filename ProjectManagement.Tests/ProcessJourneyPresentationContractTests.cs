using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProcessJourneyPresentationContractTests
{
    [Fact]
    public void ProcessPage_RendersRouteMapAndStageGuideFromTheFlowApi()
    {
        var page = ReadRepoFile("Pages", "Process", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "process-flow.js");
        var route = ReadRepoFile("wwwroot", "js", "process-route.js");

        Assert.Contains("data-route-map", page, StringComparison.Ordinal);
        Assert.Contains("data-stage-guide", page, StringComparison.Ordinal);
        Assert.Contains("~/js/process-route.js", page, StringComparison.Ordinal);
        Assert.Contains("/api/processes/${encodeURIComponent(state.version)}/flow", script, StringComparison.Ordinal);
        Assert.Contains("/stages/${encodeURIComponent(code)}/checklist", script, StringComparison.Ordinal);
        Assert.Contains("#stage-${node.code.toLowerCase()}", script, StringComparison.Ordinal);
        Assert.Contains("function buildRoute", route, StringComparison.Ordinal);

        // Stages, purposes and phases come from the database, never from the client.
        foreach (var source in new[] { script, route })
        {
            Assert.DoesNotContain("const PHASES", source, StringComparison.Ordinal);
            Assert.DoesNotContain("STAGE_PURPOSES", source, StringComparison.Ordinal);
            Assert.DoesNotContain("'TEC'", source, StringComparison.Ordinal);
            Assert.DoesNotContain("'PNC'", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProcessPage_KeepsEditingBehindServerDecidedPermissions()
    {
        var page = ReadRepoFile("Pages", "Process", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "process-flow.js");

        Assert.Contains("data-can-edit-checklist=\"@(Model.CanEditChecklist", page, StringComparison.Ordinal);
        Assert.Contains("data-can-edit-purpose=\"@(Model.CanEditPurpose", page, StringComparison.Ordinal);
        Assert.Contains("id=\"stagePurposeModal\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"checklistItemModal\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"checklistDeleteModal\"", page, StringComparison.Ordinal);
        Assert.Contains("purposeEdit.hidden = !state.canEditPurpose", script, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", script, StringComparison.Ordinal);
        Assert.Contains("templateRowVersion", script, StringComparison.Ordinal);
        Assert.Contains("error?.status === 409", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessPage_IsLightweightAndFollowsTheGlobalTheme()
    {
        var page = ReadRepoFile("Pages", "Process", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "process-flow.js");
        var styles = ReadRepoFile("wwwroot", "css", "process-flow.css");

        // Native scrolling: the page never takes over the mouse wheel.
        Assert.DoesNotContain("'wheel'", script, StringComparison.Ordinal);
        // One theme for the whole application.
        Assert.DoesNotContain("prism.process.theme", script, StringComparison.Ordinal);
        Assert.DoesNotContain("data-process-theme", page, StringComparison.Ordinal);
        // No GPU-heavy effects or animations that run forever.
        Assert.DoesNotContain("backdrop-filter", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("filter: blur", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("infinite", styles, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessPage_LoadsNoInternetHostedAssets()
    {
        foreach (var source in new[]
                 {
                     ReadRepoFile("Pages", "Process", "Index.cshtml"),
                     ReadRepoFile("wwwroot", "js", "process-flow.js"),
                     ReadRepoFile("wwwroot", "js", "process-route.js"),
                     ReadRepoFile("wwwroot", "css", "process-flow.css")
                 })
        {
            Assert.DoesNotContain("http://", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ProcessPage_KeepsStructuredChecklistRendering()
    {
        var page = ReadRepoFile("Pages", "Process", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "process-flow.js");
        var route = ReadRepoFile("wwwroot", "js", "process-route.js");
        var styles = ReadRepoFile("wwwroot", "css", "process-flow.css");

        Assert.Contains("if (detour.successor)", route, StringComparison.Ordinal);
        Assert.Contains("single optional continuation", route, StringComparison.Ordinal);
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

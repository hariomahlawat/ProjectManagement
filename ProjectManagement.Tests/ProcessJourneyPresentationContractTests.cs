using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProcessJourneyPresentationContractTests
{
    [Fact]
    public void ProcessPage_UsesSingleJourneyWithoutInventedPhases()
    {
        var page = ReadRepoFile("Pages", "Process", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "process-flow.js");

        Assert.Contains("data-process-world", page, StringComparison.Ordinal);
        Assert.Contains("data-mode-button=\"journey\"", page, StringComparison.Ordinal);
        Assert.Contains("data-mode-button=\"map\"", page, StringComparison.Ordinal);
        Assert.Contains("From concept to capability.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("six governance phases", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("process-phase-strip", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("const PHASES", script, StringComparison.Ordinal);
        Assert.DoesNotContain("STAGE_PURPOSES", script, StringComparison.Ordinal);
    }


    [Fact]
    public void ProcessJourney_UsesProgressiveDisclosureAndSemanticMapZoom()
    {
        var page = ReadRepoFile("Pages", "Process", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "process-flow.js");
        var styles = ReadRepoFile("wwwroot", "css", "process-flow.css");

        Assert.Contains("data-fullscreen-exit", page, StringComparison.Ordinal);
        Assert.Contains("Search or jump to a stage", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-stage-jump", page, StringComparison.Ordinal);
        Assert.Contains("function journeyTiersFor", script, StringComparison.Ordinal);
        Assert.Contains("function branchClusterFor", script, StringComparison.Ordinal);
        Assert.Contains("data-map-density", styles, StringComparison.Ordinal);
        Assert.Contains("process-route-signal", styles, StringComparison.Ordinal);
        Assert.Contains("process-endpoint", styles, StringComparison.Ordinal);
        Assert.Contains("conditional-entry", script, StringComparison.Ordinal);
        Assert.Contains("terminal-main", script, StringComparison.Ordinal);
        Assert.Contains("prism.process.hero.seen.v2", script, StringComparison.Ordinal);
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

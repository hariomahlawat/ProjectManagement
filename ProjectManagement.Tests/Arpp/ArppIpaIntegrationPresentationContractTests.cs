using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppIpaIntegrationPresentationContractTests
{
    [Fact]
    public void ProcurementSummary_IdentifiesExactCurrentArppSource()
    {
        var markup = ReadRepoFile("Pages", "Shared", "_ProjectProcurementAtAGlance.cshtml");

        Assert.Contains("Current IPA source:", markup, StringComparison.Ordinal);
        Assert.Contains("Original ARPP", markup, StringComparison.Ordinal);
        Assert.Contains("Addendum No.", markup, StringComparison.Ordinal);
        Assert.Contains("IssueDate.Value.ToString", markup, StringComparison.Ordinal);
        Assert.Contains("Ser No.", markup, StringComparison.Ordinal);
        Assert.Contains("/Projects/Arpp/History", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcurementEditorManagementMessage_IdentifiesExactCurrentArppSource()
    {
        var pageModel = ReadRepoFile("Pages", "Projects", "Overview.cshtml.cs");

        Assert.Contains("BuildIpaManagementMessage", pageModel, StringComparison.Ordinal);
        Assert.Contains("Original ARPP", pageModel, StringComparison.Ordinal);
        Assert.Contains("Addendum No.", pageModel, StringComparison.Ordinal);
        Assert.Contains("position.IssueDate.Value.ToString", pageModel, StringComparison.Ordinal);
        Assert.Contains("Ser No.", pageModel, StringComparison.Ordinal);
        Assert.Contains("Update the authoritative value in the ARPP register", pageModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcurementEditor_UsesPublishedRowsAsTheAuthorityBoundary()
    {
        var pageModel = ReadRepoFile("Pages", "Projects", "Procurement", "Edit.cshtml.cs");

        Assert.Contains("_db.ArppPublishedEntries", pageModel, StringComparison.Ordinal);
        Assert.DoesNotContain("_db.ArppEntries", pageModel, StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleMutationServices_EnforcePublishedArppAuthority()
    {
        var progress = ReadRepoFile("Services", "Stages", "StageProgressService.cs");
        var requests = ReadRepoFile("Services", "Stages", "StageRequestService.cs");
        var decisions = ReadRepoFile("Services", "Stages", "StageDecisionService.cs");
        var directApply = ReadRepoFile("Services", "Stages", "StageDirectApplyService.cs");
        var backfill = ReadRepoFile("Services", "Stages", "StageBackfillService.cs");
        var actuals = ReadRepoFile("Services", "Stages", "StageActualsUpdateService.cs");
        var historical = ReadRepoFile("Services", "Stages", "HistoricalStageRecordService.cs");

        Assert.Contains("EnsureManualLifecycleMutationAllowedAsync", progress, StringComparison.Ordinal);
        Assert.Contains("ArppManagedIpaStageException.UserMessage", requests, StringComparison.Ordinal);
        Assert.Contains("ArppManagedIpaStageException.UserMessage", decisions, StringComparison.Ordinal);
        Assert.Contains("EnsureManualLifecycleMutationAllowedAsync", directApply, StringComparison.Ordinal);
        Assert.Contains("ArppManagedIpaStageException.UserMessage", backfill, StringComparison.Ordinal);
        Assert.Contains("Completion date is controlled by the published ARPP", actuals, StringComparison.Ordinal);
        Assert.Contains("managedIpaRow.CompletedOn != ipaAuthority.IssueDate", historical, StringComparison.Ordinal);
    }

    [Fact]
    public void DataQualityReporting_UsesAuditLogs_NotUnsupportedStageLogAction()
    {
        var synchronizer = ReadRepoFile("Services", "Arpp", "ArppIpaStageSynchronizer.cs");

        Assert.Contains("_db.AuditLogs", synchronizer, StringComparison.Ordinal);
        Assert.Contains("Arpp.IpaStageDataQualityIssue", synchronizer, StringComparison.Ordinal);
        Assert.DoesNotContain("Action = DataQualityLogAction", synchronizer, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_PresentsInitialAuthorityAndSuppressesManualStageActions()
    {
        var markup = ReadRepoFile("Pages", "Shared", "_ProjectTimeline.cshtml");

        Assert.Contains("ARPP-derived", markup, StringComparison.Ordinal);
        Assert.Contains("Initial IPA approval", markup, StringComparison.Ordinal);
        Assert.Contains("!s.IsArppManaged", markup, StringComparison.Ordinal);
        Assert.Contains("Not recorded", markup, StringComparison.Ordinal);
        Assert.Contains("Unavailable", markup, StringComparison.Ordinal);
        Assert.Contains("Actual start needs correction", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActualsAndHistoricalEditors_KeepCompletionLockedButAllowStartCorrection()
    {
        var actuals = ReadRepoFile("Pages", "Projects", "Timeline", "_EditActualsForm.cshtml");
        var historical = ReadRepoFile("Pages", "Projects", "Timeline", "_HistoricalStageForm.cshtml");

        Assert.Contains("canEditStart && !row.IsArppManaged", actuals, StringComparison.Ordinal);
        Assert.Contains("Leave blank when the actual start is not documented", actuals, StringComparison.Ordinal);
        Assert.Contains("Controlled by @row.ArppSourceLabel", actuals, StringComparison.Ordinal);

        Assert.Contains("name=\"Input.Rows[@index].Outcome\"", historical, StringComparison.Ordinal);
        Assert.Contains("name=\"Input.Rows[@index].CompletedOn\"", historical, StringComparison.Ordinal);
        Assert.Contains("Earliest published ARPP issue date", historical, StringComparison.Ordinal);
        Assert.Contains("name=\"Input.Rows[@index].ActualStart\"", historical, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var root = ResolveRepoRoot();
        var path = Path.Combine(new[] { root }.Concat(segments).ToArray());
        return File.ReadAllText(path);
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }
}

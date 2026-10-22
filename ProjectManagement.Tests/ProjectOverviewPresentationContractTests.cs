using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectOverviewPresentationContractTests
{
    [Fact]
    public void Breadcrumb_UsesOneExplicitSpacingOwner()
    {
        var overview = ReadRepoFile("Pages", "Projects", "Overview.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "pages", "project-portfolio.css");

        Assert.Contains("class=\"project-portfolio-breadcrumb\"", overview, StringComparison.Ordinal);
        Assert.Contains("class=\"breadcrumb mb-0\"", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"breadcrumb\" class=\"mb-3\"", overview, StringComparison.Ordinal);
        Assert.Contains(".project-portfolio-breadcrumb {", css, StringComparison.Ordinal);
        Assert.Contains(".project-portfolio-breadcrumb .breadcrumb {", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CoverPhoto_UsesLandscapeFourByThreeSurface()
    {
        var css = ReadRepoFile("wwwroot", "css", "pages", "project-portfolio.css");
        var overview = ReadRepoFile("Pages", "Projects", "_ProjectOverviewCard.cshtml");

        Assert.Contains("aspect-ratio: 4 / 3;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("aspect-ratio: 3 / 4;", css, StringComparison.Ordinal);
        Assert.Contains("project-photo-cover-frame--", overview, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_PreservesTerminalWorkspaceAndExplainsMissingHistory()
    {
        var workspace = ReadRepoFile("Pages", "Projects", "_ProjectLifecycleWorkspace.cshtml");

        Assert.Contains("if (!hasRecordedStageHistory && !isActiveLifecycle)", workspace, StringComparison.Ordinal);
        Assert.Contains("No reliable stage-level history is available.", workspace, StringComparison.Ordinal);
        Assert.Contains("<p class=\"pm-card-subtitle mb-0\">@timelineSubtitle</p>", workspace, StringComparison.Ordinal);
        Assert.Contains("project-timeline-empty-history", workspace, StringComparison.Ordinal);
        Assert.Contains("No recorded stage history", workspace, StringComparison.Ordinal);
        Assert.Contains("Project remarks remain available", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("No stage history recorded", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("No stage history was recorded", workspace, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(workspace, "No recorded stage history"));
    }

    [Fact]
    public void RecordScores_UseDistinctAndAccurateLabels()
    {
        // The header strip's tiles live in _ProjectHeaderTile, shared with the overview card.
        var header = ReadRepoFile("Pages", "Projects", "_ProjectHeaderTile.cshtml");
        var overview = ReadRepoFile("Pages", "Projects", "_ProjectOverviewCard.cshtml");
        var healthModal = ReadRepoFile("Pages", "Projects", "_ProjectRecordCompleteness.cshtml");
        var workspaceTable = ReadRepoFile("Pages", "Workspace", "_ProjectOfficerProjectTable.cshtml");
        var workspaceModals = ReadRepoFile("Pages", "Workspace", "_ProjectOfficerRecordModals.cshtml");
        var healthService = ReadRepoFile("Services", "Workspace", "ProjectRecordHealthService.cs");

        Assert.Contains("Overall record health", header, StringComparison.Ordinal);
        Assert.Contains("Overall record health", healthModal, StringComparison.Ordinal);
        Assert.Contains("Overall record health", workspaceTable, StringComparison.Ordinal);
        Assert.Contains("Overall record health", workspaceModals, StringComparison.Ordinal);
        Assert.Contains("Core profile fields:", overview, StringComparison.Ordinal);
        Assert.Contains("\"Project description\"", healthService, StringComparison.Ordinal);
        Assert.DoesNotContain("Project profile:", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("Core project profile", healthService, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentRequests_RenderAsTheWaitingBarUnderTheHeader()
    {
        var overview = ReadRepoFile("Pages", "Projects", "Overview.cshtml");
        var pending = ReadRepoFile("Pages", "Projects", "_ProjectPendingActions.cshtml");
        var waiting = ReadRepoFile("Pages", "Projects", "_ProjectWaitingOnYou.cshtml");

        var header = overview.IndexOf("_ProjectCommandHeader", StringComparison.Ordinal);
        var bar = overview.IndexOf("_ProjectWaitingOnYou", StringComparison.Ordinal);
        Assert.True(header >= 0 && bar > header, "The waiting bar follows the header.");
        Assert.True(bar < overview.IndexOf("project-portfolio-layout", StringComparison.Ordinal), "The waiting bar spans the page, above both columns.");

        // The bar keeps the approve and reject posts; the old card is gone from the column.
        Assert.Contains("asp-page-handler=\"Approve\"", waiting, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"Reject\"", waiting, StringComparison.Ordinal);
        Assert.Contains("Model.IsDocumentApprover", waiting, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentPendingRequests", pending, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyHeader_DistinguishesRecordMetadataFromHistoricalProjectDates()
    {
        var header = ReadRepoFile("Pages", "Projects", "_ProjectCommandHeader.cshtml");

        Assert.Contains("Mapped workflow", header, StringComparison.Ordinal);
        Assert.Contains("Record added", header, StringComparison.Ordinal);
        Assert.Contains("project?.IsLegacy == true", header, StringComparison.Ordinal);
    }

    [Fact]
    public void RemarksFilters_AreRenderedInsideRemarksBody()
    {
        var workspace = ReadRepoFile("Pages", "Projects", "_ProjectLifecycleWorkspace.cshtml");
        var remarksBodyIndex = workspace.IndexOf("data-panel=\"remarks\"", StringComparison.Ordinal);
        var filtersIndex = workspace.IndexOf("remarks-filter-toolbar", StringComparison.Ordinal);

        Assert.True(remarksBodyIndex >= 0, "The remarks body was not found.");
        Assert.True(filtersIndex > remarksBodyIndex, "Remarks filters must be rendered in the body rather than the card header.");
    }

    [Fact]
    public void TotCard_UsesServerToneAndTracksActualDirtyState()
    {
        var card = ReadRepoFile("Pages", "Shared", "Components", "ProjectTotCommandCard", "Default.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "projects", "overview-tot.js");

        Assert.Contains("project-intelligence-card--tone-@Model.Tone", card, StringComparison.Ordinal);
        Assert.Contains("data-tot-card-tone=\"@Model.Tone\"", card, StringComparison.Ordinal);
        Assert.Contains("payload.tone", script, StringComparison.Ordinal);
        Assert.Contains("hasUnsavedDetails()", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredPostCompletionPartial_CannotRestoreLowerTotPanel()
    {
        var postCompletion = ReadRepoFile("Pages", "Projects", "_ProjectPostCompletion.cshtml");
        var exploitationTransfer = ReadRepoFile("Pages", "Projects", "_ProjectExploitationTransfer.cshtml");

        Assert.DoesNotContain("_ProjectTotPanel", postCompletion, StringComparison.Ordinal);
        Assert.DoesNotContain("_ProjectTotPanel", exploitationTransfer, StringComparison.Ordinal);
        Assert.Contains("Compatibility placeholder", postCompletion, StringComparison.Ordinal);
        Assert.Contains("Compatibility placeholder", exploitationTransfer, StringComparison.Ordinal);
    }

    [Fact]
    public void ProliferationEditor_IsolatesItsValidationScopeAndAllowsExplicitZeroCost()
    {
        var pageModel = ReadRepoFile("Pages", "Projects", "Overview.cshtml.cs");
        var form = ReadRepoFile("Pages", "Projects", "_ProjectProliferationEditForm.cshtml");
        var viewModel = ReadRepoFile("ViewModels", "ProjectProliferationProfileVm.cs");

        var handlerStart = pageModel.IndexOf("OnPostProliferationAsync", StringComparison.Ordinal);
        var handlerEnd = pageModel.IndexOf("OnPostCompleteAsync", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart, "The proliferation POST handler could not be isolated.");

        var handler = pageModel[handlerStart..handlerEnd];
        Assert.Contains("GetProliferationModelStateErrors(ModelState)", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!ModelState.IsValid)", handler, StringComparison.Ordinal);
        Assert.Contains("ProliferationInputPrefix", pageModel, StringComparison.Ordinal);
        Assert.Contains("StartsWith(ProliferationInputPrefix", pageModel, StringComparison.Ordinal);

        Assert.Contains("min=\"0\"", form, StringComparison.Ordinal);
        Assert.DoesNotContain("min=\"0.01\"", form, StringComparison.Ordinal);
        Assert.Contains("Use 0 when the cost is explicitly zero", form, StringComparison.Ordinal);

        Assert.Contains("[Range(typeof(decimal), \"0\"", viewModel, StringComparison.Ordinal);
        Assert.Contains("if (!costLakhs.HasValue)", viewModel, StringComparison.Ordinal);
        Assert.Contains("₹{costLakhs.Value:0.##} lakh", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelineEditors_AreVisibleAndNotHiddenByAPrivatePlanDraft()
    {
        // Discovery C2.1: an unsubmitted plan draft used to hide both editors,
        // locking the PO out of the actual-date editor. They are now visible buttons
        // gated only by edit permission, not menu items.
        var workspace = ReadRepoFile("Pages", "Projects", "_ProjectLifecycleWorkspace.cshtml");

        Assert.Contains("var canEditTimelinePlan = canEditTimeline;", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("hasMyDraft && !pendingOwnedByCurrentUser", workspace, StringComparison.Ordinal);

        var editors = workspace[workspace.IndexOf("if (canEditTimelinePlan)", StringComparison.Ordinal)..];
        editors = editors[..editors.IndexOf("if (canManageHistoricalStageHistory)", StringComparison.Ordinal)];
        Assert.Contains("data-timeline-action=\"edit-plan\"", editors, StringComparison.Ordinal);
        Assert.Contains("data-timeline-action=\"correct-actuals\"", editors, StringComparison.Ordinal);
        Assert.DoesNotContain("dropdown-item", editors, StringComparison.Ordinal);
    }

    [Fact]
    public void Overview_PutsTheTimelineFirstAndFoldsLifecyclePositionIntoIt()
    {
        // The Timeline comes first in the markup so that, on narrow screens, it follows the
        // header directly (desktop CSS keeps it in the right-hand column). Its plan, schedule
        // and next-action facts replace the separate "Lifecycle position" card.
        var overview = ReadRepoFile("Pages", "Projects", "Overview.cshtml");
        var work = overview.IndexOf("project-portfolio-work", StringComparison.Ordinal);
        var details = overview.IndexOf("project-portfolio-details", StringComparison.Ordinal);
        Assert.True(work >= 0 && details > work);
        Assert.True(overview.IndexOf("_ProjectLifecycleWorkspace", StringComparison.Ordinal) < details);
        Assert.True(overview.IndexOf("_ProjectOverviewCard", StringComparison.Ordinal) > details);
        Assert.DoesNotContain("_ProjectLifecyclePosition", overview, StringComparison.Ordinal);

        // Desktop: the Timeline/Remarks panel stays in the right-hand column (owner decision).
        var css = ReadRepoFile("wwwroot", "css", "pages", "project-portfolio.css");
        Assert.Contains(".project-portfolio-layout--work-first > .project-portfolio-work { grid-column: 2; grid-row: 1; }", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1.62fr) minmax(31rem, .98fr);", css, StringComparison.Ordinal);

        var workspace = ReadRepoFile("Pages", "Projects", "_ProjectLifecycleWorkspace.cshtml");
        Assert.Contains("data-timeline-facts", workspace, StringComparison.Ordinal);
        Assert.Contains("@portfolio.ScheduleStatus", workspace, StringComparison.Ordinal);
        Assert.Contains("@portfolio.NextAction", workspace, StringComparison.Ordinal);

        // Completed and cancelled projects keep the facts (the removed card showed them for
        // every lifecycle), under the model's historical labels; only the submission status
        // is limited to active projects.
        Assert.DoesNotMatch(@"@if \(isActiveLifecycle\)\s*\{\s*@\*[^*]*""Lifecycle position""", workspace);
        Assert.Contains("portfolio.TimelinePlanLabel", workspace, StringComparison.Ordinal);
        Assert.Contains("portfolio.ScheduleStatusLabel", workspace, StringComparison.Ordinal);
        Assert.Contains("portfolio.NextActionLabel", workspace, StringComparison.Ordinal);
        Assert.Matches(@"if \(isActiveLifecycle\)\s*\{\s*<partial name=""_TimelineSubmissionStatus""", workspace);
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

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(relativePath)}");
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}

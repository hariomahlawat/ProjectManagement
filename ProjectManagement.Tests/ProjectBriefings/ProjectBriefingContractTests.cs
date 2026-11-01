using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingContractTests
{
    [Fact]
    public void Builder_UsesApprovedTerminologySelectionMethodsAndExternalStatusPolicy()
    {
        var page = Read("Index.cshtml");

        Assert.Contains("Project Briefing Deck Builder", page, StringComparison.Ordinal);
        Assert.Contains("Briefing decks", Read("_CommandWorkspaceRail.cshtml"), StringComparison.Ordinal);
        Assert.Contains("All ongoing projects", page, StringComparison.Ordinal);
        Assert.Contains("Recently completed", page, StringComparison.Ordinal);
        Assert.Contains("Project category", page, StringComparison.Ordinal);
        Assert.Contains("Technical category", page, StringComparison.Ordinal);
        Assert.Contains("Available for proliferation", page, StringComparison.Ordinal);
        Assert.Contains("Manage individually", page, StringComparison.Ordinal);
        Assert.Contains("Cost (R&amp;D)", page, StringComparison.Ordinal);
        Assert.Contains("Generate PowerPoint", page, StringComparison.Ordinal);
        Assert.Contains("Project Update Review", page, StringComparison.Ordinal);
        Assert.Contains("Shared decks", page, StringComparison.Ordinal);
        Assert.Contains("Add projects", page, StringComparison.Ordinal);
        Assert.Contains("recommended chart", page, StringComparison.Ordinal);
        Assert.Contains("optional supporting slide", page, StringComparison.Ordinal);
    }


    [Fact]
    public void Builder_ReplacesTheLongPageWithTheStudio()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");

        // The checks section, projects table and Add projects panel are retired; the Studio and
        // the Add projects dialog take their place.
        Assert.DoesNotContain("Content used by this presentation", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-pbd-selected-section", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-pbd-selector-details", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Manage this deck", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-studio", page, StringComparison.Ordinal);
        Assert.Contains("id=\"pbd-add-projects-modal\"", page, StringComparison.Ordinal);
        Assert.Contains("Missing content uses defined placeholders", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-decks-toggle", page, StringComparison.Ordinal);
        Assert.Contains("is-decks-collapsed", script, StringComparison.Ordinal);

        // Additional slides live in the inspector's Deck tab, before the Checks tab.
        var deckPanel = page.IndexOf("data-pbd-inspector-panel=\"deck\"", StringComparison.Ordinal);
        var additional = page.IndexOf("id=\"pbd-additional-slides\"", StringComparison.Ordinal);
        var checksPanel = page.IndexOf("data-pbd-inspector-panel=\"checks\"", StringComparison.Ordinal);
        Assert.True(deckPanel >= 0 && deckPanel < additional && additional < checksPanel,
            "The additional-slide cards belong to the Deck tab.");

        // The deleted/archived notice moved into the Studio stage.
        Assert.Contains("pbd-stage__notice", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-settings-form", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-preset=", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-standard-section", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-settings-collapsible", page, StringComparison.Ordinal);
    }


    [Fact]
    public void Builder_KeepsSettingsInTheDeckTabSavedInPlace()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var css = Read("project-briefing-decks.css");

        // The drawer is gone: every setting sits under All settings in the Deck tab and saves itself.
        Assert.DoesNotContain("data-pbd-settings-drawer", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Save settings", page, StringComparison.Ordinal);
        var deckPanel = page.IndexOf("data-pbd-inspector-panel=\"deck\"", StringComparison.Ordinal);
        var form = page.IndexOf("data-pbd-settings-form", StringComparison.Ordinal);
        var checksPanel = page.IndexOf("data-pbd-inspector-panel=\"checks\"", StringComparison.Ordinal);
        Assert.True(deckPanel >= 0 && deckPanel < form && form < checksPanel, "The settings form belongs to the Deck tab.");
        Assert.Contains("data-pbd-deck-advanced", page, StringComparison.Ordinal);
        Assert.Contains("serializeSettings", script, StringComparison.Ordinal);
        Assert.Contains("beforeunload", script, StringComparison.Ordinal);
        Assert.Contains("saveSettingsNow", script, StringComparison.Ordinal);
        Assert.Contains("restoreSettingsSectionState", script, StringComparison.Ordinal);
        Assert.Contains("contain: inline-size", css, StringComparison.Ordinal);
        Assert.Contains("overflow-x: clip", css, StringComparison.Ordinal);
        Assert.Contains("max-width: 1499px", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_UsesCompactTemplateAwareSettingsLayout()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var css = Read("project-briefing-decks.css");

        Assert.Contains("data-pbd-settings-appearance-title", page, StringComparison.Ordinal);
        Assert.Contains("Classification marking <small>optional</small>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Handling / classification marking", page, StringComparison.Ordinal);
        Assert.Contains("Presentation theme", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-theme-preview", page, StringComparison.Ordinal);
        Assert.Contains("preview.classList.toggle('is-update-sheet'", script, StringComparison.Ordinal);
        Assert.Contains(".pbd-theme-preview--light.is-update-sheet", css, StringComparison.Ordinal);
        Assert.Contains(".pbd-theme-preview--light .pbd-theme-preview__header { background: #7a263a; }", css, StringComparison.Ordinal);
        Assert.Contains(".pbd-theme-preview--dark .pbd-theme-preview__header { background: #8a3042; }", css, StringComparison.Ordinal);
        Assert.Contains("background: #242a34;", css, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid #4a5260;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("appearanceTitle.textContent", script, StringComparison.Ordinal);
        // In the narrow Deck tab every choice group stacks in one column.
        Assert.Contains(".pbd-deck-settings .pbd-choice-cards,\n.pbd-deck-settings .pbd-theme-cards,\n.pbd-deck-settings .pbd-form-grid {\n    grid-template-columns: minmax(0, 1fr);", css.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void Client_UsesApplicationAntiforgeryHeaderAndDownloadsPptx()
    {
        var script = Read("project-briefing-decks.js");

        Assert.Contains("X-CSRF-TOKEN", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'RequestVerificationToken'", script, StringComparison.Ordinal);
        Assert.Contains("application/vnd.openxmlformats-officedocument.presentationml.presentation", script, StringComparison.Ordinal);
        Assert.Contains("X-Project-Briefing-Slides", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CostResolver_UsesTheSharedProjectCostRuleAndKeepsProliferationSeparate()
    {
        // Cost (R&D) is the project's cost under the one shared rule; the order itself is
        // pinned by ProjectCostResolverTests.
        var source = Read("ProjectBriefingCostResolver.cs");

        Assert.Contains("new ProjectCostResolver(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectCommercialFacts", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCostBasis.PNC", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCostBasis.Recorded", source, StringComparison.Ordinal);
        Assert.Contains("ResolveCostsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingResolvedCosts", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCostBasis.IPA", source, StringComparison.Ordinal);
        Assert.Contains("ProjectProductionCostFacts", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCostBasis.Proliferation", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalStatusResolver_UsesExternalGeneralRemarksOnly()
    {
        // The authoritative query now lives in the shared formal-output reader.
        // The briefing service is intentionally only an adapter over that reader.
        var source = Read("ProjectLatestExternalRemarkService.cs");
        var adapter = Read("ProjectBriefingExternalStatusService.cs");

        Assert.Contains("remark.Type == RemarkType.External", source, StringComparison.Ordinal);
        Assert.Contains("remark.Scope == RemarkScope.General", source, StringComparison.Ordinal);
        Assert.Contains("!remark.IsDeleted", source, StringComparison.Ordinal);
        Assert.Contains("row.LastEditedAtUtc ?? row.CreatedAtUtc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemarkType.Internal", source, StringComparison.Ordinal);
        Assert.Contains("IProjectLatestExternalRemarkService", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectUpdateSheet_UsesApprovedTemplateAndAuthoritativeFieldSources()
    {
        var page = Read("Index.cshtml");
        var resolver = Read("ProjectBriefingUpdateSheetFactsResolver.cs");
        var composer = Read("ProjectBriefingSlideComposer.UpdateSheet.cs");
        var planner = Read("ProjectBriefingUpdateSheetPlanner.cs");

        Assert.Contains("Project Update Sheets", page, StringComparison.Ordinal);
        Assert.Contains("value=\"ProjectUpdateSheet\"", page, StringComparison.Ordinal);
        Assert.Contains("Information shown on each project sheet", page, StringComparison.Ordinal);
        Assert.Contains("Project name remains the slide title", page, StringComparison.Ordinal);
        Assert.Contains("Hide fields with no recorded value", page, StringComparison.Ordinal);
        Assert.Contains("Include cover slide", page, StringComparison.Ordinal);
        Assert.Contains("Include portfolio-summary slide", page, StringComparison.Ordinal);

        var formalFacts = Read("ProjectFormalUpdateFactsResolver.cs");

        Assert.Contains("IProjectFormalUpdateFactsResolver", resolver, StringComparison.Ordinal);
        Assert.Contains("StageCodes.AON", formalFacts, StringComparison.Ordinal);
        Assert.Contains("StageCodes.DEVP", formalFacts, StringComparison.Ordinal);
        Assert.Contains("project.LeadPoUser.Rank", resolver, StringComparison.Ordinal);
        Assert.Contains("project.LeadPoUser.FullName", resolver, StringComparison.Ordinal);
        Assert.Contains("project.SponsoringLineDirectorate.Name", resolver, StringComparison.Ordinal);
        Assert.Contains("link.IndustryPartner.Name", resolver, StringComparison.Ordinal);
        Assert.Contains("ProjectSupplyOrderFacts", formalFacts, StringComparison.Ordinal);
        Assert.Contains("ArppPublishedEntries", resolver, StringComparison.Ordinal);

        Assert.Contains("RenderProjectUpdateSheet", composer, StringComparison.Ordinal);
        Assert.Contains("source.CostRd.DisplayValue", planner, StringComparison.Ordinal);
        Assert.Contains("NormalizeStatus(source.ExternalStatus)", planner, StringComparison.Ordinal);
        Assert.Contains("BRIEF OF THE PROJECT", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("PROJECT UPDATE SHEET", composer, StringComparison.Ordinal);
        Assert.Contains("AddProjectSlideHeader", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectSlideHeaderVariant.ProjectUpdateSheet", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateSheetAccent", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectUpdateLabelFill", composer, StringComparison.Ordinal);
        Assert.Contains("SO Date:", planner, StringComparison.Ordinal);
        Assert.Contains("Firm:", planner, StringComparison.Ordinal);
        Assert.Contains("string.Join(\"\\n\"", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("B5122B", composer, StringComparison.OrdinalIgnoreCase);
    }



    [Fact]
    public void ProjectUpdateSheet_PortfolioSummaryShowsSeparateRdAndAuthoritativeIpaTotals()
    {
        var dataSource = Read("ProjectBriefingDataService.cs");
        var composer = Read("ProjectBriefingSlideComposer.ExecutiveSummary.cs");

        Assert.Contains("TotalIpaCostInRupees", dataSource, StringComparison.Ordinal);
        Assert.Contains("IpaCostRecordedCount", dataSource, StringComparison.Ordinal);
        Assert.Contains("RECORDED R&D COST", composer, StringComparison.Ordinal);
        Assert.Contains("RECORDED IPA COST", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingLayout.ProjectUpdateSheet", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectUpdateSheet_SupportsThemeAndAdaptiveSelectableRowsWithoutMigration()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var css = Read("project-briefing-decks.css");
        var dataSource = Read("ProjectBriefingDataService.cs");
        var service = Read("ProjectBriefingDeckService.cs");
        var composer = Read("ProjectBriefingSlideComposer.UpdateSheet.cs");
        var planner = Read("ProjectBriefingUpdateSheetPlanner.cs");

        Assert.Contains("name=\"PresentationTheme\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"UpdateSheetRows\"", page, StringComparison.Ordinal);
        Assert.Contains("UpdateSheetRowOrder", page, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingUpdateSheetOptions.AllRows", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-update-row-list", page, StringComparison.Ordinal);
        Assert.Contains("selectedUpdateSheetRowKeys", script, StringComparison.Ordinal);
        Assert.Contains("restoreUpdateRowOrder", script, StringComparison.Ordinal);
        Assert.Contains("recommendedKeys.has", script, StringComparison.Ordinal);
        Assert.Contains("validateUpdateSheetRows", script, StringComparison.Ordinal);
        Assert.Contains(".pbd-update-row-list", css, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingDeckConfigurationCodec.WithPresentationOptions", service, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingDeckConfigurationCodec.WithSelectionRules", service, StringComparison.Ordinal);
        Assert.Contains("SelectionRulesJson", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingUpdateSheetPlanner.ResolveRows", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingUpdateSheetLayoutVariant.Compact", planner, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingUpdateSheetLayoutVariant.Standard", planner, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingUpdateSheetLayoutVariant.Detailed", planner, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", planner, StringComparison.Ordinal);
        Assert.Contains("Completion Status", planner, StringComparison.Ordinal);
        Assert.Contains("Project completed", planner, StringComparison.Ordinal);
        Assert.Contains("StageCodes.DEVP", planner, StringComparison.Ordinal);
        Assert.Contains("BriefTypographyProfiles", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("Name of Project", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationVisualSystem_UsesSharedSemanticHeaderAndNarrativeRoles()
    {
        var theme = Read("ProjectBriefingThemeDefinition.cs");
        var composer = Read("ProjectBriefingSlideComposer.cs");
        var updateComposer = Read("ProjectBriefingSlideComposer.UpdateSheet.cs");
        var typography = Read("ProjectBriefingNarrativeTypography.cs");

        Assert.Contains("HeaderAccent", theme, StringComparison.Ordinal);
        Assert.Contains("ProjectUpdateAccent", theme, StringComparison.Ordinal);
        Assert.Contains("ProjectUpdateLabelFill", theme, StringComparison.Ordinal);
        Assert.Contains("OperationalAccent", theme, StringComparison.Ordinal);
        Assert.Contains("NarrativeAccent", theme, StringComparison.Ordinal);
        Assert.Contains("AddProjectSlideHeader", composer, StringComparison.Ordinal);
        Assert.Contains("Project sheet title", composer, StringComparison.Ordinal);
        Assert.Contains("canvas.Theme.TextPrimary", composer, StringComparison.Ordinal);
        Assert.Contains("canvas.Theme.ProjectUpdateAccent", composer, StringComparison.Ordinal);
        Assert.Contains("CalculateDetailedLayout", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingNarrativeDensity.Sparse", typography, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingNarrativeDensity.Dense", typography, StringComparison.Ordinal);
        Assert.Contains("ResolveProjectBrief", typography, StringComparison.Ordinal);
        Assert.Contains("ResolveUpdateSheetBrief", typography, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string UpdateSheetAccent", updateComposer, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedDecks_AreCommandWorkspaceWideAndTrackLastModifier()
    {
        var source = Read("ProjectBriefingDeckService.cs");

        Assert.DoesNotContain("deck.OwnerUserId ==", source, StringComparison.Ordinal);
        Assert.Contains("LastModifiedByUserId", source, StringComparison.Ordinal);
        Assert.Contains("A shared command deck with this name already exists", source, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(deck => deck.UpdatedAtUtc)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StageSummary_UsesCanonicalMaturityOrderAcrossBuilderAndPresentation()
    {
        var dataSource = Read("ProjectBriefingDataService.cs");
        var composer = Read("ProjectBriefingSlideComposer.cs");
        var stageComposer = Read("ProjectBriefingSlideComposer.StageSummary.cs");

        Assert.Contains("ProjectBriefingStageOrder.BuildSummary", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingProjectOrdering.OrderProjects", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingStageOrder.Resolve", dataSource, StringComparison.Ordinal);
        Assert.Contains("OrderProjects(data.Projects)", composer, StringComparison.Ordinal);
        Assert.Contains("AddStageSummarySlides", composer, StringComparison.Ordinal);
        Assert.Contains("RenderStageWiseExecutiveSummary", composer, StringComparison.Ordinal);
        Assert.Contains("RenderStageWiseExecutiveSummary", stageComposer, StringComparison.Ordinal);
        Assert.Contains("OngoingStageSummary", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectLifecycleStatus.Active", dataSource, StringComparison.Ordinal);
        Assert.Contains("CancelledCount", dataSource, StringComparison.Ordinal);
        Assert.Contains("ONGOING PROJECTS BY PRESENT STAGE", stageComposer, StringComparison.Ordinal);
        Assert.Contains("KEY TAKEAWAY", stageComposer, StringComparison.Ordinal);
        Assert.Contains("AddEllipse", composer, StringComparison.Ordinal);
        Assert.Contains("RenderStageSummaryTable", composer, StringComparison.Ordinal);
        Assert.Contains("StandardSlideOptions.IncludeStageDistributionTable", composer, StringComparison.Ordinal);
        Assert.Contains("IncludeStageDistributionTable", dataSource, StringComparison.Ordinal);
        Assert.Contains("Stage-wise project distribution", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("data.Summary.StageSummary.Chunk", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("reverse workflow order", composer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bars are native editable PowerPoint shapes", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("STATUS: LATEST EXTERNAL REMARK ONLY", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingTablePagination.Paginate", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingTablePagination.Paginate", dataSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailedSlide_PrioritisesCapabilityOverviewAndCombinesStageWithStatus()
    {
        var composer = Read("ProjectBriefingSlideComposer.cs");

        Assert.Contains("PRESENT STATUS", composer, StringComparison.Ordinal);
        var detailStart = composer.IndexOf("private static void RenderProjectDetail(", StringComparison.Ordinal);
        var detailEnd = composer.IndexOf("private static void RenderCapabilityContinuation(", detailStart, StringComparison.Ordinal);
        Assert.True(detailStart >= 0 && detailEnd > detailStart);
        Assert.DoesNotContain("PROJECT POSITION", composer[detailStart..detailEnd], StringComparison.Ordinal);
        Assert.Contains("CAPABILITY OVERVIEW", composer, StringComparison.Ordinal);
        Assert.Contains("const double rightWidth = 7.48", composer, StringComparison.Ordinal);
        Assert.Contains("CalculateDetailedLayout", composer, StringComparison.Ordinal);
        Assert.Contains("vertOverflow=\\\"clip\\\"", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCapabilityPaginator.Paginate", composer, StringComparison.Ordinal);
        Assert.Contains("RenderCapabilityContinuation", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("FitOverview", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_OffersIndependentCapabilityAndProjectBriefNarratives()
    {
        var page = Read("Index.cshtml");
        var dataSource = Read("ProjectBriefingDataService.cs");
        var composer = Read("ProjectBriefingSlideComposer.cs");

        Assert.Contains("Project content", page, StringComparison.Ordinal);
        Assert.Contains("value=\"CapabilityOverview\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"ProjectBrief\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"Both\"", page, StringComparison.Ordinal);
        Assert.Contains("ProjectCapabilityStatements", dataSource, StringComparison.Ordinal);
        Assert.Contains("item.Project.ProjectBrief", dataSource, StringComparison.Ordinal);
        Assert.Contains("RenderProjectBrief", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingNarrativeMode.Both", composer, StringComparison.Ordinal);
    }


    [Fact]
    public void StandardBriefing_OffersTwoProjectBriefDesignsAndIndependentContextControls()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var composer = Read("ProjectBriefingSlideComposer.cs");

        Assert.Contains("Project Brief layout", page, StringComparison.Ordinal);
        Assert.Contains("name=\"ProjectBriefLayout\" value=\"Automatic\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"ProjectBriefLayout\" value=\"Standard\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"ProjectBriefLayout\" value=\"PhotoEmphasis\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"ShowPresentStage\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"ShowPresentStatus\"", page, StringComparison.Ordinal);
        Assert.Contains("Financial information", page, StringComparison.Ordinal);
        Assert.Contains("value=\"CostRdOnly\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"ProliferationOnly\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"Both\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"None\"", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-project-brief-layout-settings", page, StringComparison.Ordinal);
        Assert.Contains("ResolveProjectBriefLayout", composer, StringComparison.Ordinal);
        Assert.Contains("RenderStandardProjectBrief", composer, StringComparison.Ordinal);
        Assert.Contains("RenderPhotoEmphasisProjectBrief", composer, StringComparison.Ordinal);
        Assert.Contains("CostCards(canvas, data.CostMode, project)", composer, StringComparison.Ordinal);
        Assert.Contains("AddProjectBriefInformationStrip", composer, StringComparison.Ordinal);
        Assert.Contains("ResolvePresentStatusValue", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("normalized = \"No external status recorded\"", composer, StringComparison.Ordinal);
    }


    [Fact]
    public void Builder_AppendsConfigurableProfessionalClosingSlideToEveryDeck()
    {
        var page = Read("Index.cshtml");
        var composer = Read("ProjectBriefingSlideComposer.cs");
        var codec = Read("ProjectBriefingDeckConfigurationCodec.cs");
        var dataSource = Read("ProjectBriefingDataService.cs");

        Assert.Contains("name=\"ClosingSlideType\" value=\"JaiHind\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"ClosingSlideType\" value=\"ThankYou\"", page, StringComparison.Ordinal);
        Assert.Contains("One ceremonial closing slide is always appended", page, StringComparison.Ordinal);
        Assert.Contains("SlidePlanKind.Closing", composer, StringComparison.Ordinal);
        Assert.Contains("RenderClosingSlide", composer, StringComparison.Ordinal);
        Assert.Contains("HeaderVariant.Closing", composer, StringComparison.Ordinal);
        Assert.Contains("AddSubtleRoundedRect", composer, StringComparison.Ordinal);
        Assert.Contains("Closing ceremonial panel", composer, StringComparison.Ordinal);
        Assert.Contains("Closing organisation", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("Closing deck descriptor", composer, StringComparison.Ordinal);
        Assert.Contains("Closing saffron accent", composer, StringComparison.Ordinal);
        Assert.Contains("Closing white accent", composer, StringComparison.Ordinal);
        Assert.Contains("Closing green accent", composer, StringComparison.Ordinal);
        Assert.Contains("kind is SlidePlanKind.Cover or SlidePlanKind.Closing", composer, StringComparison.Ordinal);
        Assert.Contains("[\"closingSlide\"]", codec, StringComparison.Ordinal);
        Assert.Contains("ClosingSlides = 1", dataSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AudienceDeck_RemovesBuilderReadinessAndUsesFullCapabilityPagination()
    {
        var composer = Read("ProjectBriefingSlideComposer.cs");
        var executive = Read("ProjectBriefingSlideComposer.ExecutiveSummary.cs");
        var dataSource = Read("ProjectBriefingDataService.cs");

        Assert.DoesNotContain("STATUS MISSING", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("DATA READINESS", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerPoint-ready photo", composer, StringComparison.Ordinal);
        Assert.Contains("DATA COVERAGE", executive, StringComparison.Ordinal);
        Assert.Contains("metric.Recorded.ToString(CultureInfo.InvariantCulture)", executive, StringComparison.Ordinal);
        Assert.Contains("totalProjects.ToString(CultureInfo.InvariantCulture)", executive, StringComparison.Ordinal);
        Assert.Contains("CapabilityContinuationSlides", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCapabilityPaginator", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingTextNormalizer.NormalizeFull", dataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectBriefingTextNormalizer.Normalize(", dataSource, StringComparison.Ordinal);
        Assert.Contains("ExecutiveStatus", composer, StringComparison.Ordinal);
        Assert.Contains("includeBasis: false", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_ManagesMembershipWithoutFullPageReload()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var handler = Read("Index.cshtml.cs");
        var service = Read("ProjectBriefingDeckService.cs");

        Assert.Contains("data-membership-url", page, StringComparison.Ordinal);
        Assert.Contains("In this deck", page, StringComparison.Ordinal);
        Assert.Contains("Not in this deck", page, StringComparison.Ordinal);
        Assert.Contains("Apply changes", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-add-selection-form", page, StringComparison.Ordinal);
        Assert.Contains("updateMembership", script, StringComparison.Ordinal);
        Assert.Contains("sessionStorage", script, StringComparison.Ordinal);
        // An in-page Add projects request is answered with the refreshed deck, not a redirect.
        Assert.Contains("return new JsonResult(new { saved = true, message, deck });", handler, StringComparison.Ordinal);
        Assert.Contains("UpdateMembershipAsync", service, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefing.MembershipUpdated", service, StringComparison.Ordinal);
    }


    [Fact]
    public void SavedDeckItems_RemainVisibleAndRemovableAfterProjectLifecycleChanges()
    {
        var dataSource = Read("ProjectBriefingDataService.cs");

        Assert.Contains(".Where(item => item.DeckId == deckId)", dataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("&& !item.Project.IsDeleted", dataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("&& !item.Project.IsArchived", dataSource, StringComparison.Ordinal);
        Assert.Contains("Deleted record", dataSource, StringComparison.Ordinal);
        Assert.Contains("Archived", dataSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_PersistsProfessionalThemesBrandingAndRevealsFilteredMatches()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var composer = Read("ProjectBriefingSlideComposer.cs");

        Assert.Contains("Editorial Light", page, StringComparison.Ordinal);
        Assert.Contains("Graphite Dark", page, StringComparison.Ordinal);
        Assert.Contains("Presentation branding", page, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingThemeCatalog.Resolve", composer, StringComparison.Ordinal);
        Assert.Contains("AddBrandingImages", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("SIMULATOR DEVELOPMENT DIVISION · PRISM ERP", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_KeepsCanonicalStageGroupsAndReordersOnlyWithinAStage()
    {
        var studio = Read("project-briefing-studio.js");
        var script = Read("project-briefing-decks.js");
        var composer = Read("ProjectBriefingSlideComposer.cs");

        // The outline groups projects by present stage and only reorders inside a group.
        Assert.Contains("Projects are grouped by present stage, in the deck's (maturity) order.", studio, StringComparison.Ordinal);
        Assert.Contains("requestReorder(stageIdsFromList(list))", studio, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('pbd:reorder-projects'", script, StringComparison.Ordinal);
        Assert.Contains("double LeftMargin = .05", composer, StringComparison.Ordinal);
        Assert.Contains("double TopMargin = .025", composer, StringComparison.Ordinal);
        Assert.Contains("Emu(cell.LeftMargin)", composer, StringComparison.Ordinal);
        Assert.Contains("Emu(cell.TopMargin)", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void PhotoLoader_ValidatesActualFilesAndProducesPowerPointReadyJpeg()
    {
        var source = Read("ProjectBriefingPhotoLoader.cs");

        Assert.Contains("Image.Identify", source, StringComparison.Ordinal);
        Assert.Contains("ResizeMode.Crop", source, StringComparison.Ordinal);
        Assert.Contains("new JpegEncoder", source, StringComparison.Ordinal);
        Assert.Contains("master/", source, StringComparison.Ordinal);
        Assert.Contains("No PowerPoint-ready photograph was found", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InstitutionalProfileSlide_IsOptionalModularAndUsesAuthoritativePrismBreakdowns()
    {
        var page = Read("Index.cshtml");
        var profileEditor = Read("_InstitutionalProfileEditor.cshtml");
        var additionalSlideCatalog = Read("ProjectBriefingAdditionalSlideCatalog.cs");
        var pageModel = Read("Index.cshtml.cs");
        var deckService = Read("ProjectBriefingDeckService.cs");
        var script = Read("project-briefing-decks.js");
        var service = Read("ProjectBriefingInstitutionalProfileService.cs");
        var composer = Read("ProjectBriefingSlideComposer.InstitutionalProfile.cs");
        var mainComposer = Read("ProjectBriefingSlideComposer.cs");
        var dataSource = Read("ProjectBriefingDataService.cs");

        Assert.Contains("Additional slides", page, StringComparison.Ordinal);
        Assert.Contains("SDD Institutional Profile", additionalSlideCatalog, StringComparison.Ordinal);
        Assert.Contains("@AdditionalSlideDisplayName(slideType)", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-profile-drawer", profileEditor, StringComparison.Ordinal);
        Assert.Contains("InstitutionalModules", profileEditor, StringComparison.Ordinal);
        Assert.Contains("data-pbd-institutional-history-editor", profileEditor, StringComparison.Ordinal);
        Assert.Contains("data-pbd-institutional-partnership-editor", profileEditor, StringComparison.Ordinal);
        Assert.Contains("InstitutionalProjectScope", profileEditor, StringComparison.Ordinal);
        Assert.Contains("Original completed projects — recommended", profileEditor, StringComparison.Ordinal);
        Assert.Contains("read-only and are taken from their authoritative PRISM modules", profileEditor, StringComparison.Ordinal);
        Assert.Contains("Profile footer strip", profileEditor, StringComparison.Ordinal);
        Assert.Contains("InstitutionalFooterStripText", profileEditor, StringComparison.Ordinal);
        Assert.Contains("Shown exactly as entered", profileEditor, StringComparison.Ordinal);
        Assert.Contains("validateInstitutionalProfile", script, StringComparison.Ordinal);
        Assert.Contains("syncInstitutionalModuleOrder", script, StringComparison.Ordinal);
        Assert.Contains("syncInstitutionalHistoryEditor", script, StringComparison.Ordinal);
        Assert.Contains("syncInstitutionalPartnershipEditor", script, StringComparison.Ordinal);
        Assert.Contains("syncInstitutionalLayoutSummary", script, StringComparison.Ordinal);
        Assert.Contains("data-pbd-additional-slides", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-profile-open", page, StringComparison.Ordinal);
        Assert.Contains("SaveInstitutionalProfile", profileEditor, StringComparison.Ordinal);
        Assert.Contains("openProfileDrawer", script, StringComparison.Ordinal);
        Assert.Contains("profileInitialState", script, StringComparison.Ordinal);
        Assert.Contains("OnPostSaveInstitutionalProfileAsync", pageModel, StringComparison.Ordinal);
        Assert.Contains("OnPostToggleInstitutionalProfileAsync", pageModel, StringComparison.Ordinal);
        Assert.Contains("InstitutionalProfileOptions = additionalSlides.InstitutionalProfileOptions", pageModel, StringComparison.Ordinal);
        Assert.Contains("AdditionalSlideOrder = additionalSlides.AdditionalSlideOrder", pageModel, StringComparison.Ordinal);
        Assert.Contains("UpdateInstitutionalProfileAsync", deckService, StringComparison.Ordinal);
        var profileUpdateStart = deckService.IndexOf("public async Task UpdateInstitutionalProfileAsync(", StringComparison.Ordinal);
        var profileUpdateEnd = deckService.IndexOf("public async Task UpdateRoleCharterAsync(", profileUpdateStart, StringComparison.Ordinal);
        Assert.True(profileUpdateStart >= 0 && profileUpdateEnd > profileUpdateStart);
        var profileUpdate = deckService[profileUpdateStart..profileUpdateEnd];
        Assert.Contains("ProjectBriefingDeckConfigurationCodec.WithAdditionalSlides", profileUpdate, StringComparison.Ordinal);
        Assert.Contains("normalized,", profileUpdate, StringComparison.Ordinal);
        Assert.Contains("configuration.RoleCharterOptions", profileUpdate, StringComparison.Ordinal);
        Assert.Contains("configuration.FfcGlobalFootprintOptions", profileUpdate, StringComparison.Ordinal);
        Assert.True(
            page.IndexOf("Content used by this presentation", StringComparison.Ordinal)
            < page.IndexOf("data-pbd-additional-slides", StringComparison.Ordinal)
            && page.IndexOf("data-pbd-additional-slides", StringComparison.Ordinal)
            < page.IndexOf("Projects in this deck", StringComparison.Ordinal),
            "Additional slides must remain visible between preflight and project management.");

        Assert.Contains("ProjectLifecycleStatus.Completed", service, StringComparison.Ordinal);
        Assert.Contains("!project.IsBuild", service, StringComparison.Ordinal);
        Assert.Contains("GetApprovedAggregatesAsync", service, StringComparison.Ordinal);
        Assert.Contains("TechnicalCategoryName", service, StringComparison.Ordinal);
        Assert.Contains("GetKpisAsync", service, StringComparison.Ordinal);
        Assert.Contains("FY {year.TrainingYearLabel}", service, StringComparison.Ordinal);
        Assert.Contains("Units /", service, StringComparison.Ordinal);
        Assert.Contains("IprRecords", service, StringComparison.Ordinal);
        Assert.DoesNotContain("515 ABW", service, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("RenderInstitutionalProfile", composer, StringComparison.Ordinal);
        Assert.Contains("RenderInstitutionalHistory", composer, StringComparison.Ordinal);
        Assert.Contains("RenderInstitutionalModules", composer, StringComparison.Ordinal);
        Assert.Contains("AddGroup", composer, StringComparison.Ordinal);
        Assert.Contains("SDD institutional history timeline", composer, StringComparison.Ordinal);
        Assert.Contains("institutional module", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("AddNativeTable", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeTableBorders", composer, StringComparison.Ordinal);
        Assert.Contains("InstitutionalModuleDisplayTitle", composer, StringComparison.Ordinal);
        Assert.Contains("module labels", composer, StringComparison.Ordinal);
        Assert.Contains("module values", composer, StringComparison.Ordinal);
        Assert.Contains("RenderInstitutionalFooterStrip", composer, StringComparison.Ordinal);
        Assert.Contains("Data as on", mainComposer, StringComparison.Ordinal);
        Assert.Contains("Source: PRISM ERP", mainComposer, StringComparison.Ordinal);
        Assert.Contains("InstitutionalProfileSlides", dataSource, StringComparison.Ordinal);
    }


    [Fact]
    public void FfcGlobalFootprintSlide_IsRegisteredErpBackedAndFixedBeforeClosing()
    {
        var page = Read("Index.cshtml");
        var editor = Read("_FfcGlobalFootprintEditor.cshtml");
        var pageModel = Read("Index.cshtml.cs");
        var catalog = Read("ProjectBriefingAdditionalSlideCatalog.cs");
        var dataService = Read("ProjectBriefingDataService.cs");
        var composer = Read("ProjectBriefingSlideComposer.FfcGlobalFootprint.cs");
        var mainComposer = Read("ProjectBriefingSlideComposer.cs");
        var script = Read("project-briefing-additional-slides.js");

        Assert.Contains("FfcGlobalFootprint", catalog, StringComparison.Ordinal);
        Assert.Contains("BeforeClosing", catalog, StringComparison.Ordinal);
        Assert.Contains("CanReorder: false", catalog, StringComparison.Ordinal);
        Assert.Contains("Opening slides", page, StringComparison.Ordinal);
        Assert.Contains("Before closing", page, StringComparison.Ordinal);
        Assert.Contains("pbd-additional-slide-card__placement", page, StringComparison.Ordinal);
        Assert.Contains("Each approved slide type can be added once", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-ffc-footprint-open", page, StringComparison.Ordinal);
        Assert.Contains("SaveFfcGlobalFootprint", editor, StringComparison.Ordinal);
        Assert.Contains("Live, read-only PRISM data", editor, StringComparison.Ordinal);
        Assert.Contains("FfcFootprintPreviewSummary", editor, StringComparison.Ordinal);
        Assert.Contains("FfcFootprintPreviewSummary", pageModel, StringComparison.Ordinal);
        Assert.Contains("OnPostSaveFfcGlobalFootprintAsync", pageModel, StringComparison.Ordinal);
        Assert.Contains("IFfcFootprintService", dataService, StringComparison.Ordinal);
        Assert.Contains("IFfcPresentationMapRenderer", dataService, StringComparison.Ordinal);
        Assert.Contains("RenderFocused", dataService, StringComparison.Ordinal);
        Assert.Contains("RenderFfcGlobalFootprint", composer, StringComparison.Ordinal);
        Assert.Contains("RenderFfcMapDominant", composer, StringComparison.Ordinal);
        Assert.Contains("RenderFfcCountryWiseBreakdownSlide", composer, StringComparison.Ordinal);
        Assert.Contains("IncludeCountryWiseBreakdown", editor, StringComparison.Ordinal);
        Assert.Contains("FfcGlobalFootprintLayout", editor, StringComparison.Ordinal);
        Assert.Contains("CountriesPerBreakdownSlide", mainComposer, StringComparison.Ordinal);
        Assert.Contains("Delivered, awaiting installation", composer, StringComparison.Ordinal);
        Assert.Contains("COUNTRY-WISE BREAKDOWN", composer, StringComparison.Ordinal);
        Assert.Contains("FfcMapWidth = 8.36", composer, StringComparison.Ordinal);
        Assert.Contains("FfcBreakdownWidth = 3.30", composer, StringComparison.Ordinal);
        Assert.Contains("2100, height: 1065", dataService, StringComparison.Ordinal);
        Assert.Contains("countries — recommended", editor, StringComparison.Ordinal);
        Assert.Contains("AddConcludingPlans", mainComposer, StringComparison.Ordinal);
        Assert.Contains("SlidePlanKind.FfcGlobalFootprint", mainComposer, StringComparison.Ordinal);
        Assert.Contains("confirmFfcDiscard", script, StringComparison.Ordinal);
        Assert.DoesNotContain("window.confirm", script, StringComparison.Ordinal);
    }

    private static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", fileName);
        Assert.True(File.Exists(path), $"Project briefing contract file was not copied to test output: {path}");
        return File.ReadAllText(path);
    }
}

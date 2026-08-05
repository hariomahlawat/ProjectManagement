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
        Assert.Contains("Status <small>external remark</small>", page, StringComparison.Ordinal);
        Assert.Contains("Cost (R&amp;D)", page, StringComparison.Ordinal);
        Assert.Contains("Generate PowerPoint", page, StringComparison.Ordinal);
        Assert.Contains("Project Update Review", page, StringComparison.Ordinal);
        Assert.Contains("Shared decks", page, StringComparison.Ordinal);
        Assert.Contains("Estimated deck size", page, StringComparison.Ordinal);
        Assert.Contains("chart and table", page, StringComparison.Ordinal);
    }


    [Fact]
    public void Builder_KeepsCompactPreflightAboveProjectsAndUsesTemplateAwareChecks()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");

        Assert.Contains("Projects in this deck", page, StringComparison.Ordinal);
        Assert.Contains("Content used by this presentation", page, StringComparison.Ordinal);
        Assert.Contains("Supporting project metadata", page, StringComparison.Ordinal);
        Assert.Contains("Missing content uses defined placeholders", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-selector-details", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-decks-toggle", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-pbd-metric=\"update-facts\"", page, StringComparison.Ordinal);
        Assert.Contains("syncPreflightRequirementVisibility", script, StringComparison.Ordinal);
        Assert.Contains("is-decks-collapsed", script, StringComparison.Ordinal);
        Assert.True(
            page.IndexOf("Deck preflight", StringComparison.Ordinal) < page.IndexOf("Projects in this deck", StringComparison.Ordinal),
            "Deck preflight must remain the compact decision checkpoint above project management.");
        Assert.Contains("data-pbd-settings-drawer", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-settings-dirty", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-standard-section", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-settings-collapsible", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-readiness-tip", page, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Readiness indicator order", page, StringComparison.Ordinal);
        Assert.Contains("projects have", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Review {totalPreflightGapCount}", page, StringComparison.Ordinal);
    }


    [Fact]
    public void Builder_UsesDrawerSettingsAndResponsiveWidthContainment()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var css = Read("project-briefing-decks.css");

        Assert.Contains("pbd-settings-drawer", page, StringComparison.Ordinal);
        Assert.Contains("Save settings", page, StringComparison.Ordinal);
        Assert.Contains("Unsaved settings", page, StringComparison.Ordinal);
        Assert.Contains("serializeSettings", script, StringComparison.Ordinal);
        Assert.Contains("beforeunload", script, StringComparison.Ordinal);
        Assert.Contains("confirmSettingsNavigation", script, StringComparison.Ordinal);
        Assert.Contains("restoreSettingsSectionState", script, StringComparison.Ordinal);
        Assert.Contains("initialiseReadinessTooltips", script, StringComparison.Ordinal);
        Assert.Contains("Save or discard settings before generating", script, StringComparison.Ordinal);
        Assert.Contains("contain: inline-size", css, StringComparison.Ordinal);
        Assert.Contains("overflow-x: clip", css, StringComparison.Ordinal);
        Assert.Contains("max-width: 1499px", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_UsesCompactTemplateAwareSettingsDrawerLayout()
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
        Assert.Contains(".pbd-settings-drawer .pbd-choice-cards--three", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains(".pbd-settings-drawer .pbd-theme-cards", css, StringComparison.Ordinal);
        Assert.Contains(".pbd-settings-drawer .pbd-branding-options", css, StringComparison.Ordinal);
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
    public void CostResolver_UsesL1ThenAonThenIpaAndKeepsProliferationSeparate()
    {
        var source = Read("ProjectBriefingCostResolver.cs");
        var l1 = source.IndexOf("l1.TryGetValue", StringComparison.Ordinal);
        var aon = source.IndexOf("aon.TryGetValue", StringComparison.Ordinal);
        var ipa = source.IndexOf("ipa.TryGetValue", StringComparison.Ordinal);

        Assert.True(l1 >= 0 && aon > l1 && ipa > aon, "Cost (R&D) resolution must remain L1 → AoN → IPA.");
        Assert.Contains("ResolveCostsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingResolvedCosts", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCostBasis.IPA", source, StringComparison.Ordinal);
        Assert.Contains("ProjectProductionCostFacts", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCostBasis.Proliferation", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalStatusResolver_UsesExternalGeneralRemarksOnly()
    {
        var source = Read("ProjectBriefingExternalStatusService.cs");

        Assert.Contains("remark.Type == RemarkType.External", source, StringComparison.Ordinal);
        Assert.Contains("remark.Scope == RemarkScope.General", source, StringComparison.Ordinal);
        Assert.Contains("!remark.IsDeleted", source, StringComparison.Ordinal);
        Assert.Contains("row.LastEditedAtUtc ?? row.CreatedAtUtc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemarkType.Internal", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectUpdateSheet_UsesApprovedTemplateAndAuthoritativeFieldSources()
    {
        var page = Read("Index.cshtml");
        var resolver = Read("ProjectBriefingUpdateSheetFactsResolver.cs");
        var composer = Read("ProjectBriefingSlideComposer.UpdateSheet.cs");

        Assert.Contains("Project Update Sheets", page, StringComparison.Ordinal);
        Assert.Contains("value=\"ProjectUpdateSheet\"", page, StringComparison.Ordinal);
        Assert.Contains("Information shown on each project sheet", page, StringComparison.Ordinal);
        Assert.Contains("Project name remains the slide title", page, StringComparison.Ordinal);
        Assert.Contains("Hide fields with no recorded value", page, StringComparison.Ordinal);
        Assert.Contains("Include cover slide", page, StringComparison.Ordinal);
        Assert.Contains("Include portfolio-summary slide", page, StringComparison.Ordinal);

        Assert.Contains("StageCodes.AON", resolver, StringComparison.Ordinal);
        Assert.Contains("StageCodes.DEVP", resolver, StringComparison.Ordinal);
        Assert.Contains("project.LeadPoUser.Rank", resolver, StringComparison.Ordinal);
        Assert.Contains("project.LeadPoUser.FullName", resolver, StringComparison.Ordinal);
        Assert.Contains("project.SponsoringLineDirectorate.Name", resolver, StringComparison.Ordinal);
        Assert.Contains("link.IndustryPartner.Name", resolver, StringComparison.Ordinal);
        Assert.Contains("ProjectSupplyOrderFacts", resolver, StringComparison.Ordinal);
        Assert.Contains("ArppPublishedEntries", resolver, StringComparison.Ordinal);

        Assert.Contains("RenderProjectUpdateSheet", composer, StringComparison.Ordinal);
        Assert.Contains("project.CostRd.DisplayValue", composer, StringComparison.Ordinal);
        Assert.Contains("UpdateSheetStatus(project.ExternalStatus)", composer, StringComparison.Ordinal);
        Assert.Contains("BRIEF OF THE PROJECT", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("PROJECT UPDATE SHEET", composer, StringComparison.Ordinal);
        Assert.Contains("AddProjectSlideHeader", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectSlideHeaderVariant.ProjectUpdateSheet", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateSheetAccent", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectUpdateLabelFill", composer, StringComparison.Ordinal);
        Assert.Contains("SO Date:", composer, StringComparison.Ordinal);
        Assert.Contains("Firm:", composer, StringComparison.Ordinal);
        Assert.Contains("string.Join(\"\\n\"", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("B5122B", composer, StringComparison.OrdinalIgnoreCase);
    }



    [Fact]
    public void ProjectUpdateSheet_PortfolioSummaryShowsSeparateRdAndAuthoritativeIpaTotals()
    {
        var dataSource = Read("ProjectBriefingDataService.cs");
        var composer = Read("ProjectBriefingSlideComposer.cs");

        Assert.Contains("TotalIpaCostInRupees", dataSource, StringComparison.Ordinal);
        Assert.Contains("IpaCostRecordedCount", dataSource, StringComparison.Ordinal);
        Assert.Contains("TOTAL R&D COST", composer, StringComparison.Ordinal);
        Assert.Contains("TOTAL IPA COST", composer, StringComparison.Ordinal);
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
        Assert.Contains("ResolveProjectUpdateRows", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectUpdateSheetLayoutVariant.Compact", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectUpdateSheetLayoutVariant.Standard", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectUpdateSheetLayoutVariant.Detailed", composer, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", composer, StringComparison.Ordinal);
        Assert.Contains("Completion Status", composer, StringComparison.Ordinal);
        Assert.Contains("Project completed", composer, StringComparison.Ordinal);
        Assert.Contains("StageCodes.DEVP", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingNarrativeTypography.ResolveUpdateSheetBrief", composer, StringComparison.Ordinal);
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

        Assert.Contains("ProjectBriefingStageOrder.BuildSummary", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingProjectOrdering.OrderProjects", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingStageOrder.Resolve", dataSource, StringComparison.Ordinal);
        Assert.Contains("OrderProjects(data.Projects)", composer, StringComparison.Ordinal);
        Assert.Contains("AddStageSummarySlides", composer, StringComparison.Ordinal);
        Assert.Contains("RenderStageSummaryTable", composer, StringComparison.Ordinal);
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
        Assert.DoesNotContain("PROJECT POSITION", composer, StringComparison.Ordinal);
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
        Assert.Contains("ShowPresentStatus", script, StringComparison.Ordinal);
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
        var dataSource = Read("ProjectBriefingDataService.cs");

        Assert.DoesNotContain("STATUS MISSING", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("DATA READINESS", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerPoint-ready photo", composer, StringComparison.Ordinal);
        Assert.Contains("Available for {recorded} of {total} selected projects", composer, StringComparison.Ordinal);
        Assert.Contains("CapabilityContinuationSlides", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCapabilityPaginator", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingTextNormalizer.NormalizeFull", dataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectBriefingTextNormalizer.Normalize(", dataSource, StringComparison.Ordinal);
        Assert.Contains("ExecutiveStatus", composer, StringComparison.Ordinal);
        Assert.Contains("includeBasis: false", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_ManagesMembershipWithoutFullPageReloadAndSearchesSelectedProjects()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var service = Read("ProjectBriefingDeckService.cs");

        Assert.Contains("data-membership-url", page, StringComparison.Ordinal);
        Assert.Contains("Search within this deck", page, StringComparison.Ordinal);
        Assert.Contains("In this deck", page, StringComparison.Ordinal);
        Assert.Contains("Not in this deck", page, StringComparison.Ordinal);
        Assert.Contains("Apply changes", page, StringComparison.Ordinal);
        Assert.Contains("updateMembership", script, StringComparison.Ordinal);
        Assert.Contains("sessionStorage", script, StringComparison.Ordinal);
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
        Assert.Contains("data-pbd-clear-selected-filters", page, StringComparison.Ordinal);
        Assert.Contains("revealFirstFilterMatch", script, StringComparison.Ordinal);
        Assert.Contains("matching ${noun}", script, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingThemeCatalog.Resolve", composer, StringComparison.Ordinal);
        Assert.Contains("AddBrandingImages", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("SIMULATOR DEVELOPMENT DIVISION · PRISM ERP", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_KeepsCanonicalStageGroupsAndReordersOnlyWithinAStage()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var composer = Read("ProjectBriefingSlideComposer.cs");

        Assert.Contains("Projects are grouped by present stage in maturity order", page, StringComparison.Ordinal);
        Assert.Contains("Top of stage", page, StringComparison.Ordinal);
        Assert.Contains("data-stage-order", page, StringComparison.Ordinal);
        Assert.Contains("row.dataset.stageCode === stage", script, StringComparison.Ordinal);
        Assert.Contains("sameStage", script, StringComparison.Ordinal);
        Assert.Contains("Projects remain grouped by maturity", script, StringComparison.Ordinal);
        Assert.Contains("NativeTableHorizontalMargin = .11", composer, StringComparison.Ordinal);
        Assert.Contains("NativeTableVerticalMargin = .04", composer, StringComparison.Ordinal);
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
        var pageModel = Read("Index.cshtml.cs");
        var deckService = Read("ProjectBriefingDeckService.cs");
        var script = Read("project-briefing-decks.js");
        var service = Read("ProjectBriefingInstitutionalProfileService.cs");
        var composer = Read("ProjectBriefingSlideComposer.InstitutionalProfile.cs");
        var mainComposer = Read("ProjectBriefingSlideComposer.cs");
        var dataSource = Read("ProjectBriefingDataService.cs");

        Assert.Contains("Additional slides", page, StringComparison.Ordinal);
        Assert.Contains("SDD Institutional Profile", page, StringComparison.Ordinal);
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
        Assert.Contains("preservedProfile", pageModel, StringComparison.Ordinal);
        Assert.Contains("UpdateInstitutionalProfileAsync", deckService, StringComparison.Ordinal);
        Assert.Contains("WithInstitutionalProfileOptions", deckService, StringComparison.Ordinal);
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
        Assert.Contains("Immediately before closing", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-ffc-footprint-open", page, StringComparison.Ordinal);
        Assert.Contains("SaveFfcGlobalFootprint", editor, StringComparison.Ordinal);
        Assert.Contains("Live, read-only PRISM data", editor, StringComparison.Ordinal);
        Assert.Contains("OnPostSaveFfcGlobalFootprintAsync", pageModel, StringComparison.Ordinal);
        Assert.Contains("IFfcFootprintService", dataService, StringComparison.Ordinal);
        Assert.Contains("IFfcPresentationMapRenderer", dataService, StringComparison.Ordinal);
        Assert.Contains("RenderFfcGlobalFootprint", composer, StringComparison.Ordinal);
        Assert.Contains("Delivered, awaiting installation", composer, StringComparison.Ordinal);
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

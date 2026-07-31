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
        Assert.Contains("aria-label="Readiness indicator order", page, StringComparison.Ordinal);
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
        Assert.Contains("Project Cost is always the authoritative R&amp;D cost", page, StringComparison.Ordinal);
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
        Assert.Contains("AddBrandingImages(HeaderVariant.ProjectUpdateSheet)", composer, StringComparison.Ordinal);
        Assert.Contains("Align: \"ctr\"", composer, StringComparison.Ordinal);
        Assert.Contains("UpdateSheetAccent", composer, StringComparison.Ordinal);
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
        Assert.Contains("Header branding", page, StringComparison.Ordinal);
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

    private static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", fileName);
        Assert.True(File.Exists(path), $"Project briefing contract file was not copied to test output: {path}");
        return File.ReadAllText(path);
    }
}

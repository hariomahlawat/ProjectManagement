using System.Text.Json.Nodes;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingDeckConfigurationCodecTests
{
    [Fact]
    public void Read_LegacySelectionRules_PreservesRulesAndUsesRecommendedRows()
    {
        const string legacy = "{\"kind\":\"Ongoing\",\"projectIds\":[1,2]}";

        var configuration = ProjectBriefingDeckConfigurationCodec.Read(legacy);

        Assert.Equal(legacy, configuration.SelectionRulesJson);
        Assert.Equal(ProjectBriefingUpdateSheetOptions.RecommendedRows, configuration.UpdateSheetOptions.Rows);
        Assert.False(configuration.UpdateSheetOptions.HideEmptyValues);
        Assert.Equal(ProjectBriefingProjectBriefLayout.Automatic, configuration.StandardSlideOptions.ProjectBriefLayout);
        Assert.True(configuration.StandardSlideOptions.ShowPresentStage);
        Assert.True(configuration.StandardSlideOptions.ShowPresentStatus);
        Assert.False(configuration.StandardSlideOptions.IncludeStageDistributionTable);
        Assert.Equal(ProjectBriefingClosingSlideType.JaiHind, configuration.ClosingSlideType);
        Assert.False(configuration.InstitutionalProfileOptions.IncludeSlide);
        Assert.Equal(
            ProjectBriefingInstitutionalProfileOptions.DefaultModules,
            configuration.InstitutionalProfileOptions.Modules);
    }

    [Fact]
    public void RecommendedRows_AreCompactSubsetOfAllAvailableRows()
    {
        Assert.Equal(9, ProjectBriefingUpdateSheetOptions.AllRows.Count);
        Assert.Equal(
            new[]
            {
                ProjectBriefingUpdateSheetRow.ProjectCost,
                ProjectBriefingUpdateSheetRow.AonDate,
                ProjectBriefingUpdateSheetRow.SupplyOrder,
                ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus,
                ProjectBriefingUpdateSheetRow.PresentStatus
            },
            ProjectBriefingUpdateSheetOptions.RecommendedRows);
        Assert.All(
            ProjectBriefingUpdateSheetOptions.RecommendedRows,
            row => Assert.Contains(row, ProjectBriefingUpdateSheetOptions.AllRows));
    }

    [Fact]
    public void WithUpdateSheetOptions_PreservesSelectionRulesAndRowOrder()
    {
        const string legacy = "{\"kind\":\"TechnicalCategory\",\"technicalCategoryIds\":[4]}";
        var options = new ProjectBriefingUpdateSheetOptions(
            new[]
            {
                ProjectBriefingUpdateSheetRow.PresentStatus,
                ProjectBriefingUpdateSheetRow.ProjectCost,
                ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus
            },
            HideEmptyValues: true);

        var encoded = ProjectBriefingDeckConfigurationCodec.WithUpdateSheetOptions(legacy, options);
        var decoded = ProjectBriefingDeckConfigurationCodec.Read(encoded);

        Assert.Equal(JsonNode.Parse(legacy)?.ToJsonString(), decoded.SelectionRulesJson);
        Assert.Equal(options.Rows, decoded.UpdateSheetOptions.Rows);
        Assert.True(decoded.UpdateSheetOptions.HideEmptyValues);
    }

    [Fact]
    public void WithSelectionRules_PreservesUpdateSheetPreferences()
    {
        var initial = ProjectBriefingDeckConfigurationCodec.WithUpdateSheetOptions(
            null,
            new ProjectBriefingUpdateSheetOptions(
                new[]
                {
                    ProjectBriefingUpdateSheetRow.LineDirectorate,
                    ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus
                },
                HideEmptyValues: true));

        var encoded = ProjectBriefingDeckConfigurationCodec.WithSelectionRules(
            initial,
            "{\"kind\":\"IndividualProjects\",\"projectIds\":[9]}" );
        var decoded = ProjectBriefingDeckConfigurationCodec.Read(encoded);

        Assert.Equal(
            new[]
            {
                ProjectBriefingUpdateSheetRow.LineDirectorate,
                ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus
            },
            decoded.UpdateSheetOptions.Rows);
        Assert.True(decoded.UpdateSheetOptions.HideEmptyValues);
        Assert.Contains("IndividualProjects", decoded.SelectionRulesJson, StringComparison.Ordinal);
    }

    [Fact]
    public void WithStandardSlideOptions_PreservesSelectionAndUpdateSheetPreferences()
    {
        var initial = ProjectBriefingDeckConfigurationCodec.WithUpdateSheetOptions(
            "{\"kind\":\"Ongoing\"}",
            new ProjectBriefingUpdateSheetOptions(
                new[] { ProjectBriefingUpdateSheetRow.ProjectCost },
                HideEmptyValues: true));

        var encoded = ProjectBriefingDeckConfigurationCodec.WithStandardSlideOptions(
            initial,
            new ProjectBriefingStandardSlideOptions(
                ProjectBriefingProjectBriefLayout.PhotoEmphasis,
                ShowPresentStage: false,
                ShowPresentStatus: true,
                IncludeStageDistributionTable: true));
        var decoded = ProjectBriefingDeckConfigurationCodec.Read(encoded);

        Assert.Contains("Ongoing", decoded.SelectionRulesJson, StringComparison.Ordinal);
        Assert.Equal(new[] { ProjectBriefingUpdateSheetRow.ProjectCost }, decoded.UpdateSheetOptions.Rows);
        Assert.True(decoded.UpdateSheetOptions.HideEmptyValues);
        Assert.Equal(ProjectBriefingProjectBriefLayout.PhotoEmphasis, decoded.StandardSlideOptions.ProjectBriefLayout);
        Assert.False(decoded.StandardSlideOptions.ShowPresentStage);
        Assert.True(decoded.StandardSlideOptions.ShowPresentStatus);
        Assert.True(decoded.StandardSlideOptions.IncludeStageDistributionTable);
    }

    [Fact]
    public void WithSelectionRules_PreservesStandardSlidePreferences()
    {
        var initial = ProjectBriefingDeckConfigurationCodec.WithStandardSlideOptions(
            null,
            new ProjectBriefingStandardSlideOptions(
                ProjectBriefingProjectBriefLayout.Standard,
                ShowPresentStage: true,
                ShowPresentStatus: false,
                IncludeStageDistributionTable: false));

        var encoded = ProjectBriefingDeckConfigurationCodec.WithSelectionRules(
            initial,
            "{\"kind\":\"IndividualProjects\",\"projectIds\":[4]}" );
        var decoded = ProjectBriefingDeckConfigurationCodec.Read(encoded);

        Assert.Equal(ProjectBriefingProjectBriefLayout.Standard, decoded.StandardSlideOptions.ProjectBriefLayout);
        Assert.True(decoded.StandardSlideOptions.ShowPresentStage);
        Assert.False(decoded.StandardSlideOptions.ShowPresentStatus);
    }

    [Fact]
    public void WithPresentationOptions_PersistsSelectedClosingSlideAndPreservesOtherConfiguration()
    {
        var initial = ProjectBriefingDeckConfigurationCodec.WithSelectionRules(
            null,
            "{\"kind\":\"Ongoing\"}");

        var encoded = ProjectBriefingDeckConfigurationCodec.WithPresentationOptions(
            initial,
            new ProjectBriefingUpdateSheetOptions(
                new[] { ProjectBriefingUpdateSheetRow.ProjectCost },
                HideEmptyValues: true),
            new ProjectBriefingStandardSlideOptions(
                ProjectBriefingProjectBriefLayout.Standard,
                ShowPresentStage: true,
                ShowPresentStatus: false,
                IncludeStageDistributionTable: true),
            ProjectBriefingClosingSlideType.ThankYou);
        var decoded = ProjectBriefingDeckConfigurationCodec.Read(encoded);

        Assert.Equal(ProjectBriefingClosingSlideType.ThankYou, decoded.ClosingSlideType);
        Assert.Contains("Ongoing", decoded.SelectionRulesJson, StringComparison.Ordinal);
        Assert.True(decoded.UpdateSheetOptions.HideEmptyValues);
        Assert.Equal(ProjectBriefingProjectBriefLayout.Standard, decoded.StandardSlideOptions.ProjectBriefLayout);
        Assert.False(decoded.StandardSlideOptions.ShowPresentStatus);
        Assert.True(decoded.StandardSlideOptions.IncludeStageDistributionTable);
    }

    [Fact]
    public void SelectionAndSlidePreferenceUpdates_PreserveClosingSlideChoice()
    {
        var initial = ProjectBriefingDeckConfigurationCodec.WithPresentationOptions(
            null,
            ProjectBriefingUpdateSheetOptions.Default,
            ProjectBriefingStandardSlideOptions.Default,
            ProjectBriefingClosingSlideType.ThankYou);

        var withSelection = ProjectBriefingDeckConfigurationCodec.WithSelectionRules(
            initial,
            "{\"kind\":\"IndividualProjects\",\"projectIds\":[7]}");
        var withRows = ProjectBriefingDeckConfigurationCodec.WithUpdateSheetOptions(
            withSelection,
            new ProjectBriefingUpdateSheetOptions(
                new[] { ProjectBriefingUpdateSheetRow.PresentStatus },
                HideEmptyValues: false));
        var withStandard = ProjectBriefingDeckConfigurationCodec.WithStandardSlideOptions(
            withRows,
            ProjectBriefingStandardSlideOptions.Default);

        Assert.Equal(
            ProjectBriefingClosingSlideType.ThankYou,
            ProjectBriefingDeckConfigurationCodec.Read(withStandard).ClosingSlideType);
    }


    [Fact]
    public void InstitutionalProfileOptions_RoundTripAuthorisedManualContentAndModuleOrder()
    {
        var options = ProjectBriefingInstitutionalProfileOptions.Normalize(
            includeSlide: true,
            title: "SDD – Growth over the years",
            includeHistory: true,
            historyMilestones: new[]
            {
                new ProjectBriefingInstitutionalHistoryMilestone(1991, "Raising & 1st PE"),
                new ProjectBriefingInstitutionalHistoryMilestone(1986, "Conceptualised at MCEME")
            },
            modules: new[]
            {
                ProjectBriefingInstitutionalProfileModule.Proliferation,
                ProjectBriefingInstitutionalProfileModule.ProjectsDeveloped,
                ProjectBriefingInstitutionalProfileModule.Partnerships
            },
            projectScope: ProjectBriefingInstitutionalProjectScope.OriginalCompleted,
            maximumDetailRows: 5,
            trainingHighlightTechnicalCategory: "AR/VR",
            partnershipEntries: new[] { "IIT Hyderabad", "Private industry / start-ups" },
            includeFooterStrip: true,
            footerStripText: "GOC-in-C Unit Citations",
            footerStripEmphasisValue: "03",
            footerStripStyle: ProjectBriefingInstitutionalFooterStyle.Outline,
            footerStripAlignment: ProjectBriefingInstitutionalFooterAlignment.Center);

        var encoded = ProjectBriefingDeckConfigurationCodec.WithInstitutionalProfileOptions(
            "{\"kind\":\"Ongoing\"}",
            options);
        var decoded = ProjectBriefingDeckConfigurationCodec.Read(encoded);

        Assert.True(decoded.InstitutionalProfileOptions.IncludeSlide);
        Assert.Equal(options.Modules, decoded.InstitutionalProfileOptions.Modules);
        Assert.Equal(options.HistoryMilestones, decoded.InstitutionalProfileOptions.HistoryMilestones);
        Assert.Equal(options.PartnershipEntries, decoded.InstitutionalProfileOptions.PartnershipEntries);
        Assert.Equal(ProjectBriefingInstitutionalProjectScope.OriginalCompleted, decoded.InstitutionalProfileOptions.ProjectScope);
        Assert.True(decoded.InstitutionalProfileOptions.IncludeFooterStrip);
        Assert.Equal("GOC-in-C Unit Citations", decoded.InstitutionalProfileOptions.FooterStripText);
        Assert.Equal("03", decoded.InstitutionalProfileOptions.FooterStripEmphasisValue);
        Assert.Equal(ProjectBriefingInstitutionalFooterStyle.Outline, decoded.InstitutionalProfileOptions.FooterStripStyle);
        Assert.Contains("Ongoing", decoded.SelectionRulesJson, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationPreferenceUpdates_PreserveInstitutionalProfileConfiguration()
    {
        var initial = ProjectBriefingDeckConfigurationCodec.WithInstitutionalProfileOptions(
            null,
            ProjectBriefingInstitutionalProfileOptions.Normalize(
                includeSlide: true,
                title: null,
                includeHistory: false,
                historyMilestones: Array.Empty<ProjectBriefingInstitutionalHistoryMilestone>(),
                modules: new[] { ProjectBriefingInstitutionalProfileModule.IntellectualProperty },
                projectScope: ProjectBriefingInstitutionalProjectScope.AllCompletedIncludingRebuilds,
                maximumDetailRows: 4,
                trainingHighlightTechnicalCategory: null,
                partnershipEntries: Array.Empty<string>(),
                includeFooterStrip: false,
                footerStripText: null,
                footerStripEmphasisValue: null,
                footerStripStyle: ProjectBriefingInstitutionalFooterStyle.Outline,
                footerStripAlignment: ProjectBriefingInstitutionalFooterAlignment.Center));

        var updated = ProjectBriefingDeckConfigurationCodec.WithPresentationOptions(
            initial,
            ProjectBriefingUpdateSheetOptions.Default,
            ProjectBriefingStandardSlideOptions.Default,
            ProjectBriefingClosingSlideType.ThankYou);
        var decoded = ProjectBriefingDeckConfigurationCodec.Read(updated);

        Assert.True(decoded.InstitutionalProfileOptions.IncludeSlide);
        Assert.False(decoded.InstitutionalProfileOptions.IncludeHistory);
        Assert.Equal(ProjectBriefingInstitutionalProjectScope.AllCompletedIncludingRebuilds, decoded.InstitutionalProfileOptions.ProjectScope);
        Assert.Equal(
            new[] { ProjectBriefingInstitutionalProfileModule.IntellectualProperty },
            decoded.InstitutionalProfileOptions.Modules);
    }

    [Fact]
    public void InstitutionalProfileDefaults_ExcludeRebuildProjects()
    {
        var decoded = ProjectBriefingDeckConfigurationCodec.Read(null);

        Assert.Equal(
            ProjectBriefingInstitutionalProjectScope.OriginalCompleted,
            decoded.InstitutionalProfileOptions.ProjectScope);
    }

    [Fact]
    public void Normalize_RemovesDuplicatesAndInvalidEnumValues()
    {
        var options = ProjectBriefingUpdateSheetOptions.Normalize(
            new[]
            {
                ProjectBriefingUpdateSheetRow.ProjectCost,
                ProjectBriefingUpdateSheetRow.ProjectCost,
                (ProjectBriefingUpdateSheetRow)999,
                ProjectBriefingUpdateSheetRow.PresentStatus
            },
            hideEmptyValues: false);

        Assert.Equal(
            new[]
            {
                ProjectBriefingUpdateSheetRow.ProjectCost,
                ProjectBriefingUpdateSheetRow.PresentStatus
            },
            options.Rows);
    }
    [Fact]
    public void InstitutionalProfileOptions_ReadsLegacyUnitCitationConfigurationAsFooterStrip()
    {
        const string legacy = """
        {"schema":"prism.projectBriefing.deckConfig.v1","institutionalProfile":{"includeSlide":true,"includeUnitCitations":true,"unitCitationLabel":"GOC-in-C Unit Citations","unitCitationCount":3}}
        """;

        var decoded = ProjectBriefingDeckConfigurationCodec.Read(legacy);

        Assert.True(decoded.InstitutionalProfileOptions.IncludeFooterStrip);
        Assert.Equal("GOC-in-C Unit Citations", decoded.InstitutionalProfileOptions.FooterStripText);
        Assert.Equal("03", decoded.InstitutionalProfileOptions.FooterStripEmphasisValue);
    }

    [Fact]
    public void RoleCharterAndAdditionalSlideOrder_RoundTripWithoutLosingSharedOrCustomContent()
    {
        var roleCharter = ProjectBriefingRoleCharterOptions.Normalize(
            includeSlide: true,
            title: "Role & Charter",
            layout: ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter,
            useSharedContent: false,
            roleStatements: new[]
            {
                new ProjectBriefingRoleCharterEntry("Nodal Centre", "Development of specified simulators")
            },
            charterItems: new[]
            {
                new ProjectBriefingRoleCharterEntry("Repository", "Information related to simulators and AI"),
                new ProjectBriefingRoleCharterEntry("Facilitator", "QR, feasibility study and scope of work")
            });
        var profile = ProjectBriefingInstitutionalProfileOptions.Default;

        var encoded = ProjectBriefingDeckConfigurationCodec.WithAdditionalSlides(
            null,
            profile,
            roleCharter,
            ProjectBriefingFfcGlobalFootprintOptions.Default,
            new[]
            {
                ProjectBriefingAdditionalSlideType.RoleAndCharter,
                ProjectBriefingAdditionalSlideType.InstitutionalProfile
            });
        var decoded = ProjectBriefingDeckConfigurationCodec.Read(encoded);

        Assert.True(decoded.RoleCharterOptions.IncludeSlide);
        Assert.False(decoded.RoleCharterOptions.UseSharedContent);
        Assert.Equal(roleCharter.RoleStatements, decoded.RoleCharterOptions.RoleStatements);
        Assert.Equal(roleCharter.CharterItems, decoded.RoleCharterOptions.CharterItems);
        Assert.Equal(
            new[]
            {
                ProjectBriefingAdditionalSlideType.RoleAndCharter,
                ProjectBriefingAdditionalSlideType.InstitutionalProfile
            },
            decoded.AdditionalSlideOrder);
    }


    [Fact]
    public void FfcGlobalFootprint_RoundTripsAndRemainsInFixedConcludingPlacement()
    {
        var footprint = ProjectBriefingFfcGlobalFootprintOptions.Normalize(
            includeSlide: true,
            title: "Military Diplomacy — Global Footprint",
            layout: ProjectBriefingFfcGlobalFootprintLayout.MapDominant,
            maximumCountryRows: 9,
            includeCountryWiseBreakdown: true);

        var encoded = ProjectBriefingDeckConfigurationCodec.WithAdditionalSlides(
            null,
            ProjectBriefingInstitutionalProfileOptions.Default,
            ProjectBriefingRoleCharterOptions.Default,
            footprint,
            new[]
            {
                ProjectBriefingAdditionalSlideType.FfcGlobalFootprint,
                ProjectBriefingAdditionalSlideType.RoleAndCharter,
                ProjectBriefingAdditionalSlideType.InstitutionalProfile
            });
        var decoded = ProjectBriefingDeckConfigurationCodec.Read(encoded);

        Assert.True(decoded.FfcGlobalFootprintOptions.IncludeSlide);
        Assert.Equal("Military Diplomacy — Global Footprint", decoded.FfcGlobalFootprintOptions.Title);
        Assert.Equal(ProjectBriefingFfcGlobalFootprintLayout.MapDominant, decoded.FfcGlobalFootprintOptions.Layout);
        Assert.Equal(9, decoded.FfcGlobalFootprintOptions.MaximumCountryRows);
        Assert.True(decoded.FfcGlobalFootprintOptions.IncludeCountryWiseBreakdown);
        Assert.Equal(3, decoded.FfcGlobalFootprintOptions.EstimateSlideCount(13));
        Assert.Equal(ProjectBriefingAdditionalSlideType.FfcGlobalFootprint, decoded.AdditionalSlideOrder[^1]);
        Assert.Equal(
            new[]
            {
                ProjectBriefingAdditionalSlideType.RoleAndCharter,
                ProjectBriefingAdditionalSlideType.InstitutionalProfile,
                ProjectBriefingAdditionalSlideType.FfcGlobalFootprint
            },
            decoded.AdditionalSlideOrder);
    }

    [Fact]
    public void AdditionalSlideOrder_ExplicitEmptyArrayRemainsEmptyWhileLegacyJsonGetsProfileCompatibility()
    {
        var legacy = ProjectBriefingDeckConfigurationCodec.Read("{\"kind\":\"Ongoing\"}");
        Assert.Equal(
            new[] { ProjectBriefingAdditionalSlideType.InstitutionalProfile },
            legacy.AdditionalSlideOrder);

        var encoded = ProjectBriefingDeckConfigurationCodec.WithAdditionalSlideOrder(
            null,
            Array.Empty<ProjectBriefingAdditionalSlideType>());
        var decoded = ProjectBriefingDeckConfigurationCodec.Read(encoded);

        Assert.Empty(decoded.AdditionalSlideOrder);
    }

    [Fact]
    public void RoleCharterLegacyDefault_UpgradesToMilitaryDiplomacyWithoutRewritingCustomVariants()
    {
        const string legacy = """
        {
          "schema": "prism.projectBriefing.deckConfig.v1",
          "roleCharter": {
            "includeSlide": true,
            "title": "Role & Charter",
            "layout": "RoleAndTwoColumnCharter",
            "useSharedContent": false,
            "roleStatements": [],
            "charterItems": [
              {
                "leadPhrase": "Development support",
                "text": "Develop simulators and projects for FFCs"
              },
              {
                "leadPhrase": "Development support",
                "text": "Custom deck-specific wording"
              }
            ]
          },
          "additionalSlides": ["RoleAndCharter"]
        }
        """;

        var decoded = ProjectBriefingDeckConfigurationCodec.Read(legacy);

        Assert.Contains(
            decoded.RoleCharterOptions.CharterItems,
            item => item.LeadPhrase == "Military Diplomacy"
                && item.Text == "Develop simulators and projects for Friendly Foreign Countries (FFCs)");
        Assert.Contains(
            decoded.RoleCharterOptions.CharterItems,
            item => item.LeadPhrase == "Development support"
                && item.Text == "Custom deck-specific wording");
        Assert.DoesNotContain(
            decoded.RoleCharterOptions.CharterItems,
            item => item.LeadPhrase == "Development support"
                && item.Text == "Develop simulators and projects for FFCs");
    }

    [Fact]
    public void RoleCharterCustomContent_DoesNotSilentlyFallBackToSharedAuthorisedContent()
    {
        var options = ProjectBriefingRoleCharterOptions.Normalize(
            includeSlide: true,
            title: "Role & Charter",
            layout: ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter,
            useSharedContent: false,
            roleStatements: Array.Empty<ProjectBriefingRoleCharterEntry>(),
            charterItems: Array.Empty<ProjectBriefingRoleCharterEntry>());

        Assert.False(options.UseSharedContent);
        Assert.Empty(options.RoleStatements);
        Assert.Empty(options.CharterItems);
    }

}

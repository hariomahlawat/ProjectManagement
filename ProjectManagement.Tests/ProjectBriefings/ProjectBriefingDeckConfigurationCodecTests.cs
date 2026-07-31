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
}

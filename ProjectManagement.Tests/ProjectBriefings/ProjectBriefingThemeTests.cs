using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingThemeTests
{
    [Theory]
    [InlineData(ProjectBriefingPresentationTheme.EditorialLight, false, "F7F7F5")]
    [InlineData(ProjectBriefingPresentationTheme.GraphiteDark, true, "15181E")]
    public void ThemeCatalog_ProvidesCompleteSemanticPalette(
        ProjectBriefingPresentationTheme theme,
        bool isDark,
        string expectedCanvas)
    {
        var palette = ProjectBriefingThemeCatalog.Resolve(theme);

        Assert.Equal(isDark, palette.IsDark);
        Assert.Equal(expectedCanvas, palette.Canvas);
        Assert.All(
            palette.GetType().GetProperties()
                .Where(property => property.PropertyType == typeof(string))
                .Select(property => (string?)property.GetValue(palette)),
            value => Assert.Matches("^[0-9A-F]{6}$", Assert.IsType<string>(value)));
    }

    [Theory]
    [InlineData(ProjectBriefingPresentationTheme.EditorialLight, "315FA8", "2D7F82", "8F0D21", "EDF1F6")]
    [InlineData(ProjectBriefingPresentationTheme.GraphiteDark, "5B7CFA", "4FA6A8", "5B7CFA", "242A34")]
    public void ThemeCatalog_ProvidesSemanticOperationalNarrativeAndUpdateSheetRoles(
        ProjectBriefingPresentationTheme theme,
        string operationalAccent,
        string narrativeAccent,
        string updateSheetAccent,
        string updateSheetLabelFill)
    {
        var palette = ProjectBriefingThemeCatalog.Resolve(theme);

        Assert.Equal(operationalAccent, palette.OperationalAccent);
        Assert.Equal(narrativeAccent, palette.NarrativeAccent);
        Assert.Equal(updateSheetAccent, palette.ProjectUpdateAccent);
        Assert.Equal(updateSheetLabelFill, palette.ProjectUpdateLabelFill);
    }

}

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
}

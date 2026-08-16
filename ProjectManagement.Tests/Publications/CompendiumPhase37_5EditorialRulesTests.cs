using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase37_5EditorialRulesTests
{
    [Fact]
    public void AdditionalNoteMeasurementRetainsEditorialReserve()
    {
        var height = CompendiumDossierPaginationPlanner.MeasureAdditionalNoteHeightPoints(
            "A short closing publication note.",
            narrativeFontScale: 1f);

        Assert.True(height > 30f);
    }

    [Fact]
    public void TechnicalSpecificationMeasurementRetainsHeadingBreathingRoom()
    {
        var height = CompendiumDossierPaginationPlanner.MeasureTechnicalSpecificationsHeight(
            new[] { "High resolution optical sight with protected controls." },
            columns: 1);

        Assert.True(height > 30f);
    }
}

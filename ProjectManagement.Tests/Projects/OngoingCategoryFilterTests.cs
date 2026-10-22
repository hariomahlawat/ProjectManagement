using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class OngoingCategoryFilterTests
{
    // CoE (1) › CoE Short Term (2), CoE Mid Term (3); DCD Projects (4); Simulators (5, added in Admin) › Naval (6).
    private static readonly OngoingCategoryNode[] Categories =
    {
        new(1, "CoE", null),
        new(2, "CoE Short Term", 1),
        new(3, "CoE Mid Term", 1),
        new(4, "DCD Projects", null),
        new(5, "Simulators", null),
        new(6, "Naval", 5)
    };

    [Fact]
    public void Chips_KeepOnlyKnownTopLevelCategories()
    {
        var selection = OngoingCategoryFilter.Resolve(Categories, new[] { 4, 1, 2, 99 }, null, null);

        Assert.Equal(new[] { 1, 4 }, selection.TopLevelIds);
        Assert.Null(selection.SubCategoryId);
        Assert.Equal(new[] { 1, 4 }, selection.EffectiveIds);
    }

    [Fact]
    public void SubCategory_NarrowsWithinTheSelectedChips()
    {
        var selection = OngoingCategoryFilter.Resolve(Categories, new[] { 1 }, null, 2);

        Assert.Equal(new[] { 1 }, selection.TopLevelIds);
        Assert.Equal(2, selection.SubCategoryId);
        Assert.Equal(new[] { 2 }, selection.EffectiveIds);
    }

    [Fact]
    public void SubCategory_OutsideTheSelectedChips_IsDropped()
    {
        var selection = OngoingCategoryFilter.Resolve(Categories, new[] { 4 }, null, 2);

        Assert.Null(selection.SubCategoryId);
        Assert.Equal(new[] { 4 }, selection.EffectiveIds);
    }

    [Fact]
    public void SubCategory_WithNoChips_StandsAlone()
    {
        var selection = OngoingCategoryFilter.Resolve(Categories, Array.Empty<int>(), null, 6);

        Assert.Empty(selection.TopLevelIds);
        Assert.Equal(new[] { 6 }, selection.EffectiveIds);
    }

    [Theory]
    [InlineData(1, new[] { 1 }, null)]
    [InlineData(2, new int[0], 2)]
    public void LegacyCategory_IsAChipWhenTopLevel_ElseASubCategory(int legacyId, int[] expectedChips, int? expectedSub)
    {
        var selection = OngoingCategoryFilter.Resolve(Categories, Array.Empty<int>(), legacyId, null);

        Assert.Equal(expectedChips, selection.TopLevelIds);
        Assert.Equal(expectedSub, selection.SubCategoryId);
        Assert.Equal(new[] { legacyId }, selection.EffectiveIds);
    }

    [Fact]
    public void SubCategoryOptions_FollowTheSelectedChips_WithTheirPath()
    {
        var all = OngoingCategoryFilter.SubCategoryOptions(Categories, Array.Empty<int>());
        var underCoe = OngoingCategoryFilter.SubCategoryOptions(Categories, new[] { 1 });

        Assert.Equal(new[] { "CoE › CoE Mid Term", "CoE › CoE Short Term", "Simulators › Naval" }, all.Select(option => option.Label));
        Assert.Equal(new[] { 3, 2 }, underCoe.Select(option => option.Id));
    }

    [Theory]
    [InlineData("DCD Projects", "DCD")]
    [InlineData("Other R&D Projects", "Other R&D")]
    [InlineData("CoE", "CoE")]
    [InlineData("Projects", "Projects")]
    public void ChipLabel_DropsATrailingProjects(string name, string expected)
        => Assert.Equal(expected, OngoingCategoryFilter.ChipLabel(name));
}

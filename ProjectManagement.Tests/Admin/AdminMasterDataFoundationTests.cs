using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using Xunit;

namespace ProjectManagement.Tests.Admin;

public sealed class AdminMasterDataFoundationTests
{
    [Theory]
    [InlineData("  Air   Defence  ", "Air Defence")]
    [InlineData("Line\tDirectorate", "Line Directorate")]
    [InlineData("\r\nProject Type\r\n", "Project Type")]
    public void MasterDataName_Normalize_TrimsAndCollapsesWhitespace(string input, string expected)
    {
        Assert.Equal(expected, MasterDataName.Normalize(input));
    }

    [Fact]
    public void HierarchyValidation_RejectsSelfParent()
    {
        var relations = new[]
        {
            new AdminHierarchyValidationService.HierarchyRelation(1, null),
            new AdminHierarchyValidationService.HierarchyRelation(2, 1)
        };

        var result = AdminHierarchyValidationService.Validate(relations, 2, 2, "technical category");

        Assert.False(result.Succeeded);
        Assert.Equal("SelfParent", result.ErrorCode);
    }

    [Fact]
    public void HierarchyValidation_RejectsMovingUnderDescendant()
    {
        var relations = new[]
        {
            new AdminHierarchyValidationService.HierarchyRelation(1, null),
            new AdminHierarchyValidationService.HierarchyRelation(2, 1),
            new AdminHierarchyValidationService.HierarchyRelation(3, 2)
        };

        var result = AdminHierarchyValidationService.Validate(relations, 1, 3, "project category");

        Assert.False(result.Succeeded);
        Assert.Equal("DescendantParent", result.ErrorCode);
    }

    [Fact]
    public void HierarchyValidation_DetectsExistingCycle()
    {
        var relations = new[]
        {
            new AdminHierarchyValidationService.HierarchyRelation(1, 2),
            new AdminHierarchyValidationService.HierarchyRelation(2, 1)
        };

        var result = AdminHierarchyValidationService.Validate(relations, null, 1, "project category");

        Assert.False(result.Succeeded);
        Assert.Equal("HierarchyCycleDetected", result.ErrorCode);
    }

    [Fact]
    public void FlashMessageKeys_AreNamespacedAndUnique()
    {
        var keys = typeof(FlashMessageKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(field => Assert.IsType<string>(field.GetValue(null)))
            .ToArray();

        Assert.All(keys, key => Assert.Contains('.', key));
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("ok", keys);
        Assert.DoesNotContain("err", keys);
    }
}

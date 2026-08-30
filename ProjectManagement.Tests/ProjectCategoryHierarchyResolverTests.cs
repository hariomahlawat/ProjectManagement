using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectCategoryHierarchyResolverTests
{
    [Fact]
    public void ResolveRoot_WalksAllAncestorLevels()
    {
        var categories = new Dictionary<int, ProjectCategoryHierarchyNode>
        {
            [10] = new(10, "DCD Projects", null),
            [20] = new(20, "AR / VR", 10),
            [30] = new(30, "Medical Simulation", 20)
        };

        var root = ProjectCategoryHierarchyResolver.ResolveRoot(30, categories);

        Assert.NotNull(root);
        Assert.Equal(10, root.Value.Id);
        Assert.Equal("DCD Projects", root.Value.Name);
    }

    [Fact]
    public void ResolveRoot_KeepsNearestKnownNodeWhenParentReferenceIsMissing()
    {
        var categories = new Dictionary<int, ProjectCategoryHierarchyNode>
        {
            [30] = new(30, "Legacy Child", 999)
        };

        var root = ProjectCategoryHierarchyResolver.ResolveRoot(30, categories);

        Assert.NotNull(root);
        Assert.Equal(30, root.Value.Id);
    }

    [Fact]
    public void ResolveRoot_TerminatesSafelyWhenHierarchyContainsCycle()
    {
        var categories = new Dictionary<int, ProjectCategoryHierarchyNode>
        {
            [10] = new(10, "A", 20),
            [20] = new(20, "B", 10)
        };

        var root = ProjectCategoryHierarchyResolver.ResolveRoot(10, categories);

        Assert.NotNull(root);
        Assert.Contains(root.Value.Id, new[] { 10, 20 });
    }
}

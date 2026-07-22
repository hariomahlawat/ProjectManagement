using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Tests;

public sealed class TechnicalCategoryHierarchyTests
{
    [Fact]
    public async Task ValidatorRejectsMovingCategoryBelowDescendant()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var root = new TechnicalCategory { Name = "Root", CreatedByUserId = "admin" };
        db.TechnicalCategories.Add(root);
        await db.SaveChangesAsync();

        var child = new TechnicalCategory
        {
            Name = "Child",
            ParentId = root.Id,
            CreatedByUserId = "admin"
        };
        db.TechnicalCategories.Add(child);
        await db.SaveChangesAsync();

        var grandchild = new TechnicalCategory
        {
            Name = "Grandchild",
            ParentId = child.Id,
            CreatedByUserId = "admin"
        };
        db.TechnicalCategories.Add(grandchild);
        await db.SaveChangesAsync();

        var validator = new AdminHierarchyValidationService(db);

        var result = await validator.ValidateTechnicalCategoryParentAsync(
            root.Id,
            grandchild.Id,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("DescendantParent", result.ErrorCode);
        Assert.Contains(
            "descendants",
            result.UserMessage ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        var unchangedRoot = await db.TechnicalCategories
            .AsNoTracking()
            .SingleAsync(item => item.Id == root.Id);
        Assert.Null(unchangedRoot.ParentId);
    }
}

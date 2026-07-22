using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.Admin.Pages.TechnicalCategories;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Tests;

public sealed class TechnicalCategoryHierarchyTests
{
    [Fact]
    public async Task EditRejectsMovingCategoryBelowDescendant()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var root = new TechnicalCategory { Name = "Root", CreatedByUserId = "admin" };
        db.TechnicalCategories.Add(root);
        await db.SaveChangesAsync();
        var child = new TechnicalCategory { Name = "Child", ParentId = root.Id, CreatedByUserId = "admin" };
        db.TechnicalCategories.Add(child);
        await db.SaveChangesAsync();
        var grandchild = new TechnicalCategory { Name = "Grandchild", ParentId = child.Id, CreatedByUserId = "admin" };
        db.TechnicalCategories.Add(grandchild);
        await db.SaveChangesAsync();

        var page = new EditModel(db)
        {
            Input = new EditModel.InputModel
            {
                Id = root.Id,
                Name = root.Name,
                ParentId = grandchild.Id,
                IsActive = true
            }
        };

        var result = await page.OnPostAsync();

        Assert.False(page.ModelState.IsValid);
        Assert.Contains(
            page.ModelState["Input.ParentId"]!.Errors,
            error => error.ErrorMessage.Contains("descendants", StringComparison.OrdinalIgnoreCase));
        Assert.Same(root, await db.TechnicalCategories.FindAsync(root.Id));
        Assert.Null((await db.TechnicalCategories.FindAsync(root.Id))!.ParentId);
    }
}

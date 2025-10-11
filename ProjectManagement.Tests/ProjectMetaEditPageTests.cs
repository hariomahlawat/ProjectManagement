using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Pages.Projects.Meta;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectMetaEditPageTests
{
    [Fact]
    public async Task OnPostAsync_UnrelatedHod_ReturnsForbid()
    {
        await using var db = CreateContext();
        await db.Projects.AddAsync(new Project
        {
            Id = 1,
            Name = "Test",
            CreatedByUserId = "creator",
            HodUserId = "hod-assigned"
        });
        await db.SaveChangesAsync();

        var project = await db.Projects.SingleAsync(p => p.Id == 1);
        var userContext = new FakeUserContext("hod-other", isHoD: true);
        var page = CreatePage(db, userContext);
        page.Input = new EditModel.MetaEditInput
        {
            ProjectId = 1,
            Name = "Updated",
            RowVersion = Convert.ToBase64String(project.RowVersion)
        };

        var result = await page.OnPostAsync(1, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task OnPostAsync_AssignedHod_UpdatesProject()
    {
        await using var db = CreateContext();
        await db.ProjectCategories.AddAsync(new ProjectCategory
        {
            Id = 50,
            Name = "Infrastructure",
            IsActive = true
        });
        await db.TechnicalCategories.AddAsync(new TechnicalCategory
        {
            Id = 75,
            Name = "Networks",
            IsActive = true
        });

        await db.Projects.AddAsync(new Project
        {
            Id = 5,
            Name = "Original",
            CreatedByUserId = "creator",
            HodUserId = "hod-5"
        });
        await db.SaveChangesAsync();

        var project = await db.Projects.SingleAsync(p => p.Id == 5);
        var userContext = new FakeUserContext("hod-5", isHoD: true);
        var page = CreatePage(db, userContext);
        page.Input = new EditModel.MetaEditInput
        {
            ProjectId = 5,
            Name = "Updated Name",
            Description = "Desc",
            CategoryId = 50,
            TechnicalCategoryId = 75,
            RowVersion = Convert.ToBase64String(project.RowVersion)
        };

        var result = await page.OnPostAsync(5, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Projects/Overview", redirect.PageName);

        project = await db.Projects.SingleAsync(p => p.Id == 5);
        Assert.Equal("Updated Name", project.Name);
        Assert.Equal("Desc", project.Description);
        Assert.Equal(50, project.CategoryId);
        Assert.Equal(75, project.TechnicalCategoryId);
    }

    [Fact]
    public async Task OnPostAsync_AdminBypassesAssignment()
    {
        await using var db = CreateContext();
        await db.ProjectCategories.AddAsync(new ProjectCategory
        {
            Id = 25,
            Name = "Operations",
            IsActive = true
        });
        await db.TechnicalCategories.AddRangeAsync(
            new TechnicalCategory
            {
                Id = 85,
                Name = "Infrastructure",
                IsActive = true
            },
            new TechnicalCategory
            {
                Id = 86,
                Name = "Applications",
                IsActive = true
            });

        await db.Projects.AddAsync(new Project
        {
            Id = 8,
            Name = "Project",
            CreatedByUserId = "creator",
            HodUserId = "hod-8",
            CategoryId = 25,
            TechnicalCategoryId = 85
        });
        await db.SaveChangesAsync();

        var project = await db.Projects.SingleAsync(p => p.Id == 8);
        var userContext = new FakeUserContext("admin-user", isAdmin: true);
        var page = CreatePage(db, userContext);
        page.Input = new EditModel.MetaEditInput
        {
            ProjectId = 8,
            Name = "Admin Updated",
            CategoryId = 25,
            TechnicalCategoryId = 86,
            RowVersion = Convert.ToBase64String(project.RowVersion)
        };

        var result = await page.OnPostAsync(8, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        project = await db.Projects.SingleAsync(p => p.Id == 8);
        Assert.Equal("Admin Updated", project.Name);
        Assert.Equal(86, project.TechnicalCategoryId);
    }

    [Fact]
    public async Task OnPostAsync_DuplicateCaseFileNumber_ReturnsPageWithError()
    {
        await using var db = CreateContext();
        await db.Projects.AddRangeAsync(
            new Project
            {
                Id = 10,
                Name = "Existing",
                CaseFileNumber = "CF-999",
                CreatedByUserId = "creator",
                HodUserId = "hod-10"
            },
            new Project
            {
                Id = 11,
                Name = "Editable",
                CaseFileNumber = "CF-321",
                CreatedByUserId = "creator",
                HodUserId = "hod-owner"
            });
        await db.SaveChangesAsync();

        var project = await db.Projects.SingleAsync(p => p.Id == 11);
        var userContext = new FakeUserContext("hod-owner", isHoD: true);
        var page = CreatePage(db, userContext);
        page.Input = new EditModel.MetaEditInput
        {
            ProjectId = 11,
            Name = "Editable Updated",
            CaseFileNumber = "  CF-999  ",
            RowVersion = Convert.ToBase64String(project.RowVersion)
        };

        var result = await page.OnPostAsync(11, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(page.ModelState.TryGetValue("Input.CaseFileNumber", out var entry));
        var error = Assert.Single(entry!.Errors);
        Assert.Equal(ProjectValidationMessages.DuplicateCaseFileNumber, error.ErrorMessage);

        project = await db.Projects.SingleAsync(p => p.Id == 11);
        Assert.Equal("CF-321", project.CaseFileNumber);
    }

    [Fact]
    public async Task OnPostAsync_InactiveCategory_ReturnsError()
    {
        await using var db = CreateContext();
        await db.ProjectCategories.AddAsync(new ProjectCategory
        {
            Id = 90,
            Name = "Archived",
            IsActive = false
        });

        await db.Projects.AddAsync(new Project
        {
            Id = 21,
            Name = "Needs Category",
            CreatedByUserId = "creator",
            HodUserId = "hod-21"
        });
        await db.SaveChangesAsync();

        var project = await db.Projects.SingleAsync(p => p.Id == 21);
        var userContext = new FakeUserContext("hod-21", isHoD: true);
        var page = CreatePage(db, userContext);
        page.Input = new EditModel.MetaEditInput
        {
            ProjectId = 21,
            Name = "Needs Category",
            CategoryId = 90,
            RowVersion = Convert.ToBase64String(project.RowVersion)
        };

        var result = await page.OnPostAsync(21, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(page.ModelState.TryGetValue("Input.CategoryId", out var entry));
        var error = Assert.Single(entry!.Errors);
        Assert.Equal(ProjectValidationMessages.InactiveCategory, error.ErrorMessage);
    }

    [Fact]
    public async Task OnPostAsync_InactiveTechnicalCategory_ReturnsError()
    {
        await using var db = CreateContext();
        await db.TechnicalCategories.AddAsync(new TechnicalCategory
        {
            Id = 95,
            Name = "Legacy",
            IsActive = false
        });

        await db.Projects.AddAsync(new Project
        {
            Id = 22,
            Name = "Needs Tech Category",
            CreatedByUserId = "creator",
            HodUserId = "hod-22"
        });
        await db.SaveChangesAsync();

        var project = await db.Projects.SingleAsync(p => p.Id == 22);
        var userContext = new FakeUserContext("hod-22", isHoD: true);
        var page = CreatePage(db, userContext);
        page.Input = new EditModel.MetaEditInput
        {
            ProjectId = 22,
            Name = "Needs Tech Category",
            TechnicalCategoryId = 95,
            RowVersion = Convert.ToBase64String(project.RowVersion)
        };

        var result = await page.OnPostAsync(22, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(page.ModelState.TryGetValue("Input.TechnicalCategoryId", out var entry));
        var error = Assert.Single(entry!.Errors);
        Assert.Equal(ProjectValidationMessages.InactiveTechnicalCategory, error.ErrorMessage);
    }

    [Fact]
    public async Task OnPostAsync_ConcurrencyConflict_ReturnsPageWithError()
    {
        await using var db = CreateContext();
        await db.Projects.AddAsync(new Project
        {
            Id = 30,
            Name = "Original",
            CreatedByUserId = "creator",
            HodUserId = "hod-30"
        });
        await db.SaveChangesAsync();

        var project = await db.Projects.SingleAsync(p => p.Id == 30);
        var originalRowVersion = Convert.ToBase64String(project.RowVersion);

        project.Name = "Someone Else";
        await db.SaveChangesAsync();

        var userContext = new FakeUserContext("hod-30", isHoD: true);
        var page = CreatePage(db, userContext);
        page.Input = new EditModel.MetaEditInput
        {
            ProjectId = 30,
            Name = "My Update",
            RowVersion = originalRowVersion
        };

        var result = await page.OnPostAsync(30, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(page.ModelState.TryGetValue(string.Empty, out var entry));
        var error = Assert.Single(entry!.Errors);
        Assert.Contains("modified by someone else", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static EditModel CreatePage(ApplicationDbContext db, IUserContext userContext)
    {
        var page = new EditModel(db, userContext, new FakeAudit())
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            },
            TempData = new TempDataDictionary(
                new DefaultHttpContext(),
                new FakeTempDataProvider())
        };

        return page;
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object?> _data = new Dictionary<string, object?>();

        public IDictionary<string, object?> LoadTempData(HttpContext context)
        {
            var result = _data;
            _data = new Dictionary<string, object?>();
            return result;
        }

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
            _data = new Dictionary<string, object?>(values);
        }
    }

    private sealed class FakeUserContext : IUserContext
    {
        public FakeUserContext(string userId, bool isAdmin = false, bool isHoD = false)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId)
            };

            if (isAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            if (isHoD)
            {
                claims.Add(new Claim(ClaimTypes.Role, "HoD"));
            }

            UserId = userId;
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        public ClaimsPrincipal User { get; }

        public string? UserId { get; }
    }

    private sealed class FakeAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null)
            => Task.CompletedTask;
    }
}

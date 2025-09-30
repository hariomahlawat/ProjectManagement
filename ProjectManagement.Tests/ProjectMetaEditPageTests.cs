using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Pages.Projects.Meta;
using ProjectManagement.Services;
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

        var userContext = new FakeUserContext("hod-other", isHoD: true);
        var page = CreatePage(db, userContext);
        page.Input = new EditModel.MetaEditInput
        {
            ProjectId = 1,
            Name = "Updated"
        };

        var result = await page.OnPostAsync(1, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task OnPostAsync_AssignedHod_Succeeds()
    {
        await using var db = CreateContext();
        await db.Projects.AddAsync(new Project
        {
            Id = 5,
            Name = "Original",
            CreatedByUserId = "creator",
            HodUserId = "hod-5"
        });
        await db.SaveChangesAsync();

        var userContext = new FakeUserContext("hod-5", isHoD: true);
        var page = CreatePage(db, userContext);
        page.Input = new EditModel.MetaEditInput
        {
            ProjectId = 5,
            Name = "Updated Name",
            Description = "Desc"
        };

        var result = await page.OnPostAsync(5, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Projects/Overview", redirect.PageName);

        var project = await db.Projects.SingleAsync(p => p.Id == 5);
        Assert.Equal("Updated Name", project.Name);
        Assert.Equal("Desc", project.Description);
    }

    [Fact]
    public async Task OnPostAsync_AdminBypassesAssignment()
    {
        await using var db = CreateContext();
        await db.Projects.AddAsync(new Project
        {
            Id = 8,
            Name = "Project",
            CreatedByUserId = "creator",
            HodUserId = "hod-8"
        });
        await db.SaveChangesAsync();

        var userContext = new FakeUserContext("admin-user", isAdmin: true);
        var page = CreatePage(db, userContext);
        page.Input = new EditModel.MetaEditInput
        {
            ProjectId = 8,
            Name = "Admin Updated"
        };

        var result = await page.OnPostAsync(8, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var project = await db.Projects.SingleAsync(p => p.Id == 8);
        Assert.Equal("Admin Updated", project.Name);
    }

    private static EditModel CreatePage(ApplicationDbContext db, IUserContext userContext)
    {
        var page = new EditModel(db, userContext)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            },
            TempData = new TempDataDictionary(new DefaultHttpContext(), new SessionStateTempDataProvider())
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
}

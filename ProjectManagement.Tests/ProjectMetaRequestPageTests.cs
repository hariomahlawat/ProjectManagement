using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
using ProjectManagement.Tests.Fakes;

namespace ProjectManagement.Tests;

public sealed class ProjectMetaRequestPageTests
{
    // SECTION: Description length boundary validation
    [Fact]
    public async Task OnPostAsync_DescriptionLength5000_Accepts()
    {
        await using var db = CreateContext();
        await db.Projects.AddAsync(new Project
        {
            Id = 100,
            Name = "Alpha",
            Description = "Initial",
            CreatedByUserId = "creator",
            LeadPoUserId = "po-100"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var service = new ProjectMetaChangeRequestService(db, clock);
        var page = CreatePage(db, service, new FakeUserContext("po-100", isProjectOfficer: true));
        page.Input = new RequestModel.RequestInput
        {
            ProjectId = 100,
            Name = "Alpha",
            Description = new string('R', ProjectFieldLimits.DescriptionMaxLength)
        };

        ApplyDataAnnotationsModelValidation(page.Input, "Input", page.ModelState);
        var result = await page.OnPostAsync(100, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Projects/Overview", redirect.PageName);

        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        var payload = JsonSerializer.Deserialize<ProjectMetaChangeRequestPayload>(request.Payload);
        Assert.NotNull(payload);
        Assert.Equal(ProjectFieldLimits.DescriptionMaxLength, payload!.Description!.Length);
        Assert.Equal(ProjectFieldLimits.DescriptionMaxLength, request.OriginalDescription!.Length);
    }

    [Fact]
    public async Task OnPostAsync_DescriptionLength5001_ReturnsModelStateError()
    {
        await using var db = CreateContext();
        await db.Projects.AddAsync(new Project
        {
            Id = 101,
            Name = "Beta",
            Description = "Initial",
            CreatedByUserId = "creator",
            LeadPoUserId = "po-101"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var service = new ProjectMetaChangeRequestService(db, clock);
        var page = CreatePage(db, service, new FakeUserContext("po-101", isProjectOfficer: true));
        page.Input = new RequestModel.RequestInput
        {
            ProjectId = 101,
            Name = "Beta",
            Description = new string('R', ProjectFieldLimits.DescriptionMaxLength + 1)
        };

        ApplyDataAnnotationsModelValidation(page.Input, "Input", page.ModelState);
        var result = await page.OnPostAsync(101, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(page.ModelState.TryGetValue("Input.Description", out var entry));
        var error = Assert.Single(entry!.Errors);
        Assert.Contains(ProjectFieldLimits.DescriptionMaxLength.ToString(), error.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(db.ProjectMetaChangeRequests);
    }

    private static RequestModel CreatePage(ApplicationDbContext db, ProjectMetaChangeRequestService service, IUserContext userContext)
    {
        var page = new RequestModel(db, service, userContext)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            },
            TempData = new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider())
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

    private static void ApplyDataAnnotationsModelValidation(object model, string modelPrefix, ModelStateDictionary modelState)
    {
        var validationContext = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, validationContext, results, validateAllProperties: true);

        foreach (var result in results)
        {
            var members = result.MemberNames.Any() ? result.MemberNames : new[] { string.Empty };
            foreach (var member in members)
            {
                var key = string.IsNullOrEmpty(member) ? modelPrefix : $"{modelPrefix}.{member}";
                modelState.AddModelError(key, result.ErrorMessage ?? "Validation failed.");
            }
        }
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
        public FakeUserContext(string userId, bool isProjectOfficer)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId)
            };

            if (isProjectOfficer)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Project Officer"));
            }

            UserId = userId;
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        public ClaimsPrincipal User { get; }

        public string? UserId { get; }
    }
}

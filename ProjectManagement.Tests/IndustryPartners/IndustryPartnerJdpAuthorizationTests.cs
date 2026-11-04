using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Controllers;
using ProjectManagement.Helpers;
using ProjectManagement.Pages.IndustryPartners;
using ProjectManagement.Services.IndustryPartners;
using Xunit;
using static ProjectManagement.Tests.IndustryPartners.IndustryPartnerTestDb;

namespace ProjectManagement.Tests.IndustryPartners;

// SECTION: Project JDP link changes follow the project-level rule from every entry point
public sealed class IndustryPartnerJdpAuthorizationTests
{
    [Fact]
    public async Task LinkProject_OrganisationOwnerWithoutProjectRights_IsForbidden()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var projectId = await db.AddProjectAsync("Someone else's project", leadPoUserId: "po-2");
        var partnerId = await db.AddPartnerAsync("Owner Org", createdByUserId: "ta-1");
        var service = new IndustryPartnerService(db.NewContext());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.LinkProjectAsync(partnerId, projectId, User("ta-1", RoleNames.Ta)));

        Assert.False(await db.LinkExistsAsync(partnerId, projectId));
    }

    [Fact]
    public async Task LinkProject_LeadProjectOfficer_CanLinkOwnProjectOnly()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var ownProject = await db.AddProjectAsync("Own project", leadPoUserId: "po-1");
        var otherProject = await db.AddProjectAsync("Other project", leadPoUserId: "po-2");
        var partnerId = await db.AddPartnerAsync("Shared Org", createdByUserId: "po-1");
        var service = new IndustryPartnerService(db.NewContext());
        var po = User("po-1", RoleNames.ProjectOfficer);

        await service.LinkProjectAsync(partnerId, ownProject, po);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.LinkProjectAsync(partnerId, otherProject, po));

        Assert.True(await db.LinkExistsAsync(partnerId, ownProject));
        Assert.False(await db.LinkExistsAsync(partnerId, otherProject));
    }

    [Fact]
    public async Task LinkProject_LeadUserWithoutProjectOfficerRole_IsForbidden()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var projectId = await db.AddProjectAsync("Project", leadPoUserId: "user-1");
        var partnerId = await db.AddPartnerAsync("Org", createdByUserId: "user-1");
        var service = new IndustryPartnerService(db.NewContext());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.LinkProjectAsync(partnerId, projectId, User("user-1", RoleNames.Ta)));
    }

    [Theory]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.HoD)]
    [InlineData(RoleNames.Comdt)]
    public async Task LinkAndUnlink_ManageAnyRoles_CanChangeAnyProject(string role)
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var projectId = await db.AddProjectAsync("Any project", leadPoUserId: "po-2");
        var partnerId = await db.AddPartnerAsync("Org");
        var service = new IndustryPartnerService(db.NewContext());
        var user = User("privileged", role);

        await service.LinkProjectAsync(partnerId, projectId, user);
        Assert.True(await db.LinkExistsAsync(partnerId, projectId));

        await service.UnlinkProjectAsync(partnerId, projectId, user);
        Assert.False(await db.LinkExistsAsync(partnerId, projectId));
    }

    [Fact]
    public async Task CreateWithProject_UserWithoutProjectRights_IsForbiddenAndCreatesNothing()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var projectId = await db.AddProjectAsync("Project", leadPoUserId: "po-2");
        var context = db.NewContext();
        var service = new IndustryPartnerService(context);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(
            new CreateIndustryPartnerRequest("New Org", null, ProjectId: projectId),
            User("ito-1", RoleNames.Ito)));

        Assert.False(await context.IndustryPartners.AnyAsync());
    }

    [Fact]
    public async Task CreateWithProject_LeadProjectOfficer_CreatesAndLinks()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var projectId = await db.AddProjectAsync("Project", leadPoUserId: "po-1");
        var service = new IndustryPartnerService(db.NewContext());

        var partnerId = await service.CreateAsync(
            new CreateIndustryPartnerRequest("New Org", null, ProjectId: projectId),
            User("po-1", RoleNames.ProjectOfficer));

        Assert.True(await db.LinkExistsAsync(partnerId, projectId));
    }

    [Fact]
    public async Task UnlinkProject_OrganisationOwnerWithoutProjectRights_IsForbidden()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var projectId = await db.AddProjectAsync("Project", leadPoUserId: "po-2");
        var partnerId = await db.AddPartnerAsync("Org", createdByUserId: "ta-1");
        await db.AddLinkAsync(partnerId, projectId);
        var service = new IndustryPartnerService(db.NewContext());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.UnlinkProjectAsync(partnerId, projectId, User("ta-1", RoleNames.Ta)));

        Assert.True(await db.LinkExistsAsync(partnerId, projectId));
    }

    [Fact]
    public async Task UnlinkProject_LinkToSoftDeletedProject_RemainsRemovableSoOrganisationCanBeDeleted()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var projectId = await db.AddProjectAsync("Deleted project", leadPoUserId: "po-2", isDeleted: true);
        var partnerId = await db.AddPartnerAsync("Org");
        await db.AddLinkAsync(partnerId, projectId);
        var context = db.NewContext();
        var service = new IndustryPartnerService(context);
        var hod = User("hod-1", RoleNames.HoD);

        await service.UnlinkProjectAsync(partnerId, projectId, hod);
        await service.DeletePartnerAsync(partnerId, hod);

        Assert.False(await context.IndustryPartners.AnyAsync(partner => partner.Id == partnerId));
    }

    [Fact]
    public async Task ProjectSideAddAndRemove_UserWithoutProjectRights_IsForbidden()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var projectId = await db.AddProjectAsync("Project", leadPoUserId: "po-2");
        var linkedPartner = await db.AddPartnerAsync("Linked Org");
        var otherPartner = await db.AddPartnerAsync("Other Org");
        await db.AddLinkAsync(linkedPartner, projectId);
        var service = new IndustryPartnerService(db.NewContext());
        var po = User("po-1", RoleNames.ProjectOfficer);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.AddProjectJdpAsync(projectId, otherPartner, po));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.RemoveProjectJdpAsync(projectId, linkedPartner, po));

        Assert.True(await db.LinkExistsAsync(linkedPartner, projectId));
        Assert.False(await db.LinkExistsAsync(otherPartner, projectId));
    }

    [Fact]
    public async Task GetJdpManageableProjectIds_ReturnsOnlyProjectsTheUserMayChange()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var own = await db.AddProjectAsync("Own", leadPoUserId: "po-1");
        var other = await db.AddProjectAsync("Other", leadPoUserId: "po-2");
        var service = new IndustryPartnerService(db.NewContext());

        var poIds = await service.GetJdpManageableProjectIdsAsync(new[] { own, other }, User("po-1", RoleNames.ProjectOfficer));
        var adminIds = await service.GetJdpManageableProjectIdsAsync(new[] { own, other }, User("admin", RoleNames.Admin));
        var taIds = await service.GetJdpManageableProjectIdsAsync(new[] { own, other }, User("ta", RoleNames.Ta));

        Assert.Equal(new[] { own }, poIds.OrderBy(id => id));
        Assert.Equal(new[] { own, other }.OrderBy(id => id), adminIds.OrderBy(id => id));
        Assert.Empty(taIds);
    }

    [Fact]
    public async Task ProjectLookup_ListsOnlyProjectsTheUserMayLink()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        await db.AddProjectAsync("Own", leadPoUserId: "po-1");
        await db.AddProjectAsync("Other", leadPoUserId: "po-2");
        var controller = new IndustryPartnersProjectLookupController(db.NewContext())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = User("po-1", RoleNames.ProjectOfficer) }
            }
        };

        var result = Assert.IsType<OkObjectResult>(await controller.GetAsync(q: null));

        var json = JsonSerializer.SerializeToElement(result.Value);
        var names = json.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString());
        Assert.Equal(new[] { "Own" }, names);
    }

    [Fact]
    public async Task DirectoryLinkHandler_WhenProjectRuleRefuses_ShowsMessageInsteadOfLinking()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var projectId = await db.AddProjectAsync("Project", leadPoUserId: "po-2");
        var partnerId = await db.AddPartnerAsync("Org", createdByUserId: "ta-1");
        var page = CreatePage(new IndustryPartnerService(db.NewContext()), User("ta-1", RoleNames.Ta));

        var result = await page.OnPostLinkProjectAsync(partnerId, projectId, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("You are not authorised to change the JDPs for this project.", page.TempData["Error"]);
        Assert.False(await db.LinkExistsAsync(partnerId, projectId));
    }

    [Fact]
    public async Task DirectoryCreateHandler_WithProjectTheUserCannotChange_ShowsMessageAndCreatesNothing()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var projectId = await db.AddProjectAsync("Project", leadPoUserId: "po-2");
        var context = db.NewContext();
        var page = CreatePage(new IndustryPartnerService(context), User("ta-1", RoleNames.Ta));
        page.ContextProjectId = projectId;

        var result = await page.OnPostCreatePartnerAsync("New Org", null, null, "12345", null, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("You are not authorised to change the JDPs for this project.", page.TempData["Error"]);
        Assert.False(await context.IndustryPartners.AnyAsync());
    }

    [Fact]
    public async Task DirectoryContactHandler_WhenOrganisationNoLongerExists_ShowsMessageInsteadOfFailing()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var page = CreatePage(new IndustryPartnerService(db.NewContext()), User("ta-1", RoleNames.Ta));

        var result = await page.OnPostAddContactAsync(9999, "Name", "12345", null, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Industry organisation not found.", page.TempData["Error"]);
    }

    // SECTION: Page scaffolding (all directory policies allowed; the project rule is the service's)
    internal static IndexModel CreatePage(
        IIndustryPartnerService service,
        ClaimsPrincipal user,
        IIndustryPartnerAttachmentManager? attachmentManager = null)
    {
        var page = new IndexModel(service, attachmentManager ?? new UnusedAttachmentManager(), new AllowAuthorizationService());
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());
        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        page.TempData = new TempDataDictionary(httpContext, new NullTempDataProvider());
        return page;
    }

    private sealed class UnusedAttachmentManager : IIndustryPartnerAttachmentManager
    {
        public Task<Guid> UploadAsync(int partnerId, IFormFile file, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(System.IO.Stream Stream, string FileName, string ContentType)> DownloadAsync(int partnerId, Guid attachmentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(int partnerId, Guid attachmentId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class AllowAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}

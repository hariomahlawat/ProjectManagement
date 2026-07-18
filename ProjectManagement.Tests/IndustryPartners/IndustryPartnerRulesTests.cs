using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Models.IndustryPartners;
using ProjectManagement.Services.IndustryPartners;
using Xunit;

namespace ProjectManagement.Tests.IndustryPartners;

public sealed class IndustryPartnerRulesTests
{
    [Fact]
    public async Task SameNameAndNullLocation_CannotRepeat()
    {
        await using var db = CreateDb();
        var service = new IndustryPartnerService(db);
        var user = User();
        await service.CreateAsync(new CreateIndustryPartnerRequest("Acme", null, null), user);
        await Assert.ThrowsAsync<IndustryPartnerValidationException>(() => service.CreateAsync(new CreateIndustryPartnerRequest("  acme ", null, null), user));
    }

    [Fact]
    public async Task SameNameSameLocation_CannotRepeat_ButDifferentLocationAllowed()
    {
        await using var db = CreateDb();
        var service = new IndustryPartnerService(db);
        var user = User();
        await service.CreateAsync(new CreateIndustryPartnerRequest("Acme", "Delhi", null), user);
        await Assert.ThrowsAsync<IndustryPartnerValidationException>(() => service.CreateAsync(new CreateIndustryPartnerRequest("acme", " delhi ", null), user));
        var id = await service.CreateAsync(new CreateIndustryPartnerRequest("Acme", "Pune", null), user);
        Assert.True(id > 0);
    }

    [Fact]
    public async Task LinkedPartner_CannotBeDeleted()
    {
        await using var db = CreateDb();
        db.Projects.Add(new Project { Name = "P1", CreatedByUserId = "u1", WorkflowVersion = "v1" });
        await db.SaveChangesAsync();

        var service = new IndustryPartnerService(db);
        var user = User();
        var id = await service.CreateAsync(new CreateIndustryPartnerRequest("Acme", null, null), user);
        await service.LinkProjectAsync(id, db.Projects.First().Id, user);

        await Assert.ThrowsAsync<IndustryPartnerValidationException>(() => service.DeletePartnerAsync(id, user));
    }


    [Fact]
    public async Task LinkProjectAsync_AllowsProjectBeforeDevelopmentStage()
    {
        await using var db = CreateDb();
        var project = new Project
        {
            Name = "P2",
            CreatedByUserId = "u1",
            WorkflowVersion = PlanConstants.StageTemplateVersionV2,
            ProjectStages = new List<ProjectStage>
            {
                new() { StageCode = StageCodes.FS, Status = StageStatus.Completed },
                new() { StageCode = StageCodes.SOW, Status = StageStatus.InProgress }
            }
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = new IndustryPartnerService(db);
        var user = User();
        var partnerId = await service.CreateAsync(new CreateIndustryPartnerRequest("Beta", null, null), user);

        await service.LinkProjectAsync(partnerId, project.Id, user);

        var linked = await db.IndustryPartnerProjects.AnyAsync(x => x.IndustryPartnerId == partnerId && x.ProjectId == project.Id);
        Assert.True(linked);
    }

    [Fact]
    public async Task LinkProjectAsync_AllowsProjectAtDevelopmentStage()
    {
        await using var db = CreateDb();
        var project = new Project
        {
            Name = "P3",
            CreatedByUserId = "u1",
            WorkflowVersion = PlanConstants.StageTemplateVersionV2,
            ProjectStages = new List<ProjectStage>
            {
                new() { StageCode = StageCodes.DEVP, Status = StageStatus.InProgress }
            }
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = new IndustryPartnerService(db);
        var user = User();
        var partnerId = await service.CreateAsync(new CreateIndustryPartnerRequest("Gamma", null, null), user);

        await service.LinkProjectAsync(partnerId, project.Id, user);

        var linked = await db.IndustryPartnerProjects.AnyAsync(x => x.IndustryPartnerId == partnerId && x.ProjectId == project.Id);
        Assert.True(linked);
    }


    [Fact]
    public async Task CurrentAndPastFilters_AreInclusiveForMixedJdpHistory()
    {
        await using var db = CreateDb();
        var currentProject = new Project
        {
            Name = "Current project",
            CreatedByUserId = "u1",
            WorkflowVersion = "v1",
            LifecycleStatus = ProjectLifecycleStatus.Active
        };
        var completedProject = new Project
        {
            Name = "Completed project",
            CreatedByUserId = "u1",
            WorkflowVersion = "v1",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        };

        db.Projects.AddRange(currentProject, completedProject);
        await db.SaveChangesAsync();

        var service = new IndustryPartnerService(db);
        var user = User();
        var partnerId = await service.CreateAsync(new CreateIndustryPartnerRequest("Mixed History Partner", null), user);
        await service.LinkProjectAsync(partnerId, currentProject.Id, user);
        await service.LinkProjectAsync(partnerId, completedProject.Id, user);

        var current = await service.SearchAsync(null, IndustryPartnerDirectoryFilter.CurrentJdp, 1, 25);
        var past = await service.SearchAsync(null, IndustryPartnerDirectoryFilter.PastJdp, 1, 25);

        Assert.Contains(current.Items, item => item.Id == partnerId);
        Assert.Contains(past.Items, item => item.Id == partnerId);
        Assert.Equal("Current JDP", current.Items.Single(item => item.Id == partnerId).StatusLabel);
    }

    [Fact]
    public async Task ContactValidation_RejectsMissingAndInvalidEmail()
    {
        await using var db = CreateDb();
        var service = new IndustryPartnerService(db);
        var user = User();
        var id = await service.CreateAsync(new CreateIndustryPartnerRequest("Acme", null, null), user);

        await Assert.ThrowsAsync<IndustryPartnerValidationException>(() => service.AddContactAsync(id, new ContactRequest("A", null, null), user));
        await Assert.ThrowsAsync<IndustryPartnerValidationException>(() => service.AddContactAsync(id, new ContactRequest("A", null, "badmail"), user));
    }


    [Fact]
    public async Task ContactCreation_RecordsAuthor_AndUnnamedContactGetsStableDisplayLabel()
    {
        await using var db = CreateDb();
        var service = new IndustryPartnerService(db);
        var author = User("author-1");
        var partnerId = await service.CreateAsync(new CreateIndustryPartnerRequest("Acme", null), author);

        await service.AddContactAsync(partnerId, new ContactRequest(null, "12345", null), author);

        var contact = (await service.GetAsync(partnerId))!.Contacts.Single();
        Assert.Equal("author-1", contact.CreatedByUserId);
        Assert.Equal("General contact", contact.DisplayName);
    }

    [Fact]
    public async Task ContactAuthor_CanEditAndDeleteOwnContact()
    {
        await using var db = CreateDb();
        var service = new IndustryPartnerService(db);
        var author = User("author-1");
        var partnerId = await service.CreateAsync(new CreateIndustryPartnerRequest("Acme", null), author);
        var contactId = await service.AddContactAsync(partnerId, new ContactRequest("Office", "12345", null), author);
        var rowVersion = (await service.GetAsync(partnerId))!.Contacts.Single().RowVersion;

        await service.UpdateContactAsync(
            partnerId,
            contactId,
            new ContactRequest("Business Office", "67890", null, rowVersion),
            author);

        var updated = (await service.GetAsync(partnerId))!.Contacts.Single();
        Assert.Equal("Business Office", updated.DisplayName);

        await service.DeleteContactAsync(partnerId, contactId, author);
        Assert.Empty((await service.GetAsync(partnerId))!.Contacts);
    }

    [Fact]
    public async Task NonAuthor_CannotEditOrDeleteAnotherUsersContact()
    {
        await using var db = CreateDb();
        var service = new IndustryPartnerService(db);
        var author = User("author-1");
        var other = User("author-2");
        var partnerId = await service.CreateAsync(new CreateIndustryPartnerRequest("Acme", null), author);
        var contactId = await service.AddContactAsync(partnerId, new ContactRequest("Office", "12345", null), author);
        var rowVersion = (await service.GetAsync(partnerId))!.Contacts.Single().RowVersion;

        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateContactAsync(
            partnerId,
            contactId,
            new ContactRequest("Changed", "67890", null, rowVersion),
            other));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteContactAsync(partnerId, contactId, other));
    }

    [Theory]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.HoD)]
    [InlineData(RoleNames.Comdt)]
    public async Task ContactOverrideRoles_CanEditAndDeleteAnyContact(string role)
    {
        await using var db = CreateDb();
        var service = new IndustryPartnerService(db);
        var author = User("author-1");
        var privileged = User("privileged-user", role);
        var partnerId = await service.CreateAsync(new CreateIndustryPartnerRequest($"Acme {role}", null), author);
        var contactId = await service.AddContactAsync(partnerId, new ContactRequest("Office", "12345", null), author);
        var rowVersion = (await service.GetAsync(partnerId))!.Contacts.Single().RowVersion;

        await service.UpdateContactAsync(
            partnerId,
            contactId,
            new ContactRequest("Updated Office", "67890", null, rowVersion),
            privileged);
        await service.DeleteContactAsync(partnerId, contactId, privileged);

        Assert.Empty((await service.GetAsync(partnerId))!.Contacts);
    }

    [Fact]
    public async Task LegacyContactWithoutAuthor_IsRestrictedToOverrideRoles()
    {
        await using var db = CreateDb();
        var service = new IndustryPartnerService(db);
        var creator = User("creator");
        var ordinary = User("ordinary");
        var admin = User("admin", RoleNames.Admin);
        var partnerId = await service.CreateAsync(new CreateIndustryPartnerRequest("Legacy Partner", null), creator);
        var partner = await db.IndustryPartners.SingleAsync(item => item.Id == partnerId);
        var legacy = new IndustryPartnerContact
        {
            IndustryPartnerId = partnerId,
            Name = "Legacy Office",
            Phone = "12345",
            CreatedByUserId = null
        };
        db.IndustryPartnerContacts.Add(legacy);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteContactAsync(partnerId, legacy.Id, ordinary));
        await service.DeleteContactAsync(partnerId, legacy.Id, admin);
        Assert.False(await db.IndustryPartnerContacts.AnyAsync(item => item.Id == legacy.Id));
    }

    [Fact]
    public async Task DeletingPrimaryContact_PromotesNextContact()
    {
        await using var db = CreateDb();
        var service = new IndustryPartnerService(db);
        var author = User("author-1");
        var partnerId = await service.CreateAsync(new CreateIndustryPartnerRequest("Acme", null), author);
        var firstId = await service.AddContactAsync(partnerId, new ContactRequest("First", "111", null), author);
        await service.AddContactAsync(partnerId, new ContactRequest("Second", "222", null), author);

        await service.DeleteContactAsync(partnerId, firstId, author);

        var partner = await service.GetAsync(partnerId);
        Assert.Equal("Second", partner!.PrimaryContact!.DisplayName);
    }

    private static ApplicationDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static ClaimsPrincipal User(string userId = "u1", string? role = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (!string.IsNullOrWhiteSpace(role))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }
}

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
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

    private static ApplicationDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static ClaimsPrincipal User()
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "u1") }, "test");
        return new ClaimsPrincipal(identity);
    }
}

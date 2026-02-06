using System;
using System.Security.Claims;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
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

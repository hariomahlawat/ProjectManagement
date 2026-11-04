using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Configuration;
using ProjectManagement.Models.IndustryPartners;
using ProjectManagement.Services.IndustryPartners;
using Xunit;
using static ProjectManagement.Tests.IndustryPartners.IndustryPartnerTestDb;

namespace ProjectManagement.Tests.IndustryPartners;

// SECTION: Overlapping requests on one organisation, and how save failures are reported
public sealed class IndustryPartnerConcurrencyAndSaveTests
{
    [Fact]
    public async Task AddContact_WhenAnotherUserChangedTheOrganisationMeanwhile_StillSucceeds()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var partnerId = await db.AddPartnerAsync("Busy Org");
        var first = db.NewContext();
        var second = db.NewContext();

        // Request A has read the organisation (and its row version) ...
        _ = await first.IndustryPartners.SingleAsync(partner => partner.Id == partnerId);

        // ... request B adds a contact, which stamps the organisation and changes its row version ...
        await new IndustryPartnerService(second).AddContactAsync(
            partnerId, new ContactRequest("B", "222", null), User("user-b"));

        // ... and request A's contact must still be saved.
        await new IndustryPartnerService(first).AddContactAsync(
            partnerId, new ContactRequest("A", "111", null), User("user-a"));

        await using var check = db.NewContext();
        Assert.Equal(2, await check.IndustryPartnerContacts.CountAsync(contact => contact.IndustryPartnerId == partnerId));
        var partner = await check.IndustryPartners.SingleAsync(item => item.Id == partnerId);
        Assert.Equal("user-a", partner.UpdatedByUserId);
    }

    [Fact]
    public async Task LinkProject_WhenAnotherUserChangedTheOrganisationMeanwhile_StillSucceeds()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var partnerId = await db.AddPartnerAsync("Busy Org");
        var projectA = await db.AddProjectAsync("Project A");
        var projectB = await db.AddProjectAsync("Project B");
        var first = db.NewContext();
        var second = db.NewContext();
        var admin = User("admin", RoleNames.Admin);

        _ = await first.IndustryPartners.SingleAsync(partner => partner.Id == partnerId);
        await new IndustryPartnerService(second).AddProjectJdpAsync(projectB, partnerId, admin);

        await new IndustryPartnerService(first).AddProjectJdpAsync(projectA, partnerId, admin);

        Assert.True(await db.LinkExistsAsync(partnerId, projectA));
        Assert.True(await db.LinkExistsAsync(partnerId, projectB));
    }

    [Fact]
    public async Task UpdateContact_WithStaleContactVersion_IsStillReportedAsAConflict()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var partnerId = await db.AddPartnerAsync("Org");
        var service = new IndustryPartnerService(db.NewContext());
        var author = User("author");
        var contactId = await service.AddContactAsync(partnerId, new ContactRequest("C", "111", null), author);
        var staleVersion = (await service.GetAsync(partnerId))!.Contacts.Single().RowVersion;

        await new IndustryPartnerService(db.NewContext()).UpdateContactAsync(
            partnerId, contactId, new ContactRequest("C2", "222", null, staleVersion), author);

        var exception = await Assert.ThrowsAsync<IndustryPartnerValidationException>(() =>
            new IndustryPartnerService(db.NewContext()).UpdateContactAsync(
                partnerId, contactId, new ContactRequest("C3", "333", null, staleVersion), author));
        Assert.True(exception.Errors.ContainsKey("concurrency"));
    }

    [Fact]
    public async Task Create_WhenTheSameOrganisationIsSavedConcurrently_ReportsDuplicate()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var interceptor = new BeforeSaveInterceptor(() => db.AddPartnerAsync("Race Org").GetAwaiter().GetResult());
        var service = new IndustryPartnerService(db.NewContext(interceptor));

        var exception = await Assert.ThrowsAsync<IndustryPartnerValidationException>(() =>
            service.CreateAsync(new CreateIndustryPartnerRequest("Race Org", null), User("u1")));

        Assert.Equal(
            "An organisation with the same name and location already exists.",
            Assert.Single(exception.Errors["name"]));
    }

    [Fact]
    public async Task Create_WhenTheSaveFailsForAnotherReason_DoesNotClaimADuplicate()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var interceptor = new BeforeSaveInterceptor(() => throw new DbUpdateException("simulated failure"));
        var service = new IndustryPartnerService(db.NewContext(interceptor));

        var exception = await Assert.ThrowsAsync<IndustryPartnerValidationException>(() =>
            service.CreateAsync(new CreateIndustryPartnerRequest("Unique Org", null), User("u1")));

        Assert.False(exception.Errors.ContainsKey("name"));
        Assert.True(exception.Errors.ContainsKey("save"));
    }

    // Runs an action once, just before the first SaveChanges of the context.
    private sealed class BeforeSaveInterceptor : SaveChangesInterceptor
    {
        private readonly Action _action;
        private int _ran;

        public BeforeSaveInterceptor(Action action)
        {
            _action = action;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _ran, 1) == 0)
            {
                _action();
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

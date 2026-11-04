using System;
using System.Linq;
using System.Threading.Tasks;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.IndustryPartners;
using Xunit;
using static ProjectManagement.Tests.IndustryPartners.IndustryPartnerTestDb;

namespace ProjectManagement.Tests.IndustryPartners;

// SECTION: The project Overview's JDP panel shows how to reach each partner
public sealed class ProjectJdpPanelContactTests
{
    [Fact]
    public async Task EachLinkedPartner_CarriesItsPrimaryContact_ByTheDirectoryRule()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var project = await db.AddProjectAsync("Counter-drone Jammer");
        var withChoice = await db.AddPartnerAsync("Aravalli Defence");
        var withoutChoice = await db.AddPartnerAsync("Bharat Optronics");
        var noContacts = await db.AddPartnerAsync("Chola Antenna");
        foreach (var partner in new[] { withChoice, withoutChoice, noContacts })
        {
            await db.AddLinkAsync(partner, project);
        }

        var service = new IndustryPartnerService(db.NewContext());
        var admin = User("admin", RoleNames.Admin);
        await service.AddContactAsync(withChoice, new ContactRequest("Business Development Office", "0124 455 0100", null), admin);
        var chosen = await service.AddContactAsync(withChoice, new ContactRequest("Col Vikram Sethi", "+91 98110 24567", "vikram@aravalli.example"), admin);
        await service.SetPrimaryContactAsync(withChoice, chosen, admin);
        await service.AddContactAsync(withoutChoice, new ContactRequest(null, null, "sales@bol.example"), admin);
        await service.AddContactAsync(withoutChoice, new ContactRequest("Later Person", "222", null), admin);

        var profile = await new IndustryPartnerService(db.NewContext()).GetProjectMultiJdpProfileAsync(project);
        var byName = profile.Partners.ToDictionary(partner => partner.Name);

        Assert.Equal("Col Vikram Sethi", byName["Aravalli Defence"].PrimaryContact!.DisplayName);
        Assert.Equal("+91 98110 24567", byName["Aravalli Defence"].PrimaryContact!.Phone);
        Assert.Equal("General contact", byName["Bharat Optronics"].PrimaryContact!.DisplayName);
        Assert.Equal("sales@bol.example", byName["Bharat Optronics"].PrimaryContact!.Email);
        Assert.Null(byName["Chola Antenna"].PrimaryContact);
    }

    [Theory]
    [InlineData(new JdpProjectStatus[0], "Not a JDP on any other project")]
    [InlineData(new[] { JdpProjectStatus.Archived }, "Also a JDP on 1 other project (archived)")]
    [InlineData(new[] { JdpProjectStatus.Ongoing }, "Also a JDP on 1 other project (ongoing)")]
    [InlineData(new[] { JdpProjectStatus.Completed, JdpProjectStatus.Ongoing, JdpProjectStatus.Ongoing, JdpProjectStatus.Cancelled },
        "Also a JDP on 4 other projects · 2 ongoing · 1 completed · 1 cancelled")]
    public void UsageSummary_NamesEachStatusInsteadOfOther(JdpProjectStatus[] statuses, string expected)
    {
        Assert.Equal(expected, JdpStatusRules.UsageSummary(statuses));

        var partner = new ProjectJdpPartnerProfileDto(1, "Org", null,
            statuses.Select((status, index) => new ProjectJdpLinkedProjectDto(
                index + 10,
                $"P{index}",
                null,
                status switch
                {
                    JdpProjectStatus.Completed => ProjectLifecycleStatus.Completed,
                    JdpProjectStatus.Cancelled => ProjectLifecycleStatus.Cancelled,
                    _ => ProjectLifecycleStatus.Active
                },
                status == JdpProjectStatus.Archived,
                false)).ToList());
        Assert.Equal(expected, partner.UsageSummary);
    }
}

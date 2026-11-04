using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Controllers;
using ProjectManagement.Models;
using ProjectManagement.Models.IndustryPartners;
using ProjectManagement.Services.IndustryPartners;
using Xunit;
using static ProjectManagement.Tests.IndustryPartners.IndustryPartnerTestDb;

namespace ProjectManagement.Tests.IndustryPartners;

// SECTION: Status wording, deleted-project links, duplicate suggestions, kept form input, search escaping
public sealed class IndustryPartnerDirectoryConsistencyTests
{
    [Theory]
    [InlineData(ProjectLifecycleStatus.Active, false, false, JdpProjectStatus.Ongoing, "Ongoing")]
    [InlineData(ProjectLifecycleStatus.Completed, false, false, JdpProjectStatus.Completed, "Completed")]
    [InlineData(ProjectLifecycleStatus.Cancelled, false, false, JdpProjectStatus.Cancelled, "Cancelled")]
    [InlineData(ProjectLifecycleStatus.Active, true, false, JdpProjectStatus.Archived, "Archived")]
    [InlineData(ProjectLifecycleStatus.Completed, true, true, JdpProjectStatus.Deleted, "Deleted project")]
    public void StatusRules_ResolveEachProjectStateOnce(
        ProjectLifecycleStatus lifecycle,
        bool archived,
        bool deleted,
        JdpProjectStatus expected,
        string expectedLabel)
    {
        var status = JdpStatusRules.ForProject(lifecycle, archived, deleted);

        Assert.Equal(expected, status);
        Assert.Equal(expectedLabel, JdpStatusRules.Label(status));

        // Every DTO that shows a project's JDP status agrees with the rules.
        Assert.Equal(expectedLabel, new IndustryPartnerProjectDto(1, "P", null, lifecycle, archived, deleted, DateTimeOffset.UtcNow).ProjectStatusLabel);
        Assert.Equal(expectedLabel, new ProjectJdpLinkedProjectDto(1, "P", null, lifecycle, archived, deleted).StatusLabel);
    }

    [Fact]
    public void UsageSummary_IsWordedTheSameForOptionsAndLinkedPartners()
    {
        var option = new ProjectJdpOptionDto(1, "Org", null, OtherProjectCount: 3, OtherOngoingProjectCount: 1, OtherCompletedProjectCount: 1);

        Assert.Equal("Also linked to 3 other projects · 1 ongoing · 1 completed · 1 other", option.UsageSummary);
        Assert.Equal("Not linked to any other project", new ProjectJdpOptionDto(1, "Org", null, 0, 0, 0).UsageSummary);
    }

    [Fact]
    public async Task LinksToDeletedProjectsOnly_AreTreatedAsContactOnlyButStayRemovable()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var deletedProject = await db.AddProjectAsync("Deleted", isDeleted: true);
        var partnerId = await db.AddPartnerAsync("Orphaned Org");
        await db.AddLinkAsync(partnerId, deletedProject);
        var service = new IndustryPartnerService(db.NewContext());

        var contactOnly = await service.SearchAsync(null, IndustryPartnerDirectoryFilter.ContactOnly, 1, 25);
        var associated = await service.SearchAsync(null, IndustryPartnerDirectoryFilter.JdpAssociated, 1, 25);
        var past = await service.SearchAsync(null, IndustryPartnerDirectoryFilter.PastJdp, 1, 25);
        var detail = await service.GetAsync(partnerId);

        var item = Assert.Single(contactOnly.Items);
        Assert.Equal(0, item.ProjectCount);
        Assert.Equal("Contact only", item.StatusLabel);
        Assert.Empty(associated.Items);
        Assert.Empty(past.Items);

        Assert.Equal("Contact only", detail!.StatusLabel);
        Assert.Equal(0, detail.LiveProjectCount);
        var link = Assert.Single(detail.LinkedProjects);
        Assert.Equal("Deleted project", link.ProjectStatusLabel);
    }

    [Fact]
    public async Task LiveAndDeletedLinks_CountOnlyTheLiveProjects()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var live = await db.AddProjectAsync("Live");
        var deleted = await db.AddProjectAsync("Deleted", isDeleted: true);
        var partnerId = await db.AddPartnerAsync("Mixed Org");
        await db.AddLinkAsync(partnerId, live);
        await db.AddLinkAsync(partnerId, deleted);
        var service = new IndustryPartnerService(db.NewContext());

        var item = Assert.Single((await service.SearchAsync(null, IndustryPartnerDirectoryFilter.All, 1, 25)).Items);
        var suggestion = Assert.Single(await service.FindDuplicateSuggestionsAsync("Mixed Org"));

        Assert.Equal(1, item.ProjectCount);
        Assert.Equal("Current JDP", item.StatusLabel);
        Assert.Equal(1, suggestion.ProjectCount);
    }

    [Fact]
    public async Task DuplicateSuggestions_FindMatchesBeyondTheFirstThousandOrganisations()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        await using (var seed = db.NewContext())
        {
            for (var i = 0; i < 1100; i++)
            {
                var name = $"Aardvark Works {i:D4}";
                seed.IndustryPartners.Add(new IndustryPartner
                {
                    Name = name,
                    NormalizedName = name.ToLowerInvariant(),
                    CreatedByUserId = "seed"
                });
            }

            await seed.SaveChangesAsync();
        }

        await db.AddPartnerAsync("Zeta Systems Pvt Ltd");
        var service = new IndustryPartnerService(db.NewContext());

        var suggestions = await service.FindDuplicateSuggestionsAsync("M/s Zeta Systems Limited");

        Assert.Equal("Zeta Systems Pvt Ltd", Assert.Single(suggestions).Name);
    }

    [Fact]
    public async Task DuplicateSuggestions_IgnoreOrganisationsWhoseNameIsOnlyACompanySuffix()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        await db.AddPartnerAsync("Ltd");
        await db.AddPartnerAsync("Kestrel Avionics");
        var service = new IndustryPartnerService(db.NewContext());

        var suggestions = await service.FindDuplicateSuggestionsAsync("Kestrel Avionics Pvt Ltd");

        Assert.Equal(new[] { "Kestrel Avionics" }, suggestions.Select(item => item.Name));
    }

    [Fact]
    public async Task CreateHandler_SavesNotes_AndKeepsTheUsersInputWhenRefused()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var context = db.NewContext();
        var service = new IndustryPartnerService(context);
        var user = User("creator", RoleNames.ProjectOffice);

        var page = IndustryPartnerJdpAuthorizationTests.CreatePage(service, user);
        await page.OnPostCreatePartnerAsync(
            "Heron Labs", "Pune", "Met at expo", contactName: null, contactPhone: null, contactEmail: null, CancellationToken.None);
        Assert.Equal("Met at expo", (await context.IndustryPartners.SingleAsync()).Remarks);

        var refused = IndustryPartnerJdpAuthorizationTests.CreatePage(service, user);
        await refused.OnPostCreatePartnerAsync(
            "Heron Labs", "Pune", "Second attempt", "Ms Rao", "98765", "rao@example.test", CancellationToken.None);
        await refused.OnGetAsync(CancellationToken.None);

        var draft = Assert.IsType<ProjectManagement.Pages.IndustryPartners.IndexModel.OrganisationDraft>(refused.CreateDraft);
        Assert.Equal("Heron Labs", draft.Name);
        Assert.Equal("Second attempt", draft.Remarks);
        Assert.Equal("Ms Rao", draft.ContactName);
        Assert.Equal("98765", draft.ContactPhone);
        Assert.Equal(1, await context.IndustryPartners.CountAsync());
    }

    [Fact]
    public async Task UpdateHandler_KeepsInputOnValidationError_ButNotAfterAConflict()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        await db.AddPartnerAsync("Existing Org");
        var partnerId = await db.AddPartnerAsync("Editable Org", createdByUserId: "owner");
        var context = db.NewContext();
        var service = new IndustryPartnerService(context);
        var owner = User("owner");
        var version = (await service.GetAsync(partnerId))!.RowVersion;

        var duplicate = IndustryPartnerJdpAuthorizationTests.CreatePage(service, owner);
        duplicate.Id = partnerId;
        await duplicate.OnPostUpdatePartnerAsync(partnerId, "Existing Org", null, "Typed notes", version, CancellationToken.None);
        await duplicate.OnGetAsync(CancellationToken.None);
        Assert.Equal("Typed notes", duplicate.EditDraft?.Remarks);

        var conflict = IndustryPartnerJdpAuthorizationTests.CreatePage(service, owner);
        conflict.Id = partnerId;
        var staleVersion = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        await conflict.OnPostUpdatePartnerAsync(partnerId, "Renamed Org", null, "Typed notes", staleVersion, CancellationToken.None);
        await conflict.OnGetAsync(CancellationToken.None);
        Assert.Null(conflict.EditDraft);
    }

    [Fact]
    public async Task UpdateHandler_RestoredEdit_KeepsTheSubmittedVersionSoAConcurrentChangeIsNotOverwritten()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        await db.AddPartnerAsync("Existing Org");
        var partnerId = await db.AddPartnerAsync("Editable Org", createdByUserId: "owner");
        var owner = User("owner");
        var loadedVersion = (await new IndustryPartnerService(db.NewContext()).GetAsync(partnerId))!.RowVersion;

        // Another user saves the organisation after this user's form was loaded.
        await new IndustryPartnerService(db.NewContext()).UpdateAsync(
            partnerId, new UpdateIndustryPartnerRequest("Editable Org", "Delhi", "Other user's notes", loadedVersion), User("hod", RoleNames.HoD));

        // This user's submission fails validation first (duplicate name), so the form is restored.
        var refused = IndustryPartnerJdpAuthorizationTests.CreatePage(new IndustryPartnerService(db.NewContext()), owner);
        refused.Id = partnerId;
        await refused.OnPostUpdatePartnerAsync(partnerId, "Existing Org", null, "My notes", loadedVersion, CancellationToken.None);
        await refused.OnGetAsync(CancellationToken.None);
        Assert.Equal(loadedVersion, refused.EditDraft?.RowVersion);

        // Correcting the name and resubmitting the restored form reports the conflict.
        var resubmit = IndustryPartnerJdpAuthorizationTests.CreatePage(new IndustryPartnerService(db.NewContext()), owner);
        await resubmit.OnPostUpdatePartnerAsync(partnerId, "Editable Org", null, "My notes", refused.EditDraft!.RowVersion, CancellationToken.None);

        Assert.Contains("changed by another user", resubmit.TempData["Error"] as string);
        await using var check = db.NewContext();
        Assert.Equal("Other user's notes", (await check.IndustryPartners.SingleAsync(p => p.Id == partnerId)).Remarks);
    }

    [Fact]
    public void Drafts_StayWithinTheCookieBudget_DroppingLongFieldsFirst()
    {
        // 4,000 Devanagari characters: each is escaped to six bytes when stored in TempData.
        var longHindiNotes = new string('\u0928', 4000);
        var longHindiLocation = new string('\u092A', 2000);
        var draft = new ProjectManagement.Pages.IndustryPartners.IndexModel.OrganisationDraft(
            null, "Heron Labs", longHindiLocation, longHindiNotes, "Ms Rao", "98765", "rao@example.test");

        var stored = ProjectManagement.Pages.IndustryPartners.IndexModel.FitDraftForCookie(draft);

        Assert.NotNull(stored);
        Assert.True(System.Text.Json.JsonSerializer.Serialize(stored).Length
            <= ProjectManagement.Pages.IndustryPartners.IndexModel.MaxStoredDraftLength);
        var kept = System.Text.Json.JsonSerializer.Deserialize<ProjectManagement.Pages.IndustryPartners.IndexModel.OrganisationDraft>(stored!)!;
        Assert.Equal("Heron Labs", kept.Name);
        Assert.Equal("98765", kept.ContactPhone);
        Assert.Null(kept.Remarks);
        Assert.Null(kept.Location);
    }

    [Fact]
    public void Drafts_WithOrdinaryInput_AreKeptWhole()
    {
        var draft = new ProjectManagement.Pages.IndustryPartners.IndexModel.OrganisationDraft(
            null, "Heron Labs", "Pune", new string('n', 3000));

        var stored = ProjectManagement.Pages.IndustryPartners.IndexModel.FitDraftForCookie(draft);

        var kept = System.Text.Json.JsonSerializer.Deserialize<ProjectManagement.Pages.IndustryPartners.IndexModel.OrganisationDraft>(stored!)!;
        Assert.Equal(draft, kept);
    }

    [Theory]
    [InlineData("50%", @"50\%")]
    [InlineData("file_no", @"file\_no")]
    [InlineData(@"a\b", @"a\\b")]
    [InlineData("plain", "plain")]
    public void ProjectLookup_EscapesWildcardsTypedByTheUser(string input, string expected)
    {
        Assert.Equal(expected, IndustryPartnersProjectLookupController.EscapeLikePattern(input));
    }
}

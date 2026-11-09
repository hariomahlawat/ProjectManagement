using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;
using Xunit;
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

// IT projects on the conference review (owner, 9 Nov 2026): every live IT project sits under each
// officer who holds the ITO role; directions are the IT project's own conference remarks.
public class ItProjectConferenceTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static async Task<(int Live, int Delivered)> SeedAsync(ItProjectTestDb db, bool withIto = true)
    {
        await using (var context = db.NewContext())
        {
            context.Users.AddRange(User("hod-1", "Col Sharma"), User("ito-1", "Maj Rao"), User("po-1", "Lt Col Iyer"));
            context.Roles.Add(new IdentityRole { Id = "role-hod", Name = RoleNames.HoD, NormalizedName = RoleNames.HoD.ToUpperInvariant() });
            context.UserRoles.Add(new IdentityUserRole<string> { UserId = "hod-1", RoleId = "role-hod" });
            if (withIto)
            {
                context.Roles.Add(new IdentityRole { Id = "role-ito", Name = RoleNames.Ito, NormalizedName = RoleNames.Ito.ToUpperInvariant() });
                context.UserRoles.Add(new IdentityUserRole<string> { UserId = "ito-1", RoleId = "role-ito" });
            }

            await context.SaveChangesAsync();
        }

        var (live, liveVersion) = await db.SeedAsync("Core switches", "IT PPP 09/2025", 2025);
        Assert.True((await db.Commands().SaveStagesAsync(live, Input(S("TEC", true, D(2025, 11, 18))), liveVersion, ItProjectTestDb.Actor())).Result.Succeeded);
        var (delivered, deliveredVersion) = await db.SeedAsync("Fibre ring", "IT PPP 10/2024", 2024);
        Assert.True((await db.Commands().SaveStagesAsync(delivered, Input(S("DLV", true, D(2025, 6, 30))), deliveredVersion, ItProjectTestDb.Actor())).Result.Succeeded);
        return (live, delivered);
    }

    private static ApplicationUser User(string id, string name) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        FullName = name,
        SecurityStamp = Guid.NewGuid().ToString()
    };

    private static OfficerConferenceReadService Reads(ItProjectTestDb db, params CommandOfficerWorkloadVm[] officers)
        => new(db.NewContext(), new Workload(officers), new WorkflowStageMetadataProvider(), db.Clock);

    [Fact]
    public async Task Ito_IsListedWithTheLiveItProjects_EvenWithoutOtherWork()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (live, delivered) = await SeedAsync(db);
        var service = Reads(db, new CommandOfficerWorkloadVm { UserId = "po-1", OfficerName = "Lt Col Iyer" });

        var options = await service.GetOfficerOptionsAsync("hod-1");
        Assert.Equal(new[] { "po-1", "ito-1" }, options.Select(option => option.UserId));
        Assert.Equal(1, options.Single(option => option.UserId == "ito-1").ItProjectCount);
        Assert.Equal(0, options.Single(option => option.UserId == "po-1").ItProjectCount);

        var conference = await service.GetAsync("hod-1", "ito-1");
        Assert.NotNull(conference);
        Assert.Equal(1, conference!.ItProjectCount);
        // An ITO listed only for IT projects gets just that section (no Add idea / Add task).
        Assert.Equal(new[] { ConferenceItemKind.ItProject }, conference.Sections.Select(item => item.Kind));
        var section = conference.Sections.Single(item => item.Kind == ConferenceItemKind.ItProject);
        var item = Assert.Single(section.Items);
        Assert.Equal(live, item.ItemId);                       // the delivered one is not live
        Assert.Equal("At COB", item.CurrentStateName);
        Assert.Equal($"/ItProjects/{live}", item.OpenUrl);
        Assert.Equal("IT PPP 09/2025 · FY 2026-27 · CF", item.CurrentContext); // carried into FY 2026-27 without an SO; number not entered yet
        Assert.True(item.RequiresAttention);                     // 355 days at COB
        Assert.Null(item.LatestDirection);

        // Once this year's number is entered, the context shows it with this year.
        Assert.True((await db.Commands().SaveYearAsync(live, 2026, new ItProjectYearInput(null, "IT PPP 07/2026"), await db.RowVersionAsync(live), ItProjectTestDb.Actor())).Succeeded);
        var refreshed = (await Reads(db).GetAsync("hod-1", "ito-1"))!.Sections.Single(item2 => item2.Kind == ConferenceItemKind.ItProject).Items.Single();
        Assert.Equal("IT PPP 07/2026 · FY 2026-27 · CF", refreshed.CurrentContext);

        // A Project Officer without the ITO role gets no IT projects section.
        var po = await service.GetAsync("hod-1", "po-1");
        Assert.DoesNotContain(po!.Sections, section2 => section2.Kind == ConferenceItemKind.ItProject);
        Assert.DoesNotContain(delivered, section.Items.Select(row => row.ItemId));
    }

    [Fact]
    public async Task OtherConsumers_KeepTheirScope()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAsync(db);
        var service = Reads(db);

        // The command desk and the Notebook digest are not part of this change.
        Assert.Empty(await service.GetAllForCommandAsync("hod-1"));
        Assert.Empty((await service.GetLatestDirectionDigestAsync("hod-1"))!.OfficerGroups);
        var options = await service.GetOfficerOptionsAsync("hod-1");
        Assert.Equal(new[] { "ito-1" }, options.Select(option => option.UserId));
    }

    [Fact]
    public async Task NoItoRole_NoItProjects()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAsync(db, withIto: false);

        Assert.Empty(await Reads(db).GetOfficerOptionsAsync("hod-1"));
    }

    [Fact]
    public async Task Directions_ShowTheLatestWithTheStageWhenIssued_AndTheItoReply()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (live, _) = await SeedAsync(db);
        var remarks = db.Remarks();
        Assert.True((await remarks.AddAsync(live, new ItProjectRemarkInput("Close COB this week.", D(2026, 11, 2), ItProjectRemarkType.Conference), ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD))).Result.Succeeded);
        db.Clock.Set(ItProjectTestDb.Now.AddHours(1));
        Assert.True((await remarks.AddAsync(live, new ItProjectRemarkInput("Bids opened; COB on Friday.", D(2026, 11, 8)), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito))).Result.Succeeded);
        db.Clock.Set(ItProjectTestDb.Now.AddHours(2));
        Assert.True((await remarks.AddAsync(live, new ItProjectRemarkInput("Brief me after COB.", D(2026, 11, 8), ItProjectRemarkType.Conference), ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD))).Result.Succeeded);

        var service = Reads(db);
        var item = (await service.GetAsync("hod-1", "ito-1"))!.Sections.Single(section => section.Kind == ConferenceItemKind.ItProject).Items.Single();
        Assert.Equal("Brief me after COB.", item.LatestDirection!.Body);
        Assert.Equal("Stage when issued", item.LatestDirection.SnapshotLabel);
        Assert.Equal("COB", item.LatestDirection.SnapshotValue);
        Assert.Equal(2, item.DirectionCount);
        Assert.Empty(item.ProgressEntries);                     // nothing since the latest direction
        Assert.NotNull(item.EmptyProgressText);

        var history = await service.GetDirectionHistoryAsync("hod-1", "ito-1", ConferenceItemKind.ItProject, live);
        Assert.NotNull(history);
        Assert.Equal(2, history!.Cycles.Count);
        Assert.Equal("Bids opened; COB on Friday.", Assert.Single(history.Cycles[0].ProgressEntries).Body);
        Assert.True(history.Cycles[1].IsLatest);

        // An IT project the officer does not hold is not readable through another officer.
        Assert.Null(await service.GetDirectionHistoryAsync("hod-1", "po-1", ConferenceItemKind.ItProject, live));
    }

    [Fact]
    public async Task Ito_SeesTheItProjectsAndTheirDirectionsInTheirOwnView()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (live, _) = await SeedAsync(db);
        Assert.True((await db.Remarks().AddAsync(live, new ItProjectRemarkInput("Close COB this week.", D(2026, 11, 8), ItProjectRemarkType.Conference), ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD))).Result.Succeeded);
        var service = Reads(db, new CommandOfficerWorkloadVm { UserId = "po-1", OfficerName = "Lt Col Iyer" });

        var own = await service.GetForProjectOfficerAsync("ito-1");
        Assert.NotNull(own);
        var item = Assert.Single(own!.Sections.Single(section => section.Kind == ConferenceItemKind.ItProject).Items);
        Assert.Equal(live, item.ItemId);
        Assert.Equal("Close COB this week.", item.LatestDirection!.Body);

        var history = await service.GetDirectionHistoryForProjectOfficerAsync("ito-1", ConferenceItemKind.ItProject, live);
        Assert.Single(history!.Cycles);

        // A Project Officer without the ITO role sees no IT projects and cannot read their history.
        var po = await service.GetForProjectOfficerAsync("po-1");
        Assert.DoesNotContain(po!.Sections, section => section.Kind == ConferenceItemKind.ItProject);
        Assert.Null(await service.GetDirectionHistoryForProjectOfficerAsync("po-1", ConferenceItemKind.ItProject, live));
    }

    private sealed class Workload(IReadOnlyList<CommandOfficerWorkloadVm> officers) : IOfficerWorkloadReadService
    {
        public Task<IReadOnlyList<CommandOfficerWorkloadVm>> GetAllAsync(string requestingUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(officers);

        public Task<CommandOfficerWorkloadVm?> GetOfficerAsync(string officerUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(officers.SingleOrDefault(officer => officer.UserId == officerUserId));

        public Task<int> CountActiveOfficersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(officers.Count);
    }
}

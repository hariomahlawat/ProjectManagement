using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;

namespace ProjectManagement.Tests.ItProjects;

public class ItProjectRemarkServiceTests
{
    [Fact]
    public async Task Add_WritesTheRemarkAsTheActorsRoleWithASnapshotAndAnAuditRow()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        await AddUserAsync(db, "hod-1", "Col Sharma");

        var (result, remarkId) = await db.Remarks().AddAsync(
            id,
            new ItProjectRemarkInput("  Confirm the uplink optics are in the SO scope.  ", new DateOnly(2026, 10, 28)),
            ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var list = await db.Remarks().ListAsync(id);
        var remark = Assert.Single(list);
        Assert.Equal(remarkId, remark.Id);
        Assert.Equal("Col Sharma", remark.AuthorName);
        Assert.Equal(RoleNames.HoD, remark.AuthorRole);
        Assert.Equal("Confirm the uplink optics are in the SO scope.", remark.Body);
        Assert.Equal(new DateOnly(2026, 10, 28), remark.EventDate);
        Assert.Equal(ItProjectRemarkType.General, remark.Type);
        Assert.Null(remark.LastEditedAtUtc);
        Assert.NotEmpty(remark.RowVersion);

        await using var context = db.NewContext();
        var snapshot = Assert.Single(await context.ItProjectRemarkAudits.Where(audit => audit.RemarkId == remarkId).ToListAsync());
        Assert.Equal(ItProjectRemarkAuditAction.Created, snapshot.Action);
        Assert.Equal("hod-1", snapshot.ActorUserId);
        Assert.Equal(RoleNames.HoD, snapshot.ActorRole);
        Assert.Equal("Confirm the uplink optics are in the SO scope.", snapshot.SnapshotBody);
        var audit = db.Audit.Entries.Last();
        Assert.Equal("ItProjects.RemarkAdded", audit.Action);
        Assert.Equal(id.ToString(), audit.Data["ItProjectId"]);
        Assert.Equal("General", audit.Data["Type"]);
    }

    [Fact]
    public async Task Add_NeedsARemarkRoleAndABody()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();

        var noRole = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("Fine.", new DateOnly(2026, 11, 8)), ItProjectTestDb.RemarkActor("admin-1", null, isAdmin: true));
        Assert.False(noRole.Result.Succeeded);
        Assert.Contains(ItProjectRemarkService.NoRoleMessage, noRole.Result.Errors);

        var empty = await db.Remarks().AddAsync(id, new ItProjectRemarkInput(" ", new DateOnly(2026, 11, 8)), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.Contains("Write the remark.", empty.Result.Errors);

        var missingProject = await db.Remarks().AddAsync(id + 50, new ItProjectRemarkInput("Fine.", new DateOnly(2026, 11, 8)), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.Contains(ItProjectCommandService.NotFoundMessage, missingProject.Result.Errors);

        Assert.Empty(await db.Remarks().ListAsync(id));
    }

    [Fact]
    public async Task List_OrdersByEventDateThenWrittenNewestFirstAndFallsBackToTheUserId()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var remarks = db.Remarks();
        Assert.True((await remarks.AddAsync(id, new ItProjectRemarkInput("Older event, written first.", new DateOnly(2026, 1, 14)), ItProjectTestDb.RemarkActor("comdt-1", RoleNames.Comdt))).Result.Succeeded);
        db.Clock.Set(ItProjectTestDb.Now.AddMinutes(1));
        Assert.True((await remarks.AddAsync(id, new ItProjectRemarkInput("Newer event.", new DateOnly(2026, 11, 2)), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito))).Result.Succeeded);
        db.Clock.Set(ItProjectTestDb.Now.AddMinutes(2));
        Assert.True((await remarks.AddAsync(id, new ItProjectRemarkInput("Same newer event, written later.", new DateOnly(2026, 11, 2)), ItProjectTestDb.RemarkActor("mco-1", RoleNames.Mco))).Result.Succeeded);

        var list = await db.Remarks().ListAsync(id);

        Assert.Equal(new[] { "Same newer event, written later.", "Newer event.", "Older event, written first." }, list.Select(remark => remark.Body));
        Assert.Equal("mco-1", list[0].AuthorName); // no user row: the id stands in
    }

    [Fact]
    public async Task Edit_IsForTheAuthorOnlyAndKeepsASnapshot()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var (added, remarkId) = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("PNC concluded at 46.8 L.", new DateOnly(2026, 3, 12)), ItProjectTestDb.RemarkActor("mco-1", RoleNames.Mco));
        Assert.True(added.Succeeded);
        var rowVersion = (await db.Remarks().ListAsync(id)).Single().RowVersion;

        var byOther = await db.Remarks().EditAsync(id, remarkId, new ItProjectRemarkInput("Changed by someone else.", new DateOnly(2026, 3, 12)), rowVersion, ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD));
        Assert.False(byOther.Succeeded);
        Assert.Contains(ItProjectRemarkService.NotAuthorMessage, byOther.Errors);

        db.Clock.Set(ItProjectTestDb.Now.AddHours(1));
        var byAuthor = await db.Remarks().EditAsync(id, remarkId, new ItProjectRemarkInput("PNC concluded at 46.80 L against L1 of 49.2 L.", new DateOnly(2026, 3, 13)), rowVersion, ItProjectTestDb.RemarkActor("mco-1", RoleNames.Mco));
        Assert.True(byAuthor.Succeeded, string.Join("; ", byAuthor.Errors));

        var remark = (await db.Remarks().ListAsync(id)).Single();
        Assert.Equal("PNC concluded at 46.80 L against L1 of 49.2 L.", remark.Body);
        Assert.Equal(new DateOnly(2026, 3, 13), remark.EventDate);
        Assert.Equal(ItProjectRemarkType.General, remark.Type);
        Assert.NotNull(remark.LastEditedAtUtc);
        Assert.NotEqual(rowVersion, remark.RowVersion);

        var stale = await db.Remarks().EditAsync(id, remarkId, new ItProjectRemarkInput("Stale.", new DateOnly(2026, 3, 13)), rowVersion, ItProjectTestDb.RemarkActor("mco-1", RoleNames.Mco));
        Assert.True(stale.ConcurrencyConflict);

        await using var context = db.NewContext();
        var actions = await context.ItProjectRemarkAudits.Where(audit => audit.RemarkId == remarkId).OrderBy(audit => audit.ActionAtUtc).Select(audit => audit.Action).ToListAsync();
        Assert.Equal(new[] { ItProjectRemarkAuditAction.Created, ItProjectRemarkAuditAction.Edited }, actions);
        Assert.Equal("ItProjects.RemarkEdited", db.Audit.Entries.Last().Action);
    }

    [Fact]
    public async Task Delete_IsForTheAuthorOrAnAdminAndIsSoft()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var (first, firstId) = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("Mine.", new DateOnly(2026, 11, 8)), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        var (second, secondId) = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("Also mine.", new DateOnly(2026, 11, 8)), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.True(first.Succeeded && second.Succeeded);
        var versions = (await db.Remarks().ListAsync(id)).ToDictionary(remark => remark.Id, remark => remark.RowVersion);

        var byOther = await db.Remarks().DeleteAsync(id, firstId, versions[firstId], ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD));
        Assert.False(byOther.Succeeded);
        Assert.Contains(ItProjectRemarkService.NotAuthorOrAdminMessage, byOther.Errors);

        var byAuthor = await db.Remarks().DeleteAsync(id, firstId, versions[firstId], ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.True(byAuthor.Succeeded);
        Assert.Equal("true", db.Audit.Entries.Last().Data["DeletedByAuthor"]);

        var byAdmin = await db.Remarks().DeleteAsync(id, secondId, versions[secondId], ItProjectTestDb.RemarkActor("admin-1", null, isAdmin: true));
        Assert.True(byAdmin.Succeeded);
        Assert.Equal("false", db.Audit.Entries.Last().Data["DeletedByAuthor"]);

        Assert.Empty(await db.Remarks().ListAsync(id));
        await using var context = db.NewContext();
        Assert.Equal(2, await context.ItProjectRemarks.CountAsync(remark => remark.ItProjectId == id && remark.IsDeleted));
        Assert.Equal("admin-1", (await context.ItProjectRemarks.SingleAsync(remark => remark.Id == secondId)).DeletedByUserId);
        var adminSnapshot = await context.ItProjectRemarkAudits.Where(audit => audit.RemarkId == secondId && audit.Action == ItProjectRemarkAuditAction.Deleted).SingleAsync();
        Assert.Equal("Admin", adminSnapshot.ActorRole);
        Assert.True(adminSnapshot.SnapshotIsDeleted);

        var again = await db.Remarks().DeleteAsync(id, firstId, versions[firstId], ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.Contains(ItProjectRemarkService.NotFoundMessage, again.Errors);
    }

    [Fact]
    public async Task EditAndDelete_AreBoundToTheProjectAndRefuseADeletedParent()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (projectA, rowVersionA) = await db.SeedAsync("A", "IT PPP A");
        var (projectB, _) = await db.SeedAsync("B", "IT PPP B");
        var (added, remarkId) = await db.Remarks().AddAsync(projectA, new ItProjectRemarkInput("On A.", new DateOnly(2026, 11, 8)), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.True(added.Succeeded);
        var rowVersion = (await db.Remarks().ListAsync(projectA)).Single().RowVersion;
        var author = ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito);

        // Through another project's route the remark is not found, even for its author.
        var viaOtherProject = await db.Remarks().EditAsync(projectB, remarkId, new ItProjectRemarkInput("Moved?", new DateOnly(2026, 11, 8)), rowVersion, author);
        Assert.Contains(ItProjectRemarkService.NotFoundMessage, viaOtherProject.Errors);
        var deleteViaOther = await db.Remarks().DeleteAsync(projectB, remarkId, rowVersion, ItProjectTestDb.RemarkActor("admin-1", null, isAdmin: true));
        Assert.Contains(ItProjectRemarkService.NotFoundMessage, deleteViaOther.Errors);

        // Once the parent project is deleted its remarks are frozen.
        Assert.True((await db.Commands().DeleteAsync(projectA, rowVersionA, ItProjectTestDb.Actor("admin-1"))).Succeeded);
        var afterParentDeleted = await db.Remarks().EditAsync(projectA, remarkId, new ItProjectRemarkInput("Still editable?", new DateOnly(2026, 11, 8)), rowVersion, author);
        Assert.Contains(ItProjectRemarkService.NotFoundMessage, afterParentDeleted.Errors);
        var deleteAfterParentDeleted = await db.Remarks().DeleteAsync(projectA, remarkId, rowVersion, author);
        Assert.Contains(ItProjectRemarkService.NotFoundMessage, deleteAfterParentDeleted.Errors);

        await using var context = db.NewContext();
        var stored = await context.ItProjectRemarks.SingleAsync(remark => remark.Id == remarkId);
        Assert.Equal("On A.", stored.Body);
        Assert.False(stored.IsDeleted);
    }

    private static async Task AddUserAsync(ItProjectTestDb db, string id, string fullName)
    {
        await using var context = db.NewContext();
        context.Users.Add(new ApplicationUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), FullName = fullName });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Conference_IsForTheComdtAndHoDOnly()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();

        foreach (var role in new[] { RoleNames.Ito, RoleNames.Mco })
        {
            var refused = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("Direction.", new DateOnly(2026, 11, 8), ItProjectRemarkType.Conference), ItProjectTestDb.RemarkActor($"{role}-1", role));
            Assert.False(refused.Result.Succeeded);
            Assert.Contains(ItProjectRemarkService.ConferenceRoleMessage, refused.Result.Errors);
        }

        var byComdt = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("Close COB this week.", new DateOnly(2026, 11, 8), ItProjectRemarkType.Conference), ItProjectTestDb.RemarkActor("comdt-1", RoleNames.Comdt));
        var byHod = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("Get the PNC date.", new DateOnly(2026, 11, 8), ItProjectRemarkType.Conference), ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD));
        Assert.True(byComdt.Result.Succeeded);
        Assert.True(byHod.Result.Succeeded);

        var list = await db.Remarks().ListAsync(id);
        Assert.All(list, remark => Assert.Equal(ItProjectRemarkType.Conference, remark.Type));
        Assert.Equal("Conference", db.Audit.Entries.Last().Data!["Type"]);

        await using var context = db.NewContext();
        Assert.All(await context.ItProjectRemarkAudits.ToListAsync(), snapshot => Assert.Equal(ItProjectRemarkType.Conference, snapshot.SnapshotType));
    }

    [Fact]
    public async Task Conference_KeepsItsTypeOnEdit_AndOnlyTheComdtOrHoDAuthorChangesIt()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var (added, remarkId) = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("Close COB this week.", new DateOnly(2026, 11, 8), ItProjectRemarkType.Conference), ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD));
        Assert.True(added.Succeeded);
        var rowVersion = (await db.Remarks().ListAsync(id)).Single().RowVersion;

        // The same user, no longer holding a conference role, cannot change or delete it.
        var demotedEdit = await db.Remarks().EditAsync(id, remarkId, new ItProjectRemarkInput("Changed.", new DateOnly(2026, 11, 8)), rowVersion, ItProjectTestDb.RemarkActor("hod-1", RoleNames.Ito));
        Assert.Contains(ItProjectRemarkService.ConferenceRoleMessage, demotedEdit.Errors);
        var demotedDelete = await db.Remarks().DeleteAsync(id, remarkId, rowVersion, ItProjectTestDb.RemarkActor("hod-1", RoleNames.Ito));
        Assert.Contains(ItProjectRemarkService.ConferenceRoleMessage, demotedDelete.Errors);

        var edited = await db.Remarks().EditAsync(id, remarkId, new ItProjectRemarkInput("Close COB by Friday.", new DateOnly(2026, 11, 9)), rowVersion, ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD));
        Assert.True(edited.Succeeded, string.Join("; ", edited.Errors));
        var remark = (await db.Remarks().ListAsync(id)).Single();
        Assert.Equal(ItProjectRemarkType.Conference, remark.Type);   // the type does not change on edit

        // An administrator may still remove it as moderation.
        var byAdmin = await db.Remarks().DeleteAsync(id, remarkId, remark.RowVersion, ItProjectTestDb.RemarkActor("admin-1", null, isAdmin: true));
        Assert.True(byAdmin.Succeeded, string.Join("; ", byAdmin.Errors));
    }

    [Fact]
    public async Task Edit_LeavesALegacyStageAsItWas()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var (_, remarkId) = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("Old remark.", new DateOnly(2026, 3, 1)), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        await using (var context = db.NewContext())
        {
            var row = await context.ItProjectRemarks.SingleAsync(remark => remark.Id == remarkId);
            row.StageCode = "PNC"; // written before remarks stopped taking a stage
            await context.SaveChangesAsync();
        }

        var rowVersion = (await db.Remarks().ListAsync(id)).Single().RowVersion;
        var edited = await db.Remarks().EditAsync(id, remarkId, new ItProjectRemarkInput("Old remark, corrected.", new DateOnly(2026, 3, 1)), rowVersion, ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.True(edited.Succeeded);

        await using var check = db.NewContext();
        Assert.Equal("PNC", (await check.ItProjectRemarks.SingleAsync(remark => remark.Id == remarkId)).StageCode);
    }
}

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
            new ItProjectRemarkInput("  Confirm the uplink optics are in the SO scope.  ", new DateOnly(2026, 10, 28), "SO"),
            ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var list = await db.Remarks().ListAsync(id);
        var remark = Assert.Single(list);
        Assert.Equal(remarkId, remark.Id);
        Assert.Equal("Col Sharma", remark.AuthorName);
        Assert.Equal(RoleNames.HoD, remark.AuthorRole);
        Assert.Equal("Confirm the uplink optics are in the SO scope.", remark.Body);
        Assert.Equal(new DateOnly(2026, 10, 28), remark.EventDate);
        Assert.Equal("SO", remark.StageCode);
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
        Assert.Equal("SO", audit.Data["StageCode"]);
    }

    [Fact]
    public async Task Add_NeedsARemarkRoleABodyAndAKnownStage()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();

        var noRole = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("Fine.", new DateOnly(2026, 11, 8), null), ItProjectTestDb.RemarkActor("admin-1", null, isAdmin: true));
        Assert.False(noRole.Result.Succeeded);
        Assert.Contains(ItProjectRemarkService.NoRoleMessage, noRole.Result.Errors);

        var empty = await db.Remarks().AddAsync(id, new ItProjectRemarkInput(" ", new DateOnly(2026, 11, 8), null), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.Contains("Write the remark.", empty.Result.Errors);

        var badStage = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("Fine.", new DateOnly(2026, 11, 8), "XYZ"), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.Contains("Choose one of the project's stages, or the whole project.", badStage.Result.Errors);

        var missingProject = await db.Remarks().AddAsync(id + 50, new ItProjectRemarkInput("Fine.", new DateOnly(2026, 11, 8), null), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.Contains(ItProjectCommandService.NotFoundMessage, missingProject.Result.Errors);

        Assert.Empty(await db.Remarks().ListAsync(id));
    }

    [Fact]
    public async Task List_OrdersByEventDateThenWrittenNewestFirstAndFallsBackToTheUserId()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var remarks = db.Remarks();
        Assert.True((await remarks.AddAsync(id, new ItProjectRemarkInput("Older event, written first.", new DateOnly(2026, 1, 14), null), ItProjectTestDb.RemarkActor("comdt-1", RoleNames.Comdt))).Result.Succeeded);
        db.Clock.Set(ItProjectTestDb.Now.AddMinutes(1));
        Assert.True((await remarks.AddAsync(id, new ItProjectRemarkInput("Newer event.", new DateOnly(2026, 11, 2), "SO"), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito))).Result.Succeeded);
        db.Clock.Set(ItProjectTestDb.Now.AddMinutes(2));
        Assert.True((await remarks.AddAsync(id, new ItProjectRemarkInput("Same newer event, written later.", new DateOnly(2026, 11, 2), null), ItProjectTestDb.RemarkActor("mco-1", RoleNames.Mco))).Result.Succeeded);

        var list = await db.Remarks().ListAsync(id);

        Assert.Equal(new[] { "Same newer event, written later.", "Newer event.", "Older event, written first." }, list.Select(remark => remark.Body));
        Assert.Equal("mco-1", list[0].AuthorName); // no user row: the id stands in
    }

    [Fact]
    public async Task Edit_IsForTheAuthorOnlyAndKeepsASnapshot()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var (added, remarkId) = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("PNC concluded at 46.8 L.", new DateOnly(2026, 3, 12), "PNC"), ItProjectTestDb.RemarkActor("mco-1", RoleNames.Mco));
        Assert.True(added.Succeeded);
        var rowVersion = (await db.Remarks().ListAsync(id)).Single().RowVersion;

        var byOther = await db.Remarks().EditAsync(remarkId, new ItProjectRemarkInput("Changed by someone else.", new DateOnly(2026, 3, 12), "PNC"), rowVersion, ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD));
        Assert.False(byOther.Succeeded);
        Assert.Contains(ItProjectRemarkService.NotAuthorMessage, byOther.Errors);

        db.Clock.Set(ItProjectTestDb.Now.AddHours(1));
        var byAuthor = await db.Remarks().EditAsync(remarkId, new ItProjectRemarkInput("PNC concluded at 46.80 L against L1 of 49.2 L.", new DateOnly(2026, 3, 13), null), rowVersion, ItProjectTestDb.RemarkActor("mco-1", RoleNames.Mco));
        Assert.True(byAuthor.Succeeded, string.Join("; ", byAuthor.Errors));

        var remark = (await db.Remarks().ListAsync(id)).Single();
        Assert.Equal("PNC concluded at 46.80 L against L1 of 49.2 L.", remark.Body);
        Assert.Equal(new DateOnly(2026, 3, 13), remark.EventDate);
        Assert.Null(remark.StageCode);
        Assert.NotNull(remark.LastEditedAtUtc);
        Assert.NotEqual(rowVersion, remark.RowVersion);

        var stale = await db.Remarks().EditAsync(remarkId, new ItProjectRemarkInput("Stale.", new DateOnly(2026, 3, 13), null), rowVersion, ItProjectTestDb.RemarkActor("mco-1", RoleNames.Mco));
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
        var (first, firstId) = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("Mine.", new DateOnly(2026, 11, 8), null), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        var (second, secondId) = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("Also mine.", new DateOnly(2026, 11, 8), null), ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.True(first.Succeeded && second.Succeeded);
        var versions = (await db.Remarks().ListAsync(id)).ToDictionary(remark => remark.Id, remark => remark.RowVersion);

        var byOther = await db.Remarks().DeleteAsync(firstId, versions[firstId], ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD));
        Assert.False(byOther.Succeeded);
        Assert.Contains(ItProjectRemarkService.NotAuthorOrAdminMessage, byOther.Errors);

        var byAuthor = await db.Remarks().DeleteAsync(firstId, versions[firstId], ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.True(byAuthor.Succeeded);
        Assert.Equal("true", db.Audit.Entries.Last().Data["DeletedByAuthor"]);

        var byAdmin = await db.Remarks().DeleteAsync(secondId, versions[secondId], ItProjectTestDb.RemarkActor("admin-1", null, isAdmin: true));
        Assert.True(byAdmin.Succeeded);
        Assert.Equal("false", db.Audit.Entries.Last().Data["DeletedByAuthor"]);

        Assert.Empty(await db.Remarks().ListAsync(id));
        await using var context = db.NewContext();
        Assert.Equal(2, await context.ItProjectRemarks.CountAsync(remark => remark.ItProjectId == id && remark.IsDeleted));
        Assert.Equal("admin-1", (await context.ItProjectRemarks.SingleAsync(remark => remark.Id == secondId)).DeletedByUserId);
        var adminSnapshot = await context.ItProjectRemarkAudits.Where(audit => audit.RemarkId == secondId && audit.Action == ItProjectRemarkAuditAction.Deleted).SingleAsync();
        Assert.Equal("Admin", adminSnapshot.ActorRole);
        Assert.True(adminSnapshot.SnapshotIsDeleted);

        var again = await db.Remarks().DeleteAsync(firstId, versions[firstId], ItProjectTestDb.RemarkActor("ito-1", RoleNames.Ito));
        Assert.Contains(ItProjectRemarkService.NotFoundMessage, again.Errors);
    }

    private static async Task AddUserAsync(ItProjectTestDb db, string id, string fullName)
    {
        await using var context = db.NewContext();
        context.Users.Add(new ApplicationUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), FullName = fullName });
        await context.SaveChangesAsync();
    }
}

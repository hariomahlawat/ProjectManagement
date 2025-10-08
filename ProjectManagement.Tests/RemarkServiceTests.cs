using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Remarks;
using ProjectManagement.Tests.Fakes;

namespace ProjectManagement.Tests;

public sealed class RemarkServiceTests
{
    [Fact]
    public async Task CreateRemarkAsync_AllowsInternalForRecognisedRole()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 10);
        await SeedStageAsync(db, 10, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 10, 1, 10, 0, 0);
        var service = CreateService(db, clock, out var notifier, out var metrics);

        var actor = new RemarkActorContext("user-1", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var request = new CreateRemarkRequest(
            ProjectId: 10,
            Actor: actor,
            Type: RemarkType.Internal,
            Scope: RemarkScope.General,
            Body: "<b>Hello</b><script>alert('x')</script>",
            EventDate: new DateOnly(2024, 9, 30),
            StageRef: StageCodes.FS,
            StageNameSnapshot: null,
            Meta: "{\"origin\":\"test\"}");

        var remark = await service.CreateRemarkAsync(request, CancellationToken.None);

        Assert.Equal("user-1", remark.AuthorUserId);
        Assert.Equal(RemarkActorRole.ProjectOfficer, remark.AuthorRole);
        Assert.Equal(RemarkType.Internal, remark.Type);
        Assert.Equal("<b>Hello</b>", remark.Body);
        Assert.Equal(new DateOnly(2024, 9, 30), remark.EventDate);
        Assert.Equal(StageCodes.FS, remark.StageRef);
        Assert.Equal(StageCodes.DisplayNameOf(StageCodes.FS), remark.StageNameSnapshot);

        var audit = await db.RemarkAudits.SingleAsync(a => a.RemarkId == remark.Id);
        Assert.Equal(RemarkAuditAction.Created, audit.Action);
        Assert.Equal("user-1", audit.ActorUserId);
        Assert.Equal(RemarkActorRole.ProjectOfficer, audit.ActorRole);
        Assert.Equal("{\"origin\":\"test\"}", audit.Meta);
        Assert.Equal("<b>Hello</b>", audit.SnapshotBody);

        Assert.Single(notifier.Notifications);
        Assert.Equal(remark.Id, notifier.Notifications[0].remark.Id);
        Assert.Equal(1, metrics.CreatedCount);
    }

    [Fact]
    public async Task CreateRemarkAsync_ResolvesMentions()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 101);
        await SeedStageAsync(db, 101, StageCodes.FS);

        db.Users.Add(new ApplicationUser
        {
            Id = "mention-1",
            UserName = "mention1@test.local",
            NormalizedUserName = "MENTION1@TEST.LOCAL",
            Email = "mention1@test.local",
            NormalizedEmail = "MENTION1@TEST.LOCAL",
            FullName = "Mention User",
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.ForIstDate(2024, 10, 1, 10, 0, 0);
        var service = CreateService(db, clock, out var notifier, out _);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });

        var request = new CreateRemarkRequest(
            ProjectId: 101,
            Actor: actor,
            Type: RemarkType.Internal,
            Scope: RemarkScope.General,
            Body: "Hello @[Mention User](user:mention-1) <script>alert('x')</script>",
            EventDate: new DateOnly(2024, 9, 30),
            StageRef: StageCodes.FS,
            StageNameSnapshot: null,
            Meta: null);

        var remark = await service.CreateRemarkAsync(request, CancellationToken.None);

        Assert.Contains("remark-mention", remark.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", remark.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Single(remark.Mentions);
        Assert.Equal("mention-1", remark.Mentions.First().UserId);
        Assert.Single(notifier.Notifications);
        Assert.Equal("mention-1", notifier.Notifications[0].remark.Mentions.First().UserId);
    }

    [Fact]
    public async Task CreateRemarkAsync_ThrowsWhenExternalWithoutPrivilege()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 11);
        await SeedStageAsync(db, 11, StageCodes.FS);
        var service = CreateService(db, FakeClock.ForIstDate(2024, 9, 15, 12, 0, 0), out _, out var metrics);
        var actor = new RemarkActorContext("user-2", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });

        var request = new CreateRemarkRequest(11, actor, RemarkType.External, RemarkScope.General, "Hello", new DateOnly(2024, 9, 14), StageCodes.FS, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRemarkAsync(request, CancellationToken.None));
        AssertPermissionDenied(metrics, "Create", "ExternalRequiresOverride");
    }

    [Fact]
    public async Task CreateRemarkAsync_ThrowsWhenStageMissing()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 12);
        var service = CreateService(db, FakeClock.ForIstDate(2024, 9, 10, 9, 0, 0), out _, out var metrics);
        var actor = new RemarkActorContext("user-3", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var request = new CreateRemarkRequest(12, actor, RemarkType.Internal, RemarkScope.General, "Hello", new DateOnly(2024, 9, 9), StageCodes.IPA, null, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRemarkAsync(request, CancellationToken.None));
        Assert.Equal(RemarkService.StageNotInProjectMessage, ex.Message);
        Assert.Empty(metrics.PermissionDenied);
    }

    [Fact]
    public async Task EditRemarkAsync_ThrowsWhenWindowExpiredWithoutOverride()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 20);
        await SeedStageAsync(db, 20, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 9, 0, 0);
        var service = CreateService(db, clock, out _, out var metrics);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(20, actor, RemarkType.Internal, RemarkScope.General, "Body", new DateOnly(2024, 8, 31), StageCodes.FS, null, null), CancellationToken.None);

        clock.Set(clock.UtcNow.AddHours(4));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EditRemarkAsync(remark.Id, new EditRemarkRequest(actor, "Updated", RemarkScope.General, new DateOnly(2024, 8, 30), StageCodes.FS, null, null, remark.RowVersion), CancellationToken.None));
        Assert.Equal(RemarkService.EditWindowMessage, ex.Message);
        Assert.Equal(1, metrics.EditWindowExpiredCount);
        AssertPermissionDenied(metrics, "Edit", "AuthorWindowExpired");
    }

    [Fact]
    public async Task EditRemarkAsync_AllowsOverrideAfterWindow()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 21);
        await SeedStageAsync(db, 21, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 9, 0, 0);
        var service = CreateService(db, clock, out _, out var metrics);
        var author = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(21, author, RemarkType.Internal, RemarkScope.General, "Body", new DateOnly(2024, 8, 30), StageCodes.FS, null, null), CancellationToken.None);

        clock.Set(clock.UtcNow.AddHours(4));
        var hodActor = new RemarkActorContext("hod", RemarkActorRole.HeadOfDepartment, new[] { RemarkActorRole.HeadOfDepartment, RemarkActorRole.ProjectOfficer });

        var updated = await service.EditRemarkAsync(remark.Id, new EditRemarkRequest(hodActor, "<i>Override</i>", RemarkScope.General, new DateOnly(2024, 8, 29), StageCodes.FS, null, "{\"reason\":\"override\"}", remark.RowVersion), CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("<i>Override</i>", updated!.Body);
        Assert.Equal(new DateOnly(2024, 8, 29), updated.EventDate);

        var audits = await db.RemarkAudits.Where(a => a.RemarkId == remark.Id).OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Equal(RemarkAuditAction.Edited, audits.Last().Action);
        Assert.Equal(RemarkActorRole.HeadOfDepartment, audits.Last().ActorRole);
        Assert.Equal("{\"reason\":\"override\"}", audits.Last().Meta);
    }

    [Fact]
    public async Task EditRemarkAsync_SynchronisesMentions()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 23);
        await SeedStageAsync(db, 23, StageCodes.FS);

        db.Users.AddRange(
            new ApplicationUser
            {
                Id = "mention-a",
                UserName = "mention.a@test.local",
                NormalizedUserName = "MENTION.A@TEST.LOCAL",
                Email = "mention.a@test.local",
                NormalizedEmail = "MENTION.A@TEST.LOCAL",
                FullName = "Mention Alpha",
                SecurityStamp = Guid.NewGuid().ToString()
            },
            new ApplicationUser
            {
                Id = "mention-b",
                UserName = "mention.b@test.local",
                NormalizedUserName = "MENTION.B@TEST.LOCAL",
                Email = "mention.b@test.local",
                NormalizedEmail = "MENTION.B@TEST.LOCAL",
                FullName = "Mention Beta",
                SecurityStamp = Guid.NewGuid().ToString()
            });
        await db.SaveChangesAsync();

        var clock = FakeClock.ForIstDate(2024, 9, 1, 9, 0, 0);
        var service = CreateService(db, clock, out _, out _);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });

        var remark = await service.CreateRemarkAsync(
            new CreateRemarkRequest(
                ProjectId: 23,
                Actor: actor,
                Type: RemarkType.Internal,
                Scope: RemarkScope.General,
                Body: "Initial @[Mention Alpha](user:mention-a)",
                EventDate: new DateOnly(2024, 8, 31),
                StageRef: StageCodes.FS,
                StageNameSnapshot: null,
                Meta: null),
            CancellationToken.None);

        Assert.Single(remark.Mentions);
        Assert.Equal("mention-a", remark.Mentions.First().UserId);

        var updated = await service.EditRemarkAsync(
            remark.Id,
            new EditRemarkRequest(
                Actor: actor,
                Body: "Updated @[Mention Beta](user:mention-b)",
                Scope: RemarkScope.General,
                EventDate: new DateOnly(2024, 8, 30),
                StageRef: StageCodes.FS,
                StageNameSnapshot: null,
                Meta: null,
                RowVersion: remark.RowVersion),
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("mention-b", Assert.Single(updated!.Mentions).UserId);
        Assert.DoesNotContain("mention-a", updated.Mentions.Select(m => m.UserId));
        Assert.Contains("remark-mention", updated.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditRemarkAsync_ThrowsOnConcurrencyConflict()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 22);
        await SeedStageAsync(db, 22, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 9, 0, 0);
        var service = CreateService(db, clock, out _, out var metrics);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(22, actor, RemarkType.Internal, RemarkScope.General, "Body", new DateOnly(2024, 8, 30), StageCodes.FS, null, null), CancellationToken.None);

        var staleVersion = remark.RowVersion.ToArray();

        var overrideActor = new RemarkActorContext("hod", RemarkActorRole.HeadOfDepartment, new[] { RemarkActorRole.HeadOfDepartment, RemarkActorRole.ProjectOfficer });
        await service.EditRemarkAsync(remark.Id, new EditRemarkRequest(overrideActor, "Updated", RemarkScope.General, new DateOnly(2024, 8, 29), StageCodes.FS, null, null, remark.RowVersion), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EditRemarkAsync(remark.Id, new EditRemarkRequest(actor, "Second", RemarkScope.General, new DateOnly(2024, 8, 28), StageCodes.FS, null, null, staleVersion), CancellationToken.None));

        Assert.Equal(RemarkService.ConcurrencyConflictMessage, ex.Message);
    }

    [Fact]
    public async Task SoftDeleteRemarkAsync_AllowsAuthorWithinWindow()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 30);
        await SeedStageAsync(db, 30, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 8, 0, 0);
        var service = CreateService(db, clock, out _, out var metrics);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(30, actor, RemarkType.Internal, RemarkScope.General, "Body", new DateOnly(2024, 8, 31), StageCodes.FS, null, null), CancellationToken.None);

        var deleted = await service.SoftDeleteRemarkAsync(remark.Id, new SoftDeleteRemarkRequest(actor, "{\"reason\":\"cleanup\"}", remark.RowVersion), CancellationToken.None);

        Assert.True(deleted);
        var reloaded = await db.Remarks.SingleAsync(r => r.Id == remark.Id);
        Assert.True(reloaded.IsDeleted);
        Assert.Equal("author", reloaded.DeletedByUserId);
        Assert.Equal(RemarkActorRole.ProjectOfficer, reloaded.DeletedByRole);

        var audit = await db.RemarkAudits.OrderByDescending(a => a.Id).FirstAsync();
        Assert.Equal(RemarkAuditAction.Deleted, audit.Action);
        Assert.Equal("{\"reason\":\"cleanup\"}", audit.Meta);
        Assert.Equal(1, metrics.DeletedCount);
    }

    [Fact]
    public async Task SoftDeleteRemarkAsync_ThrowsWhenNotAuthorAndNoOverride()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 31);
        await SeedStageAsync(db, 31, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 8, 0, 0);
        var service = CreateService(db, clock, out _, out var metrics);
        var author = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(31, author, RemarkType.Internal, RemarkScope.General, "Body", new DateOnly(2024, 8, 31), StageCodes.FS, null, null), CancellationToken.None);

        var otherActor = new RemarkActorContext("other", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SoftDeleteRemarkAsync(remark.Id, new SoftDeleteRemarkRequest(otherActor, null, remark.RowVersion), CancellationToken.None));
        Assert.Equal(RemarkService.PermissionDeniedMessage, ex.Message);
        AssertPermissionDenied(metrics, "SoftDelete", "NotAuthor");
    }

    [Fact]
    public async Task SoftDeleteRemarkAsync_ThrowsWhenAuthorAfterWindow()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 33);
        await SeedStageAsync(db, 33, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 8, 0, 0);
        var service = CreateService(db, clock, out _, out var metrics);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(33, actor, RemarkType.Internal, RemarkScope.General, "Body", new DateOnly(2024, 8, 31), StageCodes.FS, null, null), CancellationToken.None);

        clock.Set(clock.UtcNow.AddHours(4));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SoftDeleteRemarkAsync(remark.Id, new SoftDeleteRemarkRequest(actor, null, remark.RowVersion), CancellationToken.None));

        Assert.Equal(RemarkService.DeleteWindowMessage, ex.Message);
        AssertPermissionDenied(metrics, "SoftDelete", "AuthorWindowExpired");
    }

    [Fact]
    public async Task SoftDeleteRemarkAsync_ThrowsOnConcurrencyConflict()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 32);
        await SeedStageAsync(db, 32, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 8, 0, 0);
        var service = CreateService(db, clock, out _, out var metrics);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(32, actor, RemarkType.Internal, RemarkScope.General, "Body", new DateOnly(2024, 8, 31), StageCodes.FS, null, null), CancellationToken.None);

        var staleVersion = remark.RowVersion.ToArray();

        await service.SoftDeleteRemarkAsync(remark.Id, new SoftDeleteRemarkRequest(actor, null, remark.RowVersion), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SoftDeleteRemarkAsync(remark.Id, new SoftDeleteRemarkRequest(actor, null, staleVersion), CancellationToken.None));

        Assert.Equal(RemarkService.ConcurrencyConflictMessage, ex.Message);
    }

    [Fact]
    public async Task ListRemarksAsync_FiltersAndIncludesDeletedForAdmin()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 40);
        await SeedStageAsync(db, 40, StageCodes.FS);
        await SeedStageAsync(db, 40, StageCodes.IPA);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 8, 0, 0);
        var service = CreateService(db, clock, out _, out var metrics);
        var author = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var admin = new RemarkActorContext("admin", RemarkActorRole.Administrator, new[] { RemarkActorRole.Administrator, RemarkActorRole.ProjectOfficer });

        await service.CreateRemarkAsync(new CreateRemarkRequest(40, author, RemarkType.Internal, RemarkScope.General, "One", new DateOnly(2024, 8, 30), StageCodes.FS, null, null), CancellationToken.None);
        var remarkToDelete = await service.CreateRemarkAsync(new CreateRemarkRequest(40, author, RemarkType.Internal, RemarkScope.General, "Two", new DateOnly(2024, 8, 29), StageCodes.IPA, null, null), CancellationToken.None);
        await service.SoftDeleteRemarkAsync(remarkToDelete.Id, new SoftDeleteRemarkRequest(admin, null, remarkToDelete.RowVersion), CancellationToken.None);

        var result = await service.ListRemarksAsync(new ListRemarksRequest(40, admin, Type: RemarkType.Internal, AuthorRole: null, StageRef: null, FromDate: new DateOnly(2024, 8, 29), ToDate: new DateOnly(2024, 8, 30), IncludeDeleted: true, Page: 1, PageSize: 10), CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, r => r.IsDeleted);
    }

    [Fact]
    public async Task ListRemarksAsync_HonoursMineFilter()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 41);
        await SeedStageAsync(db, 41, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 8, 0, 0);
        var service = CreateService(db, clock, out _, out _);
        var author = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var other = new RemarkActorContext("other", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });

        await service.CreateRemarkAsync(new CreateRemarkRequest(41, author, RemarkType.Internal, RemarkScope.General, "Mine", new DateOnly(2024, 8, 30), StageCodes.FS, null, null), CancellationToken.None);
        await service.CreateRemarkAsync(new CreateRemarkRequest(41, other, RemarkType.Internal, RemarkScope.General, "Theirs", new DateOnly(2024, 8, 29), StageCodes.FS, null, null), CancellationToken.None);

        var mine = await service.ListRemarksAsync(new ListRemarksRequest(41, author, Mine: true), CancellationToken.None);

        Assert.Single(mine.Items);
        Assert.Equal("Mine", mine.Items[0].Body);
    }

    [Fact]
    public async Task CreateRemarkAsync_PersistsScope()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 42);
        await SeedStageAsync(db, 42, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 8, 0, 0);
        var service = CreateService(db, clock, out _, out _);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });

        var request = new CreateRemarkRequest(42, actor, RemarkType.Internal, RemarkScope.TransferOfTechnology, "ToT remark", new DateOnly(2024, 8, 30), StageCodes.FS, null, null);
        var remark = await service.CreateRemarkAsync(request, CancellationToken.None);

        Assert.Equal(RemarkScope.TransferOfTechnology, remark.Scope);

        var audit = await db.RemarkAudits.SingleAsync(a => a.RemarkId == remark.Id);
        Assert.Equal(RemarkScope.TransferOfTechnology, audit.SnapshotScope);
    }

    [Fact]
    public async Task ListRemarksAsync_FiltersByScope()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 43);
        await SeedStageAsync(db, 43, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 8, 0, 0);
        var service = CreateService(db, clock, out _, out _);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });

        await service.CreateRemarkAsync(new CreateRemarkRequest(43, actor, RemarkType.Internal, RemarkScope.General, "General remark", new DateOnly(2024, 8, 30), StageCodes.FS, null, null), CancellationToken.None);
        await service.CreateRemarkAsync(new CreateRemarkRequest(43, actor, RemarkType.Internal, RemarkScope.TransferOfTechnology, "ToT remark", new DateOnly(2024, 8, 29), StageCodes.FS, null, null), CancellationToken.None);

        var general = await service.ListRemarksAsync(new ListRemarksRequest(43, actor, Scope: RemarkScope.General), CancellationToken.None);
        Assert.Single(general.Items);
        Assert.All(general.Items, r => Assert.Equal(RemarkScope.General, r.Scope));

        var tot = await service.ListRemarksAsync(new ListRemarksRequest(43, actor, Scope: RemarkScope.TransferOfTechnology), CancellationToken.None);
        Assert.Single(tot.Items);
        Assert.All(tot.Items, r => Assert.Equal(RemarkScope.TransferOfTechnology, r.Scope));
    }

    [Fact]
    public async Task GetRemarkAuditAsync_ThrowsForNonAdmin()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 50);
        await SeedStageAsync(db, 50, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 9, 0, 0);
        var service = CreateService(db, clock, out _, out var metrics);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(50, actor, RemarkType.Internal, RemarkScope.General, "Body", new DateOnly(2024, 8, 31), StageCodes.FS, null, null), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetRemarkAuditAsync(remark.Id, actor, CancellationToken.None));
        AssertPermissionDenied(metrics, "GetAudit", "AdminOnly");
    }

    [Fact]
    public async Task GetRemarkAuditAsync_ReturnsEntriesForAdmin()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 51);
        await SeedStageAsync(db, 51, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 9, 0, 0);
        var service = CreateService(db, clock, out _, out _);
        var author = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(51, author, RemarkType.Internal, RemarkScope.General, "Body", new DateOnly(2024, 8, 31), StageCodes.FS, null, null), CancellationToken.None);

        var admin = new RemarkActorContext("admin", RemarkActorRole.Administrator, new[] { RemarkActorRole.Administrator });
        var audits = await service.GetRemarkAuditAsync(remark.Id, admin, CancellationToken.None);

        Assert.Single(audits);
        Assert.Equal(RemarkAuditAction.Created, audits[0].Action);
    }

    private static RemarkService CreateService(ApplicationDbContext db, IClock clock, out TestRemarkNotificationService notifier, out TestRemarkMetrics metrics)
    {
        notifier = new TestRemarkNotificationService();
        metrics = new TestRemarkMetrics();
        var userManager = CreateUserManager(db);
        return new RemarkService(db, clock, NullLogger<RemarkService>.Instance, notifier, metrics, userManager);
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db);
        return new UserManager<ApplicationUser>(
            store,
            new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static async Task<SqliteContextScope> CreateContextAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        return new SqliteContextScope(context, connection);
    }

    private static async Task SeedProjectAsync(ApplicationDbContext db, int projectId)
    {
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"Project {projectId}",
            CreatedByUserId = "seed"
        });
        await db.SaveChangesAsync();
    }

    private sealed class TestRemarkNotificationService : IRemarkNotificationService
    {
        public List<(Remark remark, RemarkActorContext actor, RemarkProjectInfo project)> Notifications { get; } = new();

        public Task NotifyRemarkCreatedAsync(
            Remark remark,
            RemarkActorContext actor,
            RemarkProjectInfo project,
            CancellationToken cancellationToken = default)
        {
            Notifications.Add((remark, actor, project));
            return Task.CompletedTask;
        }
    }

    private sealed class TestRemarkMetrics : IRemarkMetrics
    {
        private readonly List<(string Action, string Reason)> _permissionDenied = new();

        public int CreatedCount { get; private set; }

        public int DeletedCount { get; private set; }

        public int EditWindowExpiredCount { get; private set; }

        public IReadOnlyList<(string Action, string Reason)> PermissionDenied => _permissionDenied;

        public void RecordCreated() => CreatedCount++;

        public void RecordDeleted() => DeletedCount++;

        public void RecordEditDeniedWindowExpired(string action) => EditWindowExpiredCount++;

        public void RecordPermissionDenied(string action, string reason) => _permissionDenied.Add((action, reason));
    }

    private static void AssertPermissionDenied(TestRemarkMetrics metrics, string action, string reason)
    {
        Assert.Contains(metrics.PermissionDenied, entry => entry.Action == action && entry.Reason == reason);
    }

    private static async Task SeedStageAsync(ApplicationDbContext db, int projectId, string stageCode)
    {
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = projectId,
            StageCode = stageCode,
            SortOrder = 1,
            Status = StageStatus.NotStarted
        });
        await db.SaveChangesAsync();
    }
    private sealed class SqliteContextScope : IAsyncDisposable
    {
        public SqliteContextScope(ApplicationDbContext db, SqliteConnection connection)
        {
            Db = db;
            _connection = connection;
        }

        public ApplicationDbContext Db { get; }

        private readonly SqliteConnection _connection;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

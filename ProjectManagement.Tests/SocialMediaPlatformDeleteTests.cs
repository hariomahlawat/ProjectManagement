using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using Xunit;

namespace ProjectManagement.Tests;

// A value that records use is deactivated, never deleted (docs/ux/admin-review-2-fixes.md, PR 10).
// Since third-review PR 12 the platforms follow the shared list rule.
public sealed class SocialMediaPlatformDeleteTests
{
    [Fact]
    public async Task PlatformWithEvents_IsNotDeleted_AndTheListSaysHowManyUseIt()
    {
        await using var db = NewDb();
        var type = SocialMediaTestData.CreateEventType();
        var used = SocialMediaTestData.CreatePlatform(name: "Instagram");
        var unused = SocialMediaTestData.CreatePlatform(name: "Threads");
        db.SocialMediaEventTypes.Add(type);
        db.SocialMediaPlatforms.AddRange(used, unused);
        db.SocialMediaEvents.Add(SocialMediaTestData.CreateEvent(type.Id, used.Id));
        await db.SaveChangesAsync();

        var lists = Lists(db);

        var page = await lists.GetPageAsync(ReferenceListKeys.SocialMediaPlatforms, new ReferenceListRequest(null, null, 1, 25));
        Assert.Equal("1 event", page.Rows.Single(item => item.Id == used.Id.ToString()).UsedBy.Single().Text);
        Assert.True(page.Rows.Single(item => item.Id == unused.Id.ToString()).CanDelete);

        var refused = await lists.DeleteAsync(ReferenceListKeys.SocialMediaPlatforms, used.Id, used.RowVersion);
        Assert.Equal("EntityInUse", refused.ErrorCode);
        Assert.True(await db.SocialMediaPlatforms.AnyAsync(item => item.Id == used.Id));

        var deleted = await lists.DeleteAsync(ReferenceListKeys.SocialMediaPlatforms, unused.Id, unused.RowVersion);
        Assert.True(deleted.Succeeded, deleted.UserMessage);
        Assert.False(await db.SocialMediaPlatforms.AnyAsync(item => item.Id == unused.Id));
    }

    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ReferenceListService Lists(ApplicationDbContext db) =>
        new(db, new NoAudit(), new Allow(), new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "admin") }, "test")) }
        });

    private sealed class NoAudit : IAdminAuditService
    {
        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Allow : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, System.Collections.Generic.IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(AuthorizationResult.Success());
    }
}

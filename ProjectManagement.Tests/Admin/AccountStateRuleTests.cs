using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Pins one account-state rule across the state resolver, the Users list (tiles and status
/// filters) and Admin Home. Each user below has exactly one expected state; every surface must
/// put it in that state and no other.
/// </summary>
public sealed class AccountStateRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    // Id, expected state, then the account flags. Fresh users on every read, so no test
    // shares tracked entities with another.
    private static (string Id, AdminUserAccountState Expected, ApplicationUser User)[] Cases => new[]
    {
        ("active-plain", AdminUserAccountState.Active, User("active-plain")),
        ("active-lock-ended", AdminUserAccountState.Active, User("active-lock-ended", lockoutEnd: Now.AddMinutes(-1))),
        ("active-lock-ends-now", AdminUserAccountState.Active, User("active-lock-ends-now", lockoutEnd: Now)),
        ("must-change", AdminUserAccountState.MustChangePassword, User("must-change", mustChange: true)),
        ("must-change-lock-ended", AdminUserAccountState.MustChangePassword, User("must-change-lock-ended", mustChange: true, lockoutEnd: Now.AddHours(-2))),
        ("locked", AdminUserAccountState.TemporarilyLocked, User("locked", lockoutEnd: Now.AddMinutes(15))),
        ("locked-must-change", AdminUserAccountState.TemporarilyLocked, User("locked-must-change", mustChange: true, lockoutEnd: Now.AddMinutes(15))),
        ("disabled", AdminUserAccountState.Disabled, User("disabled", disabled: true)),
        ("disabled-and-locked", AdminUserAccountState.Disabled, User("disabled-and-locked", disabled: true, lockoutEnd: DateTimeOffset.MaxValue, mustChange: true)),
        ("pending", AdminUserAccountState.PendingDeletion, User("pending", pendingDeletion: true)),
        ("pending-disabled-locked", AdminUserAccountState.PendingDeletion, User("pending-disabled-locked", pendingDeletion: true, disabled: true, lockoutEnd: Now.AddDays(1), mustChange: true)),
    };

    private static readonly IReadOnlyDictionary<AdminUserAccountState, string> StatusFilters =
        new Dictionary<AdminUserAccountState, string>
        {
            [AdminUserAccountState.Active] = "active",
            [AdminUserAccountState.MustChangePassword] = "must-change-password",
            [AdminUserAccountState.TemporarilyLocked] = "locked",
            [AdminUserAccountState.Disabled] = "disabled",
            [AdminUserAccountState.PendingDeletion] = "pending-deletion"
        };

    [Fact]
    public void Resolver_PutsEachAccountInItsState()
    {
        var resolver = new UserAccountStateResolver();
        foreach (var (id, expected, user) in Cases)
        {
            Assert.True(expected == resolver.Resolve(user, Now).State, $"{id}: expected {expected}");
        }
    }

    [Fact]
    public async Task UsersList_FiltersAndTiles_MatchTheResolver()
    {
        await using var db = await SeedAsync();
        var service = new AdminUserQueryService(
            db,
            new UserAccountStateResolver(),
            new AdminTimeService(FakeClock.AtUtc(Now)),
            Options.Create(new UserLifecycleOptions { HardDeleteWindowHours = 24 }));

        var summary = await service.GetSummaryAsync(new AdminUserListRequest(null, null, null));
        Assert.Equal(Cases.Length, summary.Total);

        foreach (var (state, filter) in StatusFilters)
        {
            var expectedIds = Cases.Where(c => c.Expected == state).Select(c => c.Id).OrderBy(id => id).ToArray();

            var result = await service.GetAsync(new AdminUserListRequest(null, null, filter, PageSize: 100));
            Assert.Equal(expectedIds, result.Rows.Select(row => row.Id).OrderBy(id => id).ToArray());
            Assert.All(result.Rows, row => Assert.Equal(state, row.AccountState.State));
            Assert.Equal(expectedIds.Length, summary.CountFor(filter));

            var export = await service.GetForExportAsync(new AdminUserListRequest(null, null, filter));
            Assert.Equal(expectedIds, export.Select(row => row.Id).OrderBy(id => id).ToArray());
        }

        Assert.Equal(3, summary.Active);
        Assert.Equal(2, summary.MustChangePassword);
        Assert.Equal(2, summary.TemporarilyLocked);
        Assert.Equal(2, summary.Disabled);
        Assert.Equal(2, summary.PendingDeletion);
    }

    [Fact]
    public async Task Home_CountsMatchTheUsersList()
    {
        await using var db = await SeedAsync();
        var home = await new AdminDashboardService(db, new AdminTimeService(FakeClock.AtUtc(Now))).GetAsync();

        Assert.Equal(Cases.Length, home.Metrics.TotalUsers);
        Assert.Equal(3, home.Metrics.ActiveUsers);
        Assert.Equal(2, home.Metrics.MustChangePasswordUsers);
        Assert.Equal(2, home.Metrics.LockedUsers);
        Assert.Equal(2, home.Metrics.DisabledUsers);
        Assert.Equal(2, home.Metrics.PendingDeletionUsers);
    }

    [Fact]
    public void SharedRule_InMemory_MatchesExpectedStates()
    {
        foreach (var (id, expected, user) in Cases)
        {
            Assert.True(expected == UserAccountStateRules.StateOf(user, Now), $"{id}: expected {expected}");
            foreach (var state in UserAccountStateRules.States)
            {
                Assert.True(
                    (state == expected) == UserAccountStateRules.Is(user, state, Now),
                    $"{id}: Is({state})");
            }
        }
    }

    [Fact]
    public void SharedRule_TranslatesForPostgreSql_WithNowAsAParameter()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=prism_query_translation;Username=prism;Password=not-used")
            .Options;
        using var db = new ApplicationDbContext(options);

        foreach (var state in UserAccountStateRules.States)
        {
            var sql = db.Users.Where(UserAccountStateRules.For(state, Now)).ToQueryString();
            // The header lists parameter values; the statement itself must not inline now.
            var statement = sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
            Assert.Contains("AspNetUsers", statement);
            Assert.DoesNotContain("2026-07-13", statement);
        }
    }

    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);
        db.Users.AddRange(Cases.Select(c => c.User));
        await db.SaveChangesAsync();
        return db;
    }

    private static ApplicationUser User(
        string id,
        bool mustChange = false,
        bool disabled = false,
        bool pendingDeletion = false,
        DateTimeOffset? lockoutEnd = null) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        FullName = id,
        Rank = "Test",
        MustChangePassword = mustChange,
        IsDisabled = disabled,
        PendingDeletion = pendingDeletion,
        LockoutEnd = lockoutEnd,
        CreatedUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}

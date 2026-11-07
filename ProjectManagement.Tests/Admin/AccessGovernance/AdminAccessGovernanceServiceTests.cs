using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.AccessGovernance;

namespace ProjectManagement.Tests.Admin.AccessGovernance;

public sealed class AdminAccessGovernanceServiceTests
{
    [Fact]
    public async Task Snapshot_IdentifiesCriticalRoleAndAccountConditions()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        var adminRole = Role("role-admin", RoleNames.Admin);
        var hodRole = Role("role-hod", RoleNames.HoD);
        db.Roles.AddRange(adminRole, hodRole);
        db.Users.AddRange(
            User("admin", "Administrator", disabled: false),
            User("disabled-hod", "Disabled HoD", disabled: true),
            User("roleless", "Roleless User", disabled: false));
        db.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = "admin", RoleId = adminRole.Id },
            new IdentityUserRole<string> { UserId = "disabled-hod", RoleId = hodRole.Id });
        await db.SaveChangesAsync();

        var service = new AdminAccessGovernanceService(
            db,
            new AdminRoleDescriptorCatalog(),
            new AdminCapabilityCatalog(),
            new FixedAdminTime());

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(1, snapshot.AdministratorCount);
        Assert.Equal(1, snapshot.UsersWithoutRoles);
        Assert.Equal(2, snapshot.PrivilegedUserCount);
        Assert.Equal(1, snapshot.RestrictedPrivilegedUsers);
        Assert.True(snapshot.SingleHolderCriticalRoles >= 1);
        Assert.Contains(snapshot.Findings, finding => finding.Code == "single-active-admin");
        Assert.Contains(snapshot.Findings, finding => finding.Code == "roleless-users");
        Assert.Contains(snapshot.Findings, finding => finding.Code == "privileged-disabled" && finding.UserId == "disabled-hod");
        Assert.Equal(3, snapshot.Users.Count);
    }

    // Who has which role tells the truth (docs/ux/admin-review-fixes.md, PR 8).
    [Fact]
    public async Task Snapshot_CountsEachFindingOnce_AndLeavesNormalStatesOut()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        var adminRole = Role("role-admin", RoleNames.Admin);
        var hodRole = Role("role-hod", RoleNames.HoD);
        var comdtRole = Role("role-comdt", RoleNames.Comdt);
        db.Roles.AddRange(adminRole, hodRole, comdtRole);
        db.Users.AddRange(
            User("admin-hod", "Admin and HoD", disabled: false, lastLogin: true),
            User("admin-two", "Second Admin", disabled: false, lastLogin: true),
            User("comdt", "Commandant", disabled: false, lastLogin: true),
            User("roleless-a", "Roleless A", disabled: false),
            User("roleless-b", "Roleless B", disabled: false));
        db.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = "admin-hod", RoleId = adminRole.Id },
            new IdentityUserRole<string> { UserId = "admin-hod", RoleId = hodRole.Id },
            new IdentityUserRole<string> { UserId = "admin-two", RoleId = adminRole.Id },
            new IdentityUserRole<string> { UserId = "comdt", RoleId = comdtRole.Id });
        await db.SaveChangesAsync();

        var snapshot = await new AdminAccessGovernanceService(
            db,
            new AdminRoleDescriptorCatalog(),
            new AdminCapabilityCatalog(),
            new FixedAdminTime()).GetSnapshotAsync();

        // The two role-less accounts are one line, not one line plus one per account.
        var finding = Assert.Single(snapshot.Findings);
        Assert.Equal("roleless-users", finding.Code);
        Assert.Equal(snapshot.Findings.Count, snapshot.AccessIssueCount);

        // An Administrator who is also HoD, and a single Commandant, are normal.
        Assert.Empty(snapshot.Users.Single(user => user.Id == "admin-hod").Findings);
        Assert.Empty(snapshot.Users.Single(user => user.Id == "comdt").Findings);

        // The account itself still carries its own note for the export.
        Assert.Contains(snapshot.Users.Single(user => user.Id == "roleless-a").Findings, item => item.Code == "user-no-role");
    }

    [Fact]
    public async Task Snapshot_IncludesPolicyRolesThatAreNotProvisionedInIdentity()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        db.Roles.Add(Role("role-admin", RoleNames.Admin));
        db.Users.Add(User("admin", "Administrator", disabled: false));
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "admin", RoleId = "role-admin" });
        await db.SaveChangesAsync();

        var snapshot = await new AdminAccessGovernanceService(
            db,
            new AdminRoleDescriptorCatalog(),
            new AdminCapabilityCatalog(),
            new FixedAdminTime()).GetSnapshotAsync();

        var mainOfficeRole = Assert.Single(snapshot.Roles.Where(role =>
            string.Equals(role.Name, RoleNames.MainOfficeClerk, StringComparison.OrdinalIgnoreCase)));
        Assert.False(mainOfficeRole.IsConfigured);
        Assert.Contains(mainOfficeRole.Capabilities, capability =>
            capability.Policy == ProjectManagement.Configuration.Policies.Calendar.ManageCelebrations);
    }

    private sealed class FixedAdminTime : IAdminTimeService
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 14, 0, 0, 0, TimeSpan.Zero);
        public DateOnly TodayIst => new(2026, 7, 14);
        public DateTimeOffset StartOfIstDayUtc(DateOnly date) => throw new NotSupportedException();
        public DateTimeOffset EndExclusiveOfIstDayUtc(DateOnly date) => throw new NotSupportedException();
        public DateTime ToIst(DateTime utc) => utc;
        public DateTimeOffset ToIst(DateTimeOffset utc) => utc;
        public string FormatIst(DateTime? utc, string fallback = "—") => utc?.ToString("O") ?? fallback;
        public string FormatIst(DateTimeOffset? utc, string fallback = "—") => utc?.ToString("O") ?? fallback;
    }

    private static IdentityRole Role(string id, string name) => new(name)
    {
        Id = id,
        NormalizedName = name.ToUpperInvariant()
    };

    private static ApplicationUser User(string id, string fullName, bool disabled, bool lastLogin = false) => new()
    {
        LastLoginUtc = lastLogin ? new DateTime(2026, 7, 13, 8, 0, 0, DateTimeKind.Utc) : null,
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        FullName = fullName,
        Rank = "Test",
        IsDisabled = disabled,
        MustChangePassword = false,
        CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}

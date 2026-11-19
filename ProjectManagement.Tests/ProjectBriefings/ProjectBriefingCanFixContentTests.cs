using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// "Fix in project" is offered only where the person may change the project's content (cold review,
// 19 Nov 2026). The Studio's flag uses the projects' own rule, ProjectAccessGuard, from plain values.
public sealed class ProjectBriefingCanFixContentTests
{
    private const string Lead = "po-lead";
    private const string OtherOfficer = "po-other";

    [Theory]
    [InlineData(OtherOfficer, false, false, true, false)] // another project's officer
    [InlineData(Lead, false, false, true, true)]          // the project's own officer
    [InlineData("hod-1", false, true, false, true)]       // any HoD
    [InlineData("admin-1", true, false, false, true)]     // Admin
    [InlineData("comdt-1", false, false, false, false)]   // a Comdt (no Admin or HoD role) changes no project content
    [InlineData("", false, true, false, false)]           // no user
    public void TheRuleFromPlainValues(string userId, bool isAdmin, bool isHod, bool isOfficer, bool expected)
    {
        Assert.Equal(expected, ProjectAccessGuard.CanManageProjectContent(Lead, userId, isAdmin, isHod, isOfficer));
    }

    // The role lookup the flag reads gives every role a person has, from the Identity tables.
    [Fact]
    public async Task TheRoleLookup_ReturnsThePersonsRoles_AndStillAnswersIsCommand()
    {
        await using var db = new ProjectManagement.Data.ApplicationDbContext(
            new DbContextOptionsBuilder<ProjectManagement.Data.ApplicationDbContext>()
                .UseInMemoryDatabase($"briefing-roles-{Guid.NewGuid()}").Options);
        db.Roles.AddRange(
            new Microsoft.AspNetCore.Identity.IdentityRole { Id = "r-hod", Name = RoleNames.HoD },
            new Microsoft.AspNetCore.Identity.IdentityRole { Id = "r-po", Name = RoleNames.ProjectOfficer });
        db.UserRoles.AddRange(
            new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = "hod-1", RoleId = "r-hod" },
            new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = Lead, RoleId = "r-po" });
        await db.SaveChangesAsync();
        var roles = new ProjectManagement.Services.ProjectBriefings.ProjectBriefingDeckRoleResolver(db);

        Assert.Equal(new[] { RoleNames.HoD }, (await roles.GetRolesAsync("hod-1", CancellationToken.None)).ToArray());
        Assert.Equal(new[] { RoleNames.ProjectOfficer }, (await roles.GetRolesAsync(Lead, CancellationToken.None)).ToArray());
        Assert.Empty(await roles.GetRolesAsync("nobody", CancellationToken.None));
        Assert.True(await roles.IsCommandAsync("hod-1", CancellationToken.None));
        Assert.False(await roles.IsCommandAsync(Lead, CancellationToken.None));
    }
}

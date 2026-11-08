using System.Linq;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.ItProjects;
using Xunit;

namespace ProjectManagement.Tests.ItProjects;

public class ItProjectAccessTests
{
    [Theory]
    [InlineData(RoleNames.Admin, true, false)]
    [InlineData(RoleNames.HoD, true, true)]
    [InlineData(RoleNames.Ito, true, true)]
    [InlineData(RoleNames.Mco, false, true)]
    [InlineData(RoleNames.Comdt, false, true)]
    [InlineData(RoleNames.ProjectOfficer, false, false)]
    [InlineData(RoleNames.ItCellClerk, false, false)]
    public void Roles_MapToViewManageAndRemark(string role, bool canManage, bool canRemark)
    {
        var user = ItProjectTestDb.User("u", role);

        Assert.True(ItProjectAccess.CanView(user));
        Assert.Equal(canManage, ItProjectAccess.CanManage(user));
        Assert.Equal(canRemark, ItProjectAccess.CanRemark(user));
    }

    [Fact]
    public void RemarkRole_IsTheSeniorOneTheUserHolds()
    {
        Assert.Equal(RoleNames.HoD, ItProjectAccess.RemarkRoleOf(ItProjectTestDb.User("u", RoleNames.Ito, RoleNames.HoD)));
        Assert.Equal(RoleNames.Comdt, ItProjectAccess.RemarkRoleOf(ItProjectTestDb.User("u", RoleNames.Mco, RoleNames.Comdt)));
        Assert.Null(ItProjectAccess.RemarkRoleOf(ItProjectTestDb.User("u", RoleNames.Admin)));
    }

    [Fact]
    public void DeletedProject_IsNotViewable()
    {
        var project = new ItProject { IsDeleted = true };
        Assert.False(ItProjectAccess.CanView(ItProjectTestDb.User("u", RoleNames.Admin), project));
        Assert.False(ItProjectAccess.CanManage(ItProjectTestDb.User("u", RoleNames.Admin), project));
    }

    [Fact]
    public void RoleAccessCatalogue_ListsItProjectsForTheRightRoles()
    {
        var catalog = new AdminRoleAccessCatalog();
        var all = new[] { RoleNames.Admin, RoleNames.HoD, RoleNames.Ito, RoleNames.Mco, RoleNames.Comdt, RoleNames.ProjectOfficer };

        var ito = catalog.ForRoles(new[] { RoleNames.Ito }, all).Select(item => item.Key).ToList();
        Assert.Contains("it-projects-view", ito);
        Assert.Contains("it-projects-manage", ito);
        Assert.Contains("it-projects-remark", ito);

        var admin = catalog.ForRoles(new[] { RoleNames.Admin }, all).Select(item => item.Key).ToList();
        Assert.Contains("it-projects-manage", admin);
        Assert.DoesNotContain("it-projects-remark", admin);

        var po = catalog.ForRoles(new[] { RoleNames.ProjectOfficer }, all).Select(item => item.Key).ToList();
        Assert.Contains("it-projects-view", po);
        Assert.DoesNotContain("it-projects-manage", po);

        var group = catalog.BuildForRoles(all).Single(g => g.Key == "it-projects");
        Assert.Equal("IT projects", group.Title);
        Assert.Equal(3, group.Items.Count);
    }
}

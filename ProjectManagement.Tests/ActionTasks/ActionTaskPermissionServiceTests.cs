using ProjectManagement.Configuration;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskPermissionServiceTests
{
    [Fact]
    public void RoleVisibilityAndWritePermissions_AreEnforced()
    {
        // SECTION: Arrange
        var service = new ActionTaskPermissionService();

        // SECTION: Act
        var comdtCanViewAll = service.CanViewAll(RoleNames.Comdt);
        var hodCanViewAll = service.CanViewAll(RoleNames.HoD);
        var taCannotViewAll = service.CanViewAll(RoleNames.Ta);

        var ownerCanWrite = service.CanAddTaskUpdate(RoleNames.Ta, "owner", "owner");
        var nonOwnerCannotWrite = service.CanAddTaskUpdate(RoleNames.Ta, "other", "owner");

        // SECTION: Assert
        Assert.True(comdtCanViewAll);
        Assert.True(hodCanViewAll);
        Assert.False(taCannotViewAll);
        Assert.True(ownerCanWrite);
        Assert.False(nonOwnerCannotWrite);
    }
}

using System;
using System.Linq;
using System.Reflection;
using ProjectManagement.Services.ProjectOfficeReports.Training;

namespace ProjectManagement.Tests.ProjectOfficeReports.Training;

public sealed class TrainingNotificationServiceTests
{
    [Fact]
    public void ApproverRoles_align_with_expected_policy_roles()
    {
        var field = typeof(TrainingNotificationService).GetField(
            "ApproverRoles",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(field);

        var roles = Assert.IsType<string[]>(field!.GetValue(null));
        var orderedRoles = roles.OrderBy(role => role, StringComparer.OrdinalIgnoreCase).ToArray();
        var expected = new[] { "Admin", "HoD", "ProjectOffice" };
        var orderedExpected = expected.OrderBy(role => role, StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.Equal(orderedExpected, orderedRoles);
    }
}

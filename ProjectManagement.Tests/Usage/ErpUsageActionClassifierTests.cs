using ProjectManagement.Services.Usage;

namespace ProjectManagement.Tests.Usage;

public sealed class ErpUsageActionClassifierTests
{
    [Theory]
    [InlineData("LoginSuccess")]
    [InlineData("UserActivityBucketRecorded")]
    [InlineData("PasswordReset")]
    public void TelemetryAndAuthenticationActions_AreIgnored(string action)
    {
        Assert.Equal(ErpUsageActionKind.Ignored, ErpUsageActionClassifier.Classify(action));
    }

    [Theory]
    [InlineData("Projects.MetaChangedDirect")]
    [InlineData("DocumentUploaded")]
    [InlineData("CalendarEventCreated")]
    [InlineData("ApprovalSubmitted")]
    public void WorkflowActions_AreOperational(string action)
    {
        Assert.Equal(ErpUsageActionKind.Operational, ErpUsageActionClassifier.Classify(action));
    }

    [Theory]
    [InlineData("AdminUserUpdated")]
    [InlineData("HolidayObservanceChanged")]
    [InlineData("RoleCreated")]
    public void ConfigurationActions_AreAdministrative(string action)
    {
        Assert.Equal(ErpUsageActionKind.Administrative, ErpUsageActionClassifier.Classify(action));
    }
}

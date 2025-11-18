using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcProjectBucketHelperTests
{
    // SECTION: Classification helpers
    [Fact]
    public void Classify_WithInstalledProject_ReturnsInstalledBucket()
    {
        var project = new FfcProject { Quantity = 3, IsInstalled = true, IsDelivered = true };

        var (bucket, quantity) = FfcProjectBucketHelper.Classify(project);

        Assert.Equal(FfcDeliveryBucket.Installed, bucket);
        Assert.Equal(3, quantity);
    }

    [Fact]
    public void Classify_WithDeliveredOnlyProject_ReturnsDeliveredBucket()
    {
        var project = new FfcProject { Quantity = 2, IsDelivered = true };

        var (bucket, quantity) = FfcProjectBucketHelper.Classify(project);

        Assert.Equal(FfcDeliveryBucket.DeliveredNotInstalled, bucket);
        Assert.Equal(2, quantity);
    }

    [Fact]
    public void Classify_WithZeroQuantity_FallsBackToOne()
    {
        var project = new FfcProject { Quantity = 0 };

        var (bucket, quantity) = FfcProjectBucketHelper.Classify(project);

        Assert.Equal(FfcDeliveryBucket.Planned, bucket);
        Assert.Equal(1, quantity);
    }

    // SECTION: Summary helpers
    [Fact]
    public void Summarize_WithMixedProjects_ReturnsQuantities()
    {
        var projects = new[]
        {
            new FfcProject { Quantity = 2, IsDelivered = true, IsInstalled = true },
            new FfcProject { Quantity = 5, IsDelivered = true },
            new FfcProject { Quantity = 4 }
        };

        var summary = FfcProjectBucketHelper.Summarize(projects);

        Assert.Equal(2, summary.Installed);
        Assert.Equal(5, summary.DeliveredNotInstalled);
        Assert.Equal(4, summary.Planned);
        Assert.Equal(11, summary.Total);
    }
}

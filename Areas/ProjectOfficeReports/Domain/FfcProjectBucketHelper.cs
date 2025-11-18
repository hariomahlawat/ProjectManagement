using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

// SECTION: Delivery bucket contract
public enum FfcDeliveryBucket
{
    Planned,
    DeliveredNotInstalled,
    Installed
}

// SECTION: Quantity summary projection
public sealed record FfcProjectQuantitySummary(int Installed, int DeliveredNotInstalled, int Planned)
{
    public int Total => Installed + DeliveredNotInstalled + Planned;
}

// SECTION: Helper entry points
public static class FfcProjectBucketHelper
{
    // SECTION: Object classification overloads
    public static (FfcDeliveryBucket Bucket, int Quantity) Classify(FfcProject project)
    {
        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        return Classify(project.IsInstalled, project.IsDelivered, project.Quantity);
    }

    public static (FfcDeliveryBucket Bucket, int Quantity) Classify(bool isInstalled, bool isDelivered, int quantity)
    {
        var safeQuantity = quantity <= 0 ? 1 : quantity;

        if (isInstalled)
        {
            return (FfcDeliveryBucket.Installed, safeQuantity);
        }

        if (isDelivered)
        {
            return (FfcDeliveryBucket.DeliveredNotInstalled, safeQuantity);
        }

        return (FfcDeliveryBucket.Planned, safeQuantity);
    }

    // SECTION: Aggregation helpers
    public static FfcProjectQuantitySummary Summarize(IEnumerable<FfcProject> projects)
    {
        if (projects is null)
        {
            return new FfcProjectQuantitySummary(0, 0, 0);
        }

        var totals = projects
            .Select(Classify)
            .GroupBy(tuple => tuple.Bucket)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));

        totals.TryGetValue(FfcDeliveryBucket.Installed, out var installed);
        totals.TryGetValue(FfcDeliveryBucket.DeliveredNotInstalled, out var delivered);
        totals.TryGetValue(FfcDeliveryBucket.Planned, out var planned);

        return new FfcProjectQuantitySummary(installed, delivered, planned);
    }

    // SECTION: Label helper
    public static string GetBucketLabel(FfcDeliveryBucket bucket) => bucket switch
    {
        FfcDeliveryBucket.Installed => "Installed",
        FfcDeliveryBucket.DeliveredNotInstalled => "Delivered (not installed)",
        _ => "Planned"
    };
}

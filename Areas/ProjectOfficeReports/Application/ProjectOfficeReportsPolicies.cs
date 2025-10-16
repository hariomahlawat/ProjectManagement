using System;
using Microsoft.AspNetCore.Authorization;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public static class ProjectOfficeReportsPolicies
{
    public const string ManageVisits = "ProjectOfficeReports.ManageVisits";
    public const string ManageSocialMediaEvents = "ProjectOfficeReports.ManageSocialMediaEvents";
    public const string ViewTotTracker = "ProjectOfficeReports.ViewTotTracker";
    public const string ManageTotTracker = "ProjectOfficeReports.ManageTotTracker";
    public const string ApproveTotTracker = "ProjectOfficeReports.ApproveTotTracker";

    public static AuthorizationPolicyBuilder RequireProjectOfficeManager(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole("Admin", "HoD", "ProjectOffice", "Project Office");
    }

    public static AuthorizationPolicyBuilder RequireTotTrackerViewer(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole("Admin", "HoD", "ProjectOffice", "Project Office", "Project Officer");
    }

    public static AuthorizationPolicyBuilder RequireTotTrackerSubmitter(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole("Admin", "HoD", "ProjectOffice", "Project Office", "Project Officer");
    }

    public static AuthorizationPolicyBuilder RequireTotTrackerApprover(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole("Admin", "HoD");
    }
}

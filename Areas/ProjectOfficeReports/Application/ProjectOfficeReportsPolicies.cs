using System;
using Microsoft.AspNetCore.Authorization;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public static class ProjectOfficeReportsPolicies
{
    private static readonly string[] ProjectOfficeRoles =
    {
        "Admin",
        "HoD",
        "ProjectOffice",
        "Project Office"
    };

    private static readonly string[] TrainingTrackerViewerRoles =
    {
        "Admin",
        "HoD",
        "ProjectOffice",
        "Project Office",
        "Project Officer",
        "Comdt",
        "MCO",
        "TA",
        "Main Office"
    };

    private static readonly string[] TrainingTrackerManagerRoles =
    {
        "Admin",
        "HoD",
        "ProjectOffice",
        "Project Office"
    };

    private static readonly string[] TotTrackerSubmitterRoles =
    {
        "Admin",
        "HoD",
        "ProjectOffice",
        "Project Office",
        "Project Officer"
    };

    private static readonly string[] TotTrackerApproverRoles = { "Admin", "HoD" };

    public const string ViewVisits = "ProjectOfficeReports.ViewVisits";
    public const string ManageVisits = "ProjectOfficeReports.ManageVisits";
    public const string ManageSocialMediaEvents = "ProjectOfficeReports.ManageSocialMediaEvents";
    public const string ViewTotTracker = "ProjectOfficeReports.ViewTotTracker";
    public const string ManageTotTracker = "ProjectOfficeReports.ManageTotTracker";
    public const string ApproveTotTracker = "ProjectOfficeReports.ApproveTotTracker";
    public const string ViewProliferationTracker = "ProjectOfficeReports.ViewProliferationTracker";
    public const string SubmitProliferationTracker = "ProjectOfficeReports.SubmitProliferationTracker";
    public const string ApproveProliferationTracker = "ProjectOfficeReports.ApproveProliferationTracker";
    public const string ManageProliferationPreferences = "ProjectOfficeReports.ManageProliferationPreferences";
    public const string ViewTrainingTracker = "ProjectOfficeReports.ViewTrainingTracker";
    public const string ManageTrainingTracker = "ProjectOfficeReports.ManageTrainingTracker";
    public const string ApproveTrainingTracker = "ProjectOfficeReports.ApproveTrainingTracker";

    public static AuthorizationPolicyBuilder RequireProjectOfficeManager(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(ProjectOfficeRoles);
    }

    public static AuthorizationPolicyBuilder RequireTotTrackerViewer(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(TotTrackerSubmitterRoles);
    }

    public static AuthorizationPolicyBuilder RequireTotTrackerSubmitter(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(TotTrackerSubmitterRoles);
    }

    public static AuthorizationPolicyBuilder RequireTotTrackerApprover(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(TotTrackerApproverRoles);
    }

    public static AuthorizationPolicyBuilder RequireProliferationViewer(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireAuthenticatedUser();
    }

    public static AuthorizationPolicyBuilder RequireProliferationSubmitter(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(ProjectOfficeRoles);
    }

    public static AuthorizationPolicyBuilder RequireProliferationApprover(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(TotTrackerApproverRoles);
    }

    public static AuthorizationPolicyBuilder RequireProliferationPreferenceManager(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(ProjectOfficeRoles);
    }

    public static AuthorizationPolicyBuilder RequireTrainingTrackerViewer(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(TrainingTrackerViewerRoles);
    }

    public static AuthorizationPolicyBuilder RequireTrainingTrackerManager(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(TrainingTrackerManagerRoles);
    }

    public static AuthorizationPolicyBuilder RequireTrainingTrackerApprover(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(TotTrackerApproverRoles);
    }
}

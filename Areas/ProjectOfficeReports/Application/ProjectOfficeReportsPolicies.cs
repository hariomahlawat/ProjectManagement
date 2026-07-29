using System;
using Microsoft.AspNetCore.Authorization;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public static class ProjectOfficeReportsPolicies
{
    public static readonly string[] ProjectOfficeManagerRoles =
    {
        "Admin",
        "HoD",
        "ProjectOffice",
        "Project Office"
    };


    public static readonly string[] ArppViewerRoles =
    {
        "Admin",
        "HoD",
        "Comdt",
        "ProjectOffice",
        "Project Office",
        "MCO",
        "Project Officer"
    };


    public static readonly string[] ArppVerifierRoles =
    {
        "Admin",
        "HoD",
        "Comdt"
    };

    public static readonly string[] ArppUnlockRoles =
    {
        "Admin",
        "HoD"
    };
    public static readonly string[] TrainingTrackerViewerRoles =
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

    public static readonly string[] TrainingTrackerManagerRoles =
    {
        "Admin",
        "HoD",
        "ProjectOffice",
        "Project Office"
    };

    public static readonly string[] ProgressReviewViewerRoles =
    {
        "Admin",
        "HoD",
        "ProjectOffice",
        "Project Office",
        "Comdt"
    };

    public static readonly string[] TotTrackerSubmitterRoles =
    {
        "Admin",
        "HoD",
        "ProjectOffice",
        "Project Office",
        "Project Officer"
    };

    public static readonly string[] TotTrackerApproverRoles = { "Admin", "HoD" };

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
    public const string ViewProgressReview = "ProjectOfficeReports.ViewProgressReview";
    public const string ViewArpp = "ProjectOfficeReports.ViewArpp";
    public const string ManageArpp = "ProjectOfficeReports.ManageArpp";
    public const string VerifyArpp = "ProjectOfficeReports.VerifyArpp";
    public const string UnlockArpp = "ProjectOfficeReports.UnlockArpp";

    public static AuthorizationPolicyBuilder RequireProjectOfficeManager(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(ProjectOfficeManagerRoles);
    }

    public static AuthorizationPolicyBuilder RequireTotTrackerViewer(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireAuthenticatedUser();
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

        return builder.RequireRole(ProjectOfficeManagerRoles);
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

        return builder.RequireRole(ProjectOfficeManagerRoles);
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

    public static AuthorizationPolicyBuilder RequireProgressReviewViewer(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(ProgressReviewViewerRoles);
    }

    public static AuthorizationPolicyBuilder RequireArppViewer(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(ArppViewerRoles);
    }

    public static AuthorizationPolicyBuilder RequireArppManager(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(ProjectOfficeManagerRoles);
    }

    public static AuthorizationPolicyBuilder RequireArppVerifier(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(ArppVerifierRoles);
    }

    public static AuthorizationPolicyBuilder RequireArppUnlocker(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(ArppUnlockRoles);
    }
}

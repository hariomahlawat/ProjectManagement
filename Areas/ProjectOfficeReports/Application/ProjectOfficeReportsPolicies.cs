using System;
using System.Collections.Generic;
using System.Security.Claims;
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

    private static readonly string[] MiscActivityViewerRoles =
    {
        "Admin",
        "HoD",
        "ProjectOffice",
        "Project Office",
        "Project Officer",
        "TA",
        "Main Office",
        "Comdt",
        "MCO"
    };

    private static readonly string[] ActivityTypeViewerRoles =
    {
        "Admin",
        "HoD",
        "ProjectOffice",
        "Project Office",
        "Project Officer",
        "TA",
        "Main Office",
        "Comdt",
        "MCO"
    };

    private static readonly string[] ActivityTypeManagerRoles =
    {
        "Admin",
        "HoD"
    };

    private static readonly string[] MiscActivityManagerRoles =
    {
        "Admin",
        "HoD",
        "ProjectOffice",
        "Project Office",
        "Project Officer",
        "TA"
    };

    private static readonly string[] MiscActivityApproverRoles =
    {
        "Admin",
        "HoD"
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
    public const string ViewMiscActivities = "ProjectOfficeReports.ViewMiscActivities";
    public const string ManageMiscActivities = "ProjectOfficeReports.ManageMiscActivities";
    public const string DeleteMiscActivities = "ProjectOfficeReports.DeleteMiscActivities";
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
    public const string ViewActivityTypes = "ProjectOfficeReports.ViewActivityTypes";
    public const string ManageActivityTypes = "ProjectOfficeReports.ManageActivityTypes";

    public static AuthorizationPolicyBuilder RequireProjectOfficeManager(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(ProjectOfficeRoles);
    }

    public static AuthorizationPolicyBuilder RequireMiscActivityViewer(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(MiscActivityViewerRoles);
    }

    public static AuthorizationPolicyBuilder RequireActivityTypeViewer(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(ActivityTypeViewerRoles);
    }

    public static AuthorizationPolicyBuilder RequireMiscActivityManager(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(MiscActivityManagerRoles);
    }

    public static AuthorizationPolicyBuilder RequireActivityTypeManager(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(ActivityTypeManagerRoles);
    }

    public static AuthorizationPolicyBuilder RequireMiscActivityApprover(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(MiscActivityApproverRoles);
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

    internal static bool HasActivityTypeViewerRole(ISet<string> roles)
    {
        if (roles is null)
        {
            throw new ArgumentNullException(nameof(roles));
        }

        foreach (var role in ActivityTypeViewerRoles)
        {
            if (roles.Contains(role))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasActivityTypeManagerRole(ISet<string> roles)
    {
        if (roles is null)
        {
            throw new ArgumentNullException(nameof(roles));
        }

        foreach (var role in ActivityTypeManagerRoles)
        {
            if (roles.Contains(role))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsActivityTypeManager(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return false;
        }

        foreach (var role in ActivityTypeManagerRoles)
        {
            if (principal.IsInRole(role))
            {
                return true;
            }
        }

        return false;
    }
}

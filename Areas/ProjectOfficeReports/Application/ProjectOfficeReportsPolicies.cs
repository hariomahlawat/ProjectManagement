using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Configuration;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public static class ProjectOfficeReportsPolicies
{
    public static readonly string[] ProjectOfficeManagerRoles =
    {
        RoleNames.Admin,
        RoleNames.HoD,
        RoleNames.ProjectOfficeAlternate,
        RoleNames.ProjectOffice
    };


    public static readonly string[] ArppViewerRoles =
    {
        RoleNames.Admin,
        RoleNames.HoD,
        RoleNames.Comdt,
        RoleNames.ProjectOfficeAlternate,
        RoleNames.ProjectOffice,
        RoleNames.Mco,
        RoleNames.ProjectOfficer
    };


    public static readonly string[] ArppVerifierRoles =
    {
        RoleNames.Admin,
        RoleNames.HoD,
        RoleNames.Comdt
    };

    public static readonly string[] ArppUnlockRoles =
    {
        RoleNames.Admin,
        RoleNames.HoD
    };
    public static readonly string[] TrainingTrackerViewerRoles =
    {
        RoleNames.Admin,
        RoleNames.HoD,
        RoleNames.ProjectOfficeAlternate,
        RoleNames.ProjectOffice,
        RoleNames.ProjectOfficer,
        RoleNames.Comdt,
        RoleNames.Mco,
        RoleNames.Ta,
        RoleNames.MainOfficeClerk,
        RoleNames.MainOfficeAlternate
    };

    public static readonly string[] TrainingTrackerManagerRoles =
    {
        RoleNames.Admin,
        RoleNames.HoD,
        RoleNames.ProjectOfficeAlternate,
        RoleNames.ProjectOffice
    };

    public static readonly string[] ProgressReviewViewerRoles =
    {
        RoleNames.Admin,
        RoleNames.HoD,
        RoleNames.ProjectOfficeAlternate,
        RoleNames.ProjectOffice,
        RoleNames.Comdt
    };

    public static readonly string[] TotTrackerSubmitterRoles =
    {
        RoleNames.Admin,
        RoleNames.HoD,
        RoleNames.ProjectOfficeAlternate,
        RoleNames.ProjectOffice,
        RoleNames.ProjectOfficer
    };

    public static readonly string[] TotTrackerApproverRoles = { RoleNames.Admin, RoleNames.HoD };

    // SECTION: FFC portfolio governance
    // Full FFC management is intentionally granted to Admin, HoD, Comdt and ITO.
    // Detailed Table inline editing is deliberately narrower: Admin, HoD and
    // Comdt retain it, while ITO is explicitly excluded by business rule.
    public static readonly string[] FfcManagerRoles =
    {
        RoleNames.Admin,
        RoleNames.HoD,
        RoleNames.Comdt,
        RoleNames.Ito
    };

    public static readonly string[] FfcInlineEditorRoles =
    {
        RoleNames.Admin,
        RoleNames.HoD,
        RoleNames.Comdt
    };

    public static readonly string[] TrainingTrackerApproverRoles =
    {
        RoleNames.Admin,
        RoleNames.HoD
    };

    public const string ManageFfc = "ProjectOfficeReports.ManageFfc";
    public const string InlineEditFfc = "ProjectOfficeReports.InlineEditFfc";
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


    public static AuthorizationPolicyBuilder RequireFfcManager(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(FfcManagerRoles);
    }

    public static AuthorizationPolicyBuilder RequireFfcInlineEditor(this AuthorizationPolicyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RequireRole(FfcInlineEditorRoles);
    }

    public static bool CanManageFfc(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true
        && FfcManagerRoles.Any(principal.IsInRole);

    public static bool CanInlineEditFfc(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true
        && FfcInlineEditorRoles.Any(principal.IsInRole);

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

        return builder.RequireRole(TrainingTrackerApproverRoles);
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

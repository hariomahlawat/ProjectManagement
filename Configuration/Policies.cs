using ProjectManagement.Models;

namespace ProjectManagement.Configuration;

public static class Policies
{
    // SECTION: Calendar policies
    public static class Calendar
    {
        public const string ManageEvents = "Calendar.ManageEvents";
        public const string ManageCelebrations = "Calendar.ManageCelebrations";
        public const string ManageBirthdays = "Calendar.ManageBirthdays";
        public const string ManageAnniversaries = "Calendar.ManageAnniversaries";

        public static readonly string[] EventManagerRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD,
            RoleNames.Ta,
            RoleNames.Comdt,
            RoleNames.Mco,
            RoleNames.ProjectOfficer,
            RoleNames.ProjectOffice,
            RoleNames.ProjectOfficeAlternate
        };

        // Birthdays and anniversaries are maintained only by Admin, TA and Main Office.
        public static readonly string[] BirthdayManagerRoles =
        {
            RoleNames.Admin,
            RoleNames.Ta,
            RoleNames.MainOfficeClerk,
            RoleNames.MainOfficeAlternate
        };

        public static readonly string[] AnniversaryManagerRoles =
        {
            RoleNames.Admin,
            RoleNames.Ta,
            RoleNames.MainOfficeClerk,
            RoleNames.MainOfficeAlternate
        };

        public static readonly string[] CelebrationManagerRoles =
        {
            RoleNames.Admin,
            RoleNames.Ta,
            RoleNames.MainOfficeClerk,
            RoleNames.MainOfficeAlternate
        };

        public static string PolicyFor(CelebrationType eventType) => eventType switch
        {
            CelebrationType.Birthday => ManageBirthdays,
            CelebrationType.Anniversary => ManageAnniversaries,
            _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unsupported celebration type.")
        };
    }


    // SECTION: ERP usage intelligence
    public static class Usage
    {
        public const string View = "ERP.Usage.View";

        public static readonly string[] ViewerRoles =
        {
            RoleNames.Admin,
            RoleNames.Comdt,
            RoleNames.HoD
        };
    }

    // SECTION: Project authorisation policies
    public static class Projects
    {
        public const string Create = "Project.Create";

        public static readonly string[] CreatorRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD
        };
    }

    // SECTION: Checklist policies
    public static class Checklist
    {
        public const string View = "Checklist.View";
        public const string Edit = "Checklist.Edit";
        public const string EditPurpose = "Checklist.PurposeEdit";

        public static readonly string[] EditorRoles =
        {
            RoleNames.Mco,
            RoleNames.HoD
        };

        public static readonly string[] PurposeEditorRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD
        };
    }

    // SECTION: Document repository policies
    public static class Documents
    {
        public const string View = "DocRepo.View";
        public const string Upload = "DocRepo.Upload";
        public const string SoftDelete = "DocRepo.SoftDelete";
        public const string ManageCategories = "DocRepo.ManageCategories";
        public const string DeleteApprove = "DocRepo.DeleteApprove";
        public const string EditMetadata = "DocRepo.EditMetadata";
        public const string Purge = "DocRepo.Purge";

        public static readonly string[] UploadAndSoftDeleteRoles =
        {
            RoleNames.ProjectOffice,
            RoleNames.MainOfficeClerk,
            RoleNames.McCellClerk,
            RoleNames.ItCellClerk,
            RoleNames.Admin,
            RoleNames.HoD
        };

        public static readonly string[] MetadataEditorRoles =
        {
            RoleNames.Admin,
            RoleNames.Ta,
            RoleNames.Ito,
            RoleNames.Mco,
            RoleNames.HoD
        };

        public static readonly string[] DeleteApprovalRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD
        };

        public static readonly string[] CategoryManagerRoles =
        {
            RoleNames.Admin
        };

        public static readonly string[] PurgeRoles =
        {
            RoleNames.Admin
        };
    }

    // SECTION: Action tracker policies
    public static class ActionTracker
    {
        public const string Access = "ActionTracker.Access";

        public static readonly string[] AccessAllowedRoles =
        {
            RoleNames.Comdt,
            RoleNames.HoD,
            RoleNames.ProjectOfficer,
            RoleNames.Mco,
            RoleNames.Ta,
            RoleNames.Ito
        };
    }

    // SECTION: Industry partners policies
    public static class IndustryPartners
    {
        public const string View = "IndustryPartners.View";
        public const string Create = "IndustryPartners.Create";
        public const string EditAny = "IndustryPartners.EditAny";
        public const string Delete = "IndustryPartners.Delete";
        public const string AddContact = "IndustryPartners.Contact.Add";
        public const string ManageAnyContact = "IndustryPartners.Contact.ManageAny";

        public static readonly string[] ViewAllowedRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD,
            RoleNames.ProjectOffice,
            RoleNames.ProjectOfficeAlternate,
            RoleNames.Comdt,
            RoleNames.Mco
        };

        // Organisation creation is intentionally broad: the operational users who
        // discover a new industry contact should be able to add it without routing the
        // record through a central clerk. Editing remains owner-based with a limited
        // command override defined separately below.
        public static readonly string[] CreateAllowedRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD,
            RoleNames.Comdt,
            RoleNames.ProjectOfficer,
            RoleNames.ProjectOffice,
            RoleNames.ProjectOfficeAlternate,
            RoleNames.Mco,
            RoleNames.Ta,
            RoleNames.Ito
        };

        public static readonly string[] EditAnyAllowedRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD,
            RoleNames.Comdt
        };

        public static readonly string[] DeleteAllowedRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD
        };

        public static readonly string[] ContactOverrideRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD,
            RoleNames.Comdt
        };
    }

    public static class Ipr
    {
        public const string View = "Ipr.View";
        public const string Edit = "Ipr.Edit";

        public static readonly string[] ViewAllowedRoles =
        {
            "Admin",
            "HoD",
            "ProjectOffice",
            "Project Office",
            "Comdt",
            "MCO"
        };

        public static readonly string[] EditAllowedRoles =
        {
            "Admin",
            "HoD",
            "ProjectOffice",
            "Project Office"
        };
    }
    // SECTION: Project briefing decks
    public static class ProjectBriefingDecks
    {
        public const string Manage = "ProjectBriefingDecks.Manage";

        public static readonly string[] ManageAllowedRoles =
        {
            RoleNames.Comdt,
            RoleNames.HoD
        };
    }

    // SECTION: Conference remark policies
    public static class ConferenceRemarks
    {
        public const string Manage = "ConferenceRemarks.Manage";

        public static readonly string[] ManageAllowedRoles =
        {
            RoleNames.Comdt,
            RoleNames.HoD
        };
    }

}

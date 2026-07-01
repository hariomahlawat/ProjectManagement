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

    // SECTION: Industry partners policies
    public static class IndustryPartners
    {
        public const string View = "IndustryPartners.View";
        public const string Manage = "IndustryPartners.Manage";
        public const string Delete = "IndustryPartners.Delete";

        public static readonly string[] ViewAllowedRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD,
            RoleNames.ProjectOffice,
            RoleNames.ProjectOfficeAlternate,
            RoleNames.Comdt,
            RoleNames.Mco
        };

        public static readonly string[] ManageAllowedRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD,
            RoleNames.ProjectOffice,
            RoleNames.ProjectOfficeAlternate
        };

        public static readonly string[] DeleteAllowedRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD
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
}

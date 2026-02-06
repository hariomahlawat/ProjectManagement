namespace ProjectManagement.Configuration;

public static class Policies
{
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

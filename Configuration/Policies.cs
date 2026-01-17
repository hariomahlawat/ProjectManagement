namespace ProjectManagement.Configuration;

public static class Policies
{
    public static class Partners
    {
        public const string View = "Partners.View";
        public const string Manage = "Partners.Manage";
        public const string Delete = "Partners.Delete";
        public const string LinkToProject = "Partners.LinkToProject";

        public static readonly string[] ManageAllowedRoles =
        {
            RoleNames.ProjectOffice,
            RoleNames.ProjectOfficeAlternate,
            RoleNames.ProjectOfficer,
            RoleNames.Admin,
            RoleNames.HoD,
            RoleNames.Ta,
            RoleNames.Mco,
            RoleNames.Comdt
        };

        public static readonly string[] DeleteAllowedRoles =
        {
            RoleNames.Admin,
            RoleNames.HoD,
            RoleNames.Comdt
        };

        public static readonly string[] LinkAllowedRoles =
        {
            RoleNames.ProjectOfficer,
            RoleNames.HoD,
            RoleNames.Mco,
            RoleNames.Admin
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

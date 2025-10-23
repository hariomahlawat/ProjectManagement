namespace ProjectManagement.Configuration;

public static class Policies
{
    public static class Ipr
    {
        public const string View = "Ipr.View";
        public const string Edit = "Ipr.Edit";

        public static readonly string[] AllowedRoles =
        {
            "Admin",
            "HoD",
            "ProjectOffice",
            "Project Office"
        };
    }
}

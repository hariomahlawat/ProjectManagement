namespace ProjectManagement.Configuration;

/// <summary>
/// Authorisation policy names for administrative capabilities. Pages and
/// application services should use these names instead of repeating role lists.
/// </summary>
public static class AdminPolicies
{
    public const string Access = "Admin.Access";
    public const string UsersManage = "Admin.Users.Manage";
    public const string SecurityView = "Admin.Security.View";
    public const string LogsView = "Admin.Logs.View";
    public const string RecoveryManage = "Admin.Recovery.Manage";
    public const string MasterDataManage = "Admin.MasterData.Manage";
    public const string ActivityTypesManage = "Admin.ActivityTypes.Manage";
    public const string IngestionManage = "Admin.Ingestion.Manage";
    public const string MediaManage = "Admin.Media.Manage";
}

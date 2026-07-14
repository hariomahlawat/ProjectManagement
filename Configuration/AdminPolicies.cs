namespace ProjectManagement.Configuration;

/// <summary>
/// Authorisation policy names for administrative capabilities. Pages and
/// application services should use these names instead of repeating role lists.
/// </summary>
public static class AdminPolicies
{
    public const string Access = "Admin.Access";
    public const string UsersManage = "Admin.Users.Manage";
    public const string AccessGovernanceView = "Admin.AccessGovernance.View";
    public const string SecurityView = "Admin.Security.View";
    public const string LogsView = "Admin.Logs.View";
    public const string RecoveryManage = "Admin.Recovery.Manage";
    public const string MasterDataManage = "Admin.MasterData.Manage";
    public const string IntegrityManage = "Admin.MasterData.Integrity.Manage";
    public const string ActivityTypesManage = "Admin.ActivityTypes.Manage";
    public const string HolidaysManage = "Admin.Holidays.Manage";
    public const string IngestionManage = "Admin.Ingestion.Manage";
    // Media administration is split into explicit capabilities. MediaManage is retained
    // as a compatibility policy for existing navigation and older page attributes.
    public const string MediaManage = "Admin.Media.Manage";
    public const string MediaView = "Admin.Media.View";
    public const string MediaConfigure = "Admin.Media.Configure";
    public const string MediaOperateQueue = "Admin.Media.OperateQueue";
    public const string MediaRecover = "Admin.Media.Recover";
    public const string MediaClassificationManage = "Admin.Media.Classification.Manage";
}

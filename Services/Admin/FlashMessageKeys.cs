namespace ProjectManagement.Services.Admin;

/// <summary>
/// Namespaced TempData keys. Generic keys such as "ok" and "err" can leak across
/// redirects when the intended destination does not consume them.
/// </summary>
public static class FlashMessageKeys
{
    public const string AdminUsersSuccess = "Admin.Users.Success";
    public const string AdminUsersError = "Admin.Users.Error";

    public const string AdminMasterDataSuccess = "Admin.MasterData.Success";
    public const string AdminMasterDataError = "Admin.MasterData.Error";

    public const string CelebrationsSuccess = "Celebrations.Success";
    public const string CelebrationsError = "Celebrations.Error";

    public const string IdentitySuccess = "Identity.Success";
    public const string IdentityError = "Identity.Error";
}

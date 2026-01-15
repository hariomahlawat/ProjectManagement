using System;
using System.Security.Claims;

namespace ProjectManagement.Services.Authorization;

public static class ApprovalAuthorization
{
    // SECTION: Role names
    private const string AdminRole = "Admin";
    private const string HodRole = "HoD";

    // SECTION: Approval checks
    public static bool CanApproveProjectChanges(ClaimsPrincipal user)
    {
        if (user is null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        return user.IsInRole(AdminRole) || user.IsInRole(HodRole);
    }

    public static bool CanApproveProjectChanges(bool isAdmin, bool isHoD)
        => isAdmin || isHoD;
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Configuration;

namespace ProjectManagement.Services.Admin.MasterData.Integrity;

/// <summary>What a list page shows about its own display order.</summary>
public sealed record ListOrderNotice(string CheckKey, bool HasProblem, int ProblemCount, bool CanRenumber)
{
    public static ListOrderNotice None(string checkKey) => new(checkKey, false, 0, false);
}

/// <summary>
/// Lets a list page say "the order of this list has gaps or repeats" and renumber it there,
/// using the same audited repair and the same permission as Data checks.
/// </summary>
public interface IListOrderNoticeService
{
    Task<ListOrderNotice> GetAsync(ClaimsPrincipal user, string checkKey, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> RenumberAsync(ClaimsPrincipal user, string checkKey, CancellationToken cancellationToken = default);
}

public sealed class ListOrderNoticeService : IListOrderNoticeService
{
    private readonly IAdminMasterDataIntegrityService _integrity;
    private readonly IAuthorizationService _authorization;

    public ListOrderNoticeService(IAdminMasterDataIntegrityService integrity, IAuthorizationService authorization)
    {
        _integrity = integrity ?? throw new ArgumentNullException(nameof(integrity));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    public async Task<ListOrderNotice> GetAsync(ClaimsPrincipal user, string checkKey, CancellationToken cancellationToken = default)
    {
        var check = await _integrity.InspectOrderAsync(checkKey, cancellationToken);
        if (check is null || check.Findings.Count == 0)
        {
            return ListOrderNotice.None(checkKey);
        }

        return new ListOrderNotice(check.Key, true, check.Findings.Count, await CanRenumberAsync(user));
    }

    public async Task<AdminOperationResult> RenumberAsync(ClaimsPrincipal user, string checkKey, CancellationToken cancellationToken = default)
    {
        // The list pages have their own permissions; renumbering needs the Data checks one.
        if (!await CanRenumberAsync(user))
        {
            return AdminOperationResult.Failure("You are not allowed to renumber lists. Ask an administrator.", "Forbidden");
        }

        return await _integrity.NormaliseOrderAsync(checkKey, cancellationToken);
    }

    private async Task<bool> CanRenumberAsync(ClaimsPrincipal user) =>
        (await _authorization.AuthorizeAsync(user, null, AdminPolicies.IntegrityManage)).Succeeded;
}

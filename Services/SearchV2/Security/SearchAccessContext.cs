using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace ProjectManagement.Services.SearchV2.Security;

public sealed record SearchAccessContext(
    string UserId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> AllowedPolicies);

public interface ISearchAccessContextFactory
{
    Task<SearchAccessContext> CreateAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}

public sealed class SearchAccessContextFactory : ISearchAccessContextFactory
{
    // Exactly the policies any search source can require (see SearchSourcePolicies).
    private static readonly IReadOnlyList<string> SearchPolicies = SearchSourcePolicies.AllPolicies;

    private readonly IAuthorizationService _authorizationService;

    public SearchAccessContextFactory(IAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    public async Task<SearchAccessContext> CreateAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var roles = user.Claims
            .Where(claim => claim.Type == ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var allowed = new List<string>(SearchPolicies.Count);
        foreach (var policy in SearchPolicies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((await _authorizationService.AuthorizeAsync(user, policy)).Succeeded)
            {
                allowed.Add(policy);
            }
        }

        return new SearchAccessContext(userId, roles, allowed);
    }
}

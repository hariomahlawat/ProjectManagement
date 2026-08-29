using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Configuration;
using ProjectManagement.Services.SearchV2.Security;
using Xunit;

namespace ProjectManagement.Tests.Search;

public sealed class SearchV2AuthorizationTests
{
    [Fact]
    public async Task AccessContext_PreservesUserRolesAndOnlySuccessfulPolicies()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-42"),
            new Claim(ClaimTypes.Name, "search.tester"),
            new Claim(ClaimTypes.Role, "HoD"),
            new Claim(ClaimTypes.Role, "ITO"),
            new Claim(ClaimTypes.Role, "HoD")
        ], "Test"));

        var authorization = new SelectiveAuthorizationService(
        [
            Policies.Documents.View,
            Policies.Ipr.View
        ]);
        var factory = new SearchAccessContextFactory(authorization);

        var context = await factory.CreateAsync(principal, CancellationToken.None);

        Assert.Equal("user-42", context.UserId);
        Assert.Equal(new[] { "HoD", "ITO" }, context.Roles.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.Contains(Policies.Documents.View, context.AllowedPolicies);
        Assert.Contains(Policies.Ipr.View, context.AllowedPolicies);
        Assert.DoesNotContain(context.AllowedPolicies, value => string.IsNullOrWhiteSpace(value));
        Assert.Equal(2, context.AllowedPolicies.Count);
    }

    private sealed class SelectiveAuthorizationService : IAuthorizationService
    {
        private readonly HashSet<string> _allowedPolicies;

        public SelectiveAuthorizationService(IEnumerable<string> allowedPolicies)
        {
            _allowedPolicies = new HashSet<string>(allowedPolicies, StringComparer.OrdinalIgnoreCase);
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
            => Task.FromResult(_allowedPolicies.Contains(policyName)
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Services.SearchV2.Security;
using Xunit;

namespace ProjectManagement.Tests.Search;

public sealed class SearchV2AuthorizationTests
{
    [Fact]
    public async Task AccessContext_ContainsOnlyPoliciesTheUserIsAuthorizedToDiscover()
    {
        var allowed = new[]
        {
            Policies.Documents.View,
            ProjectOfficeReportsPolicies.ViewArpp
        };
        var factory = new SearchAccessContextFactory(new PolicyAuthorizationService(allowed));
        var user = Principal("user-42", "HoD", "ITO");

        var context = await factory.CreateAsync(user, CancellationToken.None);

        Assert.Equal("user-42", context.UserId);
        Assert.Contains("HoD", context.Roles);
        Assert.Contains("ITO", context.Roles);
        Assert.Contains(Policies.Documents.View, context.AllowedPolicies);
        Assert.Contains(ProjectOfficeReportsPolicies.ViewArpp, context.AllowedPolicies);
        Assert.DoesNotContain(Policies.Ipr.View, context.AllowedPolicies);
        Assert.DoesNotContain(ProjectOfficeReportsPolicies.ViewVisits, context.AllowedPolicies);
    }

    [Fact]
    public async Task AccessContext_DeduplicatesRoleClaims()
    {
        var factory = new SearchAccessContextFactory(new PolicyAuthorizationService(Array.Empty<string>()));
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "u1"),
            new Claim(ClaimTypes.Role, "HoD"),
            new Claim(ClaimTypes.Role, "HoD")
        ], "Test");

        var context = await factory.CreateAsync(new ClaimsPrincipal(identity), CancellationToken.None);

        Assert.Single(context.Roles);
        Assert.Equal("HoD", context.Roles[0]);
        Assert.Empty(context.AllowedPolicies);
    }

    private static ClaimsPrincipal Principal(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private sealed class PolicyAuthorizationService : IAuthorizationService
    {
        private readonly HashSet<string> _allowed;

        public PolicyAuthorizationService(IEnumerable<string> allowed) =>
            _allowed = new HashSet<string>(allowed, StringComparer.Ordinal);

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(_allowed.Contains(policyName)
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());
    }
}

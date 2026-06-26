using System.Collections.Generic;
using System.Security.Claims;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectMediaAccessGuardTests
{
    [Fact]
    public void CanViewProjectInformation_AllowsAnyAuthenticatedUser()
    {
        var project = new Project { Id = 1, Name = "Project", CreatedByUserId = "creator" };
        var principal = CreatePrincipal("viewer");

        Assert.True(ProjectAccessGuard.CanViewProjectInformation(project, principal));
    }

    [Fact]
    public void CanViewProjectInformation_RejectsAnonymousUser()
    {
        var project = new Project { Id = 1, Name = "Project", CreatedByUserId = "creator" };

        Assert.False(ProjectAccessGuard.CanViewProjectInformation(project, new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void CanManageProjectMedia_AllowsOnlyAssignedProjectOfficerOrHodAndAdmin()
    {
        var project = new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "creator",
            LeadPoUserId = "po-owner",
            HodUserId = "hod-owner"
        };

        Assert.True(ProjectAccessGuard.CanManageProjectMedia(project, CreatePrincipal("admin", "Admin"), "admin"));
        Assert.True(ProjectAccessGuard.CanManageProjectMedia(project, CreatePrincipal("po-owner", "Project Officer"), "po-owner"));
        Assert.True(ProjectAccessGuard.CanManageProjectMedia(project, CreatePrincipal("hod-owner", "HoD"), "hod-owner"));
        Assert.False(ProjectAccessGuard.CanManageProjectMedia(project, CreatePrincipal("po-other", "Project Officer"), "po-other"));
        Assert.False(ProjectAccessGuard.CanManageProjectMedia(project, CreatePrincipal("viewer"), "viewer"));
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}

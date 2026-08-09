using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Configuration;
using ProjectManagement.Pages.Workspace;
using ProjectManagement.Services.Workspace;

namespace ProjectManagement.Tests;

public sealed class OfficerConferencePageTests
{
    [Fact]
    public void ConferencePage_RequiresDedicatedCommandPolicy()
    {
        var attribute = Assert.Single(
            typeof(ConferenceModel)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>());

        Assert.Equal(Policies.ConferenceRemarks.Manage, attribute.Policy);
    }

    [Fact]
    public void ConferencePage_ExposesReadOnlyDirectionHistoryHandler()
    {
        var method = typeof(ConferenceModel).GetMethod(nameof(ConferenceModel.OnGetDirectionHistoryAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IActionResult>), method!.ReturnType);
    }
    [Fact]
    public void ProjectOfficerWorkspace_ConferenceView_IsAFirstClassRoute()
    {
        Assert.Equal(ProjectOfficerWorkspaceView.Conference, ProjectOfficerWorkspaceViewParser.Parse("conference"));
        Assert.Equal(ProjectOfficerWorkspaceView.Conference, ProjectOfficerWorkspaceViewParser.Parse("my-conference"));
        Assert.Equal("conference", ProjectOfficerWorkspaceView.Conference.ToRouteValue());
    }

    [Fact]
    public void ProjectOfficerWorkspace_DirectionHistoryHandler_DoesNotAcceptOfficerIdentity()
    {
        var method = typeof(IndexModel).GetMethod(nameof(IndexModel.OnGetDirectionHistoryAsync));

        Assert.NotNull(method);
        Assert.DoesNotContain(
            method!.GetParameters(),
            parameter => string.Equals(parameter.Name, "officerUserId", StringComparison.OrdinalIgnoreCase));
    }

}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Configuration;
using ProjectManagement.Pages.Workspace;

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
}

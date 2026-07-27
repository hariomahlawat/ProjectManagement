using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppLibraryAuthorizationContractTests
{
    [Theory]
    [InlineData(typeof(ProjectManagement.Pages.Projects.Arpp.IndexModel))]
    [InlineData(typeof(ProjectManagement.Pages.Projects.Arpp.HistoryModel))]
    [InlineData(typeof(ProjectManagement.Pages.Projects.Arpp.PrintModel))]
    public void PublishedLibraryPages_RequireAuthenticatedUsers(Type pageModelType)
    {
        var attributes = pageModelType
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .ToArray();

        Assert.NotEmpty(attributes);
    }
}

using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Pages.Projects.Tot;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class TotEditRedirectTests
{
    [Fact]
    public void OldEditAddress_OpensTheOverviewDrawerInEditMode()
    {
        var result = Assert.IsType<RedirectToPageResult>(new EditModel().OnGet(42));

        Assert.Equal("/Projects/Overview", result.PageName);
        Assert.Equal(42, result.RouteValues!["id"]);
        Assert.Equal("edit", result.RouteValues["tot"]);
    }

    [Fact]
    public void OldEditForm_PostAlsoRedirectsWithoutSaving()
    {
        var result = Assert.IsType<RedirectToPageResult>(new EditModel().OnPost(42));

        Assert.Equal("/Projects/Overview", result.PageName);
        Assert.Equal("edit", result.RouteValues!["tot"]);
    }
}

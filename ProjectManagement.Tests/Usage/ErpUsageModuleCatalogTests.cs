using Microsoft.AspNetCore.Http;
using ProjectManagement.Services.Usage;

namespace ProjectManagement.Tests.Usage;

public sealed class ErpUsageModuleCatalogTests
{
    [Theory]
    [InlineData("/Dashboard/Index", "dashboard")]
    [InlineData("/Projects/Ongoing", "projects")]
    [InlineData("/Calendar/Index", "calendar")]
    [InlineData("/Admin/Users/Index", "administration")]
    public void ResolvePath_ReturnsStablePrivacySafeModule(string path, string expected)
    {
        var catalog = new ErpUsageModuleCatalog();
        Assert.Equal(expected, catalog.ResolvePath(new PathString(path)));
    }

    [Fact]
    public void SimilarButDifferentRouteSegment_IsNotMisclassified()
    {
        var catalog = new ErpUsageModuleCatalog();
        Assert.Null(catalog.ResolvePath(new PathString("/ProjectsArchive/Index")));
    }

    [Fact]
    public void UnknownModule_IsRejected()
    {
        var catalog = new ErpUsageModuleCatalog();
        Assert.False(catalog.IsKnownModule("unknown-module"));
    }
}

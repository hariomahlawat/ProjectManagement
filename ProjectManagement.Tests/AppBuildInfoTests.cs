using ProjectManagement.Utilities;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AppBuildInfoTests
{
    [Fact]
    public void InformationalVersion_WithSourceLinkCommit_SplitsVersionAndCommit()
    {
        var (version, commit) = AppBuildInfo.ParseInformationalVersion("1.0.0+E5B656D23BA17719F9E8D1F9FD0EA7A618FA6F06");

        Assert.Equal("1.0.0", version);
        Assert.Equal("e5b656d23ba17719f9e8d1f9fd0ea7a618fa6f06", commit);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.2.0+local-build", "1.2.0")]
    [InlineData(null, "1.0")]
    [InlineData("  ", "1.0")]
    public void InformationalVersion_WithoutCommit_HasNoCommit(string? informational, string expectedVersion)
    {
        var (version, commit) = AppBuildInfo.ParseInformationalVersion(informational);

        Assert.Equal(expectedVersion, version);
        Assert.Null(commit);
    }

    [Theory]
    [InlineData("1.0.0", "1.0")]
    [InlineData("2.3.0", "2.3")]
    [InlineData("1.0.4", "1.0.4")]
    [InlineData("1.0", "1.0")]
    public void FriendlyVersion_DropsZeroPatch(string version, string expected)
    {
        Assert.Equal(expected, AppBuildInfo.FriendlyVersion(version));
    }

    [Fact]
    public void ShortCommit_IsTheFirstSevenCharacters()
    {
        var info = new AppBuildInfo("1.0", "c6582fd85aaaa", null, ".NET 8", DateTimeOffset.UnixEpoch);

        Assert.Equal("c6582fd", info.ShortCommit);
    }

    [Fact]
    public void PrismAreas_ListsSixAreasLeadingWithProjects()
    {
        Assert.Equal(6, PrismAreas.All.Count);
        Assert.Equal("Projects", PrismAreas.All[0].Name);
    }
}

using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppAdministrationPublishedPositionContractTests
{
    [Fact]
    public void AdministrationRegister_UsesPublishedLibraryPosition_NotWorkingAggregates()
    {
        var pageModel = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Index.cshtml.cs");
        var page = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Index.cshtml");

        Assert.Contains("IArppLibraryService libraryService", pageModel, StringComparison.Ordinal);
        Assert.Contains("GetCurrentPositionAsync", pageModel, StringComparison.Ordinal);
        Assert.Contains("query: null", pageModel, StringComparison.Ordinal);
        Assert.Contains("PublishedPositions", pageModel, StringComparison.Ordinal);

        Assert.Contains("Published position", page, StringComparison.Ordinal);
        Assert.Contains("publishedPosition.ApprovedIpaValue", page, StringComparison.Ordinal);
        Assert.Contains("publishedPosition.DelistedIpaValue", page, StringComparison.Ordinal);
        Assert.DoesNotContain("group.ApprovedLinkedIpaCost", page, StringComparison.Ordinal);
        Assert.DoesNotContain("group.DelistedLinkedIpaCost", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AdministrationRegister_StatesRecordAndRowBasisPrecisely()
    {
        var page = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Index.cshtml");

        Assert.Contains("structured", page, StringComparison.Ordinal);
        Assert.Contains("across @group.Issues.Count", page, StringComparison.Ordinal);
        Assert.Contains("No published position", page, StringComparison.Ordinal);
        Assert.Contains("under correction", page, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(relativePath).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }
}

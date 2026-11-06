using System.Text.RegularExpressions;
using ProjectManagement.Areas.Admin.Pages.Help;
using ProjectManagement.Services.Navigation.ModuleNav;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// Plain words in Administration (docs/ux/admin-module.md, PR 8): one eyebrow per group, the
// catalogue's sentence on each page, "How do I…" answers, and no compliance jargon in headers.
public sealed class AdminPlainWordsTests
{
    [Theory]
    [InlineData("Admin", "/Users/Details", "People")]
    [InlineData("Admin", "/Users/Reset", "People")]
    [InlineData("Admin", "/Lookups/SponsoringUnits/Index", "Reference lists")]
    [InlineData("Admin", "/Projects/Trash", "Restore & archive")]
    [InlineData("Admin", "/Diagnostics/SearchIndex", "System")]
    [InlineData("Admin", "/Help/Index", "Help")]
    public void Eyebrow_IsThePagesSidebarGroup(string area, string page, string expected)
    {
        Assert.Equal(expected, AdminNavigationCatalog.EyebrowFor(area, page, null, "Old eyebrow"));
    }

    [Fact]
    public void Home_HasNoEyebrow_AndPagesOutsideTheCatalogueKeepTheirOwn()
    {
        Assert.Null(AdminNavigationCatalog.EyebrowFor("Admin", "/Index", null, "Anything"));
        Assert.Equal("Own words", AdminNavigationCatalog.EyebrowFor("Elsewhere", "/Somewhere", null, "Own words"));
    }

    [Fact]
    public void HowDoI_EveryAnswerPointsToARealPage()
    {
        Assert.InRange(IndexModel.Questions.Count, 8, 15);
        Assert.All(IndexModel.Questions, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Question));
            Assert.EndsWith(".", item.Answer);
            Assert.NotNull(AdminNavigationCatalog.GetEntry(item.NavigationKey));
        });
    }

    // Words the UX review found on admin pages that explain nothing to a new administrator.
    private static readonly string[] Jargon =
    {
        "governed", "governance", "assurance", "taxonomy", "concurrency-protected",
        "controlled purge", "Operational oversight", "Master data ·", "deterministic"
    };

    [Fact]
    public void AdminPageHeaders_UsePlainWords()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "Areas", "Admin", "Pages"), "*.cshtml.cs", SearchOption.AllDirectories)
            .Concat(new[]
            {
                Path.Combine(root, "Pages", "Settings", "Holidays", "Index.cshtml.cs"),
                Path.Combine(root, "Pages", "Celebrations", "Index.cshtml.cs")
            });

        var offenders = new List<string>();
        foreach (var file in files)
        {
            // Header text: the Eyebrow, Title and Description given to AdminPageHeaderModel.
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"(Eyebrow|Title|Description)\s*=\s*\$?""(?<text>[^""]*)"""))
            {
                var text = match.Groups["text"].Value;
                offenders.AddRange(Jargon
                    .Where(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))
                    .Select(word => $"{Path.GetRelativePath(root, file)}: '{word}' in \"{text}\""));
            }
        }

        Assert.Empty(offenders);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

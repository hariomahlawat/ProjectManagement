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

    [Fact]
    public void HowDoI_NeverPromisesAFixedUndoTime()
    {
        // The chat undo window is configurable (Chat:Attachments:UndoHours); the answer points to
        // the deadline the storage page shows instead of naming a period.
        var chat = IndexModel.Questions.Single(item => item.NavigationKey == AdminNavigationKeys.ChatStorage);

        Assert.DoesNotContain("day", chat.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hour", chat.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recent clean-ups", chat.Answer, StringComparison.Ordinal);
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

    // Second-level pages (docs/ux/admin-review-fixes.md, PR 10): their visible text is plain.
    // Anything inside <details> (the "Technical details" panels) may stay technical.
    private static readonly string[] SecondLevelJargon =
    {
        "Search V2", "projection", "atomic", "ranker", "idempotent", "coordinator", "Outbox",
        "dead-letter", "Materially", "posture", "non-adoption", "effective scope",
        "Analytics classification", "telemetry", "embedding", "Current filter scope", "day(s)",
        "soft-deleted", "Controlled", "Diagnostic", "heartbeat", "ingestion", "Deduplicated"
    };

    [Theory]
    [InlineData("Areas/Admin/Pages/Diagnostics/SearchIndex.cshtml")]
    [InlineData("Pages/Admin/MediaSources/Index.cshtml")]
    [InlineData("Pages/Admin/MediaIntelligence/Index.cshtml")]
    [InlineData("Areas/ProjectOfficeReports/Pages/Projects/LegacyImport.cshtml")]
    [InlineData("Areas/Admin/Pages/Documents/IngestExternalPdfs.cshtml")]
    [InlineData("Areas/Admin/Pages/Analytics/Logins.cshtml")]
    [InlineData("Pages/Usage/Index.cshtml")]
    [InlineData("Areas/Admin/Pages/Users/Details.cshtml")]
    [InlineData("Areas/Admin/Pages/Projects/Trash.cshtml")]
    [InlineData("Areas/Admin/Pages/Documents/Recycle.cshtml")]
    [InlineData("Areas/Admin/Pages/Calendar/Deleted.cshtml")]
    public void SecondLevelPages_UsePlainWordsOutsideTechnicalDetails(string view)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), view));
        text = Regex.Replace(text, @"@\*.*?\*@", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, @"<details\b.*?</details>", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = Regex.Replace(text, @"\b\w+\s*=\s*(?=[A-Za-z_])", " "); // route values such as posture = Model.Posture
        text = Regex.Replace(text, @"\b[A-Za-z_][\w]*(\.[A-Za-z_][\w]*)+", " "); // code such as Model.OutboxRuntime

        var found = SecondLevelJargon.Where(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.True(found.Length == 0, $"{view}: {string.Join(", ", found)}");
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

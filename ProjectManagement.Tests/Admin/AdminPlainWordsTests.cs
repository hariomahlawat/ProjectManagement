using System.Text.RegularExpressions;
using ProjectManagement.Areas.Admin.Pages.Help;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
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
        "soft-deleted", "Controlled", "Diagnostic", "heartbeat", "ingestion", "Deduplicated",
        // Second review (docs/ux/admin-review-2-fixes.md, PR 8).
        "Reclassify", "stale", "lifecycle", "capabilit", "Operational archive", "deletion retention",
        "directory", "Independent of user profile", "admission", "human-account", "SessionRevalidation"
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
    [InlineData("Areas/Admin/Pages/AccessGovernance/Index.cshtml")]
    [InlineData("Pages/Admin/MediaIntelligence/Classifications.cshtml")]
    [InlineData("Areas/Admin/Pages/Users/Create.cshtml")]
    [InlineData("Areas/Admin/Pages/Users/Edit.cshtml")]
    [InlineData("Areas/Admin/Pages/Shared/_AdminRoleSelectionGrid.cshtml")]
    [InlineData("Areas/Admin/Pages/Projects/Archived.cshtml")]
    [InlineData("Pages/Settings/Holidays/Index.cshtml")]
    [InlineData("Pages/Celebrations/Index.cshtml")]
    [InlineData("Areas/Admin/Pages/Help/Index.cshtml")]
    [InlineData("Areas/Admin/Pages/Diagnostics/DbHealth.cshtml")]
    public void SecondLevelPages_UsePlainWordsOutsideTechnicalDetails(string view)
    {
        var text = VisibleText(view);

        var found = SecondLevelJargon.Where(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.True(found.Length == 0, $"{view}: {string.Join(", ", found)}");
    }

    // System health's rows are written by the health services, not the page: their messages are
    // plain words too (docs/ux/admin-review-2-fixes.md, PR 8). Setting names may appear only in
    // the "what to do" text, where whoever runs the server needs them.
    private static readonly string[] HealthJargon =
    {
        "migration(s)", "assembly", "cadence", "processing cycle", "Data Protection", "Storage capacity",
        "Connectivity", "metadata", "diagnostic reference", "permitted execution", "lineage",
        "Application runtime", "Primary upload storage", "Background services", "Application security"
    };

    // The background jobs' last-run lines are written by the jobs themselves
    // (docs/ux/admin-review-3-fixes.md, PR 6): no "(s)" plurals, no "Purged", no setting names.
    private static readonly string[] JobJargon =
    {
        "(s)", "Purged", "aggregat", "already exists", "disabled by configuration", "retention is off",
        "TrashRetentionDays", "bucket", "auto-marked", "Login aggregation"
    };

    [Theory]
    [InlineData("Services/Admin/AdminSystemHealthService.cs")]
    [InlineData("Services/Admin/DatabaseHealthService.cs")]
    [InlineData("Services/Admin/BackgroundJobMessages.cs")]
    [InlineData("Hosted/NotebookTrashRetentionWorker.cs")]
    [InlineData("Hosted/DocRepoTrashPurgeWorker.cs")]
    [InlineData("Hosted/ChatAttachmentCleanupWorker.cs")]
    [InlineData("Hosted/AuditRetentionWorker.cs")]
    [InlineData("Services/Usage/UserActivityRetentionWorker.cs")]
    [InlineData("Services/TodoPurgeWorker.cs")]
    [InlineData("Services/LoginAggregationWorker.cs")]
    [InlineData("Services/Notifications/NotificationRetentionService.cs")]
    [InlineData("Services/Projects/ProjectRetentionWorker.cs")]
    public void SystemHealthMessages_UsePlainWords(string source)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), source));
        var literals = Regex.Matches(text, @"\$?""(?<text>[^""\r\n]*)""")
            .Select(match => match.Groups["text"].Value)
            .Where(literal => literal.Contains(' '))  // sentences and labels, not keys or log templates below
            .ToArray();
        var logTemplates = Regex.Matches(text, @"_logger\.Log\w+\(.*?\);", RegexOptions.Singleline)
            .Select(match => match.Value)
            .ToArray();

        var found = HealthJargon.Concat(JobJargon)
            .Where(word => literals.Any(literal => literal.Contains(word, StringComparison.OrdinalIgnoreCase)
                && !logTemplates.Any(log => log.Contains(literal, StringComparison.Ordinal))))
            .ToArray();

        Assert.True(found.Length == 0, $"{source}: {string.Join(", ", found)}");
    }

    // Who has which role (docs/ux/admin-review-2-fixes.md, PR 7): permission names such as
    // "Admin.Access" and words such as "privileged" appear only inside Technical details.
    private static readonly string[] RoleJargon =
    {
        "policy", "policies", "privileged", "capability", "capabilities", "provisioned", "elevated",
        "authoritative", "metadata", "single-holder", "identity roles", "finding", "findings", "granted"
    };

    [Fact]
    public void WhoHasWhichRole_ShowsNoPermissionNamesOutsideTechnicalDetails()
    {
        const string view = "Areas/Admin/Pages/AccessGovernance/Index.cshtml";
        var raw = File.ReadAllText(Path.Combine(FindRepositoryRoot(), view));
        Assert.Contains("<summary><h2>Technical details</h2></summary>", raw, StringComparison.Ordinal);

        var text = VisibleText(view);
        var found = RoleJargon
            .Where(word => Regex.IsMatch(text, $@"(?<![\w-]){Regex.Escape(word)}(?![\w-])", RegexOptions.IgnoreCase))
            .ToArray();

        Assert.True(found.Length == 0, $"{view}: {string.Join(", ", found)}");
    }

    [Fact]
    public void WhatEachRoleCanDo_DescribesRolesAndPermissionsInPlainWords()
    {
        string[] jargon =
        {
            "compatibility", "authorised", "authorized", "controlled", "metadata", "workflow",
            "reconcile", "privileged", "provision", "maintain", "legacy", "capability"
        };
        var texts = new AdminCapabilityCatalog().Capabilities
            .SelectMany(item => new[] { $"{item.Key}: {item.Title}", $"{item.Key}: {item.Description}" })
            .Concat(new AdminRoleDescriptorCatalog()
                .DescribeMany(RoleNames.AssignableRoles.Concat(RoleNames.LegacyAliases))
                .Select(role => $"{role.Name}: {role.DisplayName}: {role.Description}"));

        var offenders = texts
            .SelectMany(text => jargon
                .Where(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))
                .Select(word => $"'{word}' in \"{text}\""))
            .ToArray();

        Assert.True(offenders.Length == 0, string.Join(Environment.NewLine, offenders));
    }

    // Third review (docs/ux/admin-review-3-fixes.md, PR 7): formal and unclear phrases that were
    // rewritten. Each file is checked whole, so the words cannot come back in a label or message.
    [Theory]
    [InlineData("Areas/Admin/Pages/Users/Edit.cshtml", "from this workflow")]
    [InlineData("Areas/Admin/Pages/Users/Edit.cshtml.cs", "Update identity details", "Account classification", "Assign at least one role")]
    [InlineData("Areas/Admin/Pages/Users/Reset.cshtml", "This operation", "approved secure channel", "temporary credential", "Security handling", "Password policy", "Issue a temporary password")]
    [InlineData("Areas/Admin/Pages/Users/Reset.cshtml.cs", "Acknowledge the security impact", "Issue a new temporary password", "a password change is required")]
    [InlineData("Areas/Admin/Pages/Users/Disable.cshtml", "Block account access", "operational or administrative", "retained in the audit trail", "requiring immediate access revocation", "attribution are preserved", "Use disable when")]
    [InlineData("Areas/Admin/Pages/Users/Index.cshtml", "Last activity", "Password change required")]
    [InlineData("Areas/Admin/Pages/Users/Index.cshtml.cs", "\"Password change\"")]
    [InlineData("Areas/Admin/Pages/Users/Details.cshtml", "<dt>Password change</dt>")]
    [InlineData("Areas/Admin/Pages/Shared/_AdminRoleSelectionGrid.cshtml", "Privileged access selected", "operational need")]
    [InlineData("Pages/Settings/Holidays/Index.cshtml", "RH · Office holiday", "Declare as office holiday", "Withdraw office observance", "office observance are separate")]
    [InlineData("Pages/Settings/Holidays/Observe.cshtml", "RH · Office holiday", "Declare", "Controlled decision", "persisted")]
    [InlineData("Pages/Settings/Holidays/WithdrawObservance.cshtml", "RH · Office holiday", "Withdraw observance", "Withdraw RH", "Withdraw office observance", "Controlled decision", "persisted")]
    [InlineData("Areas/Admin/Pages/Help/Index.cshtml", "Declare as office holiday", "Withdraw office observance")]
    [InlineData("Services/Admin/Calendar/HolidayAdminService.cs", "Office observance withdrawn.", "observance is declared")]
    [InlineData("Areas/Admin/Pages/MasterData/Integrity/Index.cshtml", "I looked at what was found")]
    [InlineData("Pages/Admin/MediaIntelligence/Classifications.cshtml", "Strength @", "<span>Lead</span>", "btn-outline-secondary\" asp-page=\"/Admin/MediaIntelligence/Index\">Overview")]
    [InlineData("Pages/Admin/MediaSources/Index.cshtml", "is operational, but", "migration(s)", "Outside folders are switched off", "Photos works as normal", "production configuration")]
    public void ThirdReviewPhrases_StayGone(string file, params string[] phrases)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), file));

        var found = phrases.Where(phrase => text.Contains(phrase, StringComparison.Ordinal)).ToArray();

        Assert.True(found.Length == 0, $"{file}: {string.Join(" | ", found)}");
    }

    [Fact]
    public void Holidays_OfficeHolidayActionsAreAMatchedPair()
    {
        var list = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Pages/Settings/Holidays/Index.cshtml"));

        Assert.Contains(">Make it an office holiday</a>", list, StringComparison.Ordinal);
        Assert.Contains(">Stop office holiday</a>", list, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassificationCards_ShowScoresAsPointsOutOf100()
    {
        var row = ClassificationRow(predicted: 0.37m, runnerUp: 0.36, margin: 0.01);

        Assert.Equal("Score 37 of 100", row.PredictedScoreText);
        Assert.Equal("Score 36 of 100", row.RunnerUpScoreText);
        Assert.Equal("1 point", row.LeadText);
        Assert.Equal("12 points", ClassificationRow(0.6m, 0.48, 0.12).LeadText);
        // Both show as 37: the card says so instead of "ahead by 1 point".
        Assert.Equal("Less than 1 point", ClassificationRow(0.374m, 0.366, 0.008).LeadText);
    }

    private static ProjectManagement.Pages.Admin.MediaIntelligence.ClassificationsModel.Row ClassificationRow(
        decimal predicted, double runnerUp, double margin) =>
        new(1, "Image", "Context", "image.jpg",
            default, predicted, default, runnerUp, margin,
            default, null, false, null, Array.Empty<string>(), false, string.Empty, string.Empty,
            default, DateTimeOffset.UnixEpoch, default, null, Guid.Empty,
            0, 0, 0, 0, 0, false, false, false, null);

    // The list forms (docs/ux/admin-review-4-fixes.md, PR 5, U1): the governance wording of the
    // old Add and Edit pages and their guidance boxes stays gone.
    private static readonly string[] ListFormJargon =
    {
        "controlled reference value", "stable across reporting periods", "Reference-list governance",
        "historical meaning", "Change control", "stale concurrent edits", "operational activity classification",
        "suitable for management reporting", "Create a root category", "root technology domain",
        "Classification guidance", "Hierarchy guidance", "Taxonomy guidance", "Calendar behaviour",
        "classification", "taxonomy", "Create project type", "Create category", "Create activity type",
        "Create technical category", "Save changes", "Save value", "Inactive values remain",
        "informational by default", "silently", "Visibility follows", "Hierarchy cycles", "descendants"
    };

    public static IEnumerable<object[]> ListFormViews() => new[]
    {
        "Areas/Admin/Pages/Shared/_AdminListForm.cshtml",
        "Areas/Admin/Pages/Lookups/_LookupForm.cshtml",
        "Areas/Admin/Pages/Categories/Create.cshtml",
        "Areas/Admin/Pages/Categories/Edit.cshtml",
        "Areas/Admin/Pages/TechnicalCategories/Create.cshtml",
        "Areas/Admin/Pages/TechnicalCategories/Edit.cshtml",
        "Areas/Admin/Pages/ActivityTypes/Create.cshtml",
        "Areas/Admin/Pages/ActivityTypes/Edit.cshtml",
        "Areas/Admin/Pages/MasterData/ArppReferences/_ArppReferenceForm.cshtml",
        "Areas/Admin/Pages/MasterData/ArppReferences/Index.cshtml",
        "Areas/DocumentRepository/Pages/Admin/_DocRepoListForm.cshtml",
        "Areas/DocumentRepository/Pages/Admin/_DocRepoListPage.cshtml",
        "Areas/ProjectOfficeReports/Pages/Admin/SocialMediaTypes/New.cshtml",
        "Areas/ProjectOfficeReports/Pages/Admin/SocialMediaTypes/Edit.cshtml",
        "Areas/ProjectOfficeReports/Pages/Admin/SocialMediaTypes/Platforms/New.cshtml",
        "Areas/ProjectOfficeReports/Pages/Admin/SocialMediaTypes/Platforms/Edit.cshtml",
        "Areas/Admin/Models/AdminListFormModel.cs"
    }.Select(view => new object[] { view });

    [Theory]
    [MemberData(nameof(ListFormViews))]
    public void ListForms_AndGuidanceBoxes_UsePlainWords(string view)
    {
        var text = view.EndsWith(".cs", StringComparison.Ordinal)
            ? string.Join(" ", Regex.Matches(File.ReadAllText(Path.Combine(FindRepositoryRoot(), view)), @"""(?<text>[^""
]*)""").Select(match => match.Groups["text"].Value))
            : VisibleText(view);

        var found = ListFormJargon.Where(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.True(found.Length == 0, $"{view}: {string.Join(", ", found)}");
    }

    // U2: every guidance box left (Holidays, Celebrations) is styled and in plain words.
    [Fact]
    public void GuidanceBoxes_AreStyled_AndUsePlainWords()
    {
        var root = FindRepositoryRoot();
        var css = File.ReadAllText(Path.Combine(root, "wwwroot", "css", "admin.css"));
        Assert.Contains(".admin-guidance-card {", css, StringComparison.Ordinal);
        Assert.Contains(".admin-switch-row {", css, StringComparison.Ordinal);

        var boxes = Directory.EnumerateFiles(Path.Combine(root, "Areas"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Pages"), "*.cshtml", SearchOption.AllDirectories))
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"<aside class=""admin-guidance-card"">.*?</aside>", RegexOptions.Singleline)
                .Select(match => (File: Path.GetRelativePath(root, file), Text: Regex.Replace(match.Value, "<[^>]+>", " "))))
            .ToArray();

        Assert.NotEmpty(boxes);
        var offenders = boxes
            .SelectMany(box => ListFormJargon.Where(word => box.Text.Contains(word, StringComparison.OrdinalIgnoreCase)).Select(word => $"{box.File}: {word}"))
            .ToArray();
        Assert.True(offenders.Length == 0, string.Join("; ", offenders));
    }

    // The page's text as a reader sees it: no comments, no Technical details, no markup or code.
    private static string VisibleText(string view)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), view));
        text = Regex.Replace(text, @"@\*.*?\*@", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, @"<details\b.*?</details>", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = Regex.Replace(text, @"\b\w+\s*=\s*(?=[A-Za-z_])", " "); // route values such as posture = Model.Posture
        text = Regex.Replace(text, @"\b[A-Za-z_][\w]*(\.[A-Za-z_][\w]*)+", " "); // code such as Model.OutboxRuntime
        return text;
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

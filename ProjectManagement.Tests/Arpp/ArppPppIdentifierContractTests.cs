using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppPppIdentifierContractTests
{
    [Fact]
    public void DomainAndMigration_PreservePppNumberAndCategorySpecificIdentifiers()
    {
        var workingEntry = ReadRepoFile("Models", "Arpp", "ArppEntry.cs");
        var publishedEntry = ReadRepoFile("Models", "Arpp", "ArppPublishedEntry.cs");
        var migration = ReadRepoFile("Migrations", "20261207160000_AddArppPppNumberAndCategoryIdentifiers.cs");
        var commandService = ReadRepoFile("Services", "Arpp", "ArppCommandService.cs");

        Assert.Contains("string? PppNumber", workingEntry, StringComparison.Ordinal);
        Assert.Contains("string? PppNumber", publishedEntry, StringComparison.Ordinal);
        Assert.Contains("AddColumn<string>", migration, StringComparison.Ordinal);
        Assert.Contains("name: \"PppNumber\"", migration, StringComparison.Ordinal);
        Assert.Contains("WHERE \"Category\" = 4", migration, StringComparison.Ordinal);
        Assert.Contains("entity.PppNumber = entity.Category == ArppCategory.Delisted", commandService, StringComparison.Ordinal);
        Assert.Contains("ValidateRequiredText(errors, index, nameof(entry.PppNumber)", commandService, StringComparison.Ordinal);
        Assert.Contains("PppNumber = entry.PppNumber", commandService, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagementAndPublishedViews_TreatDelistedIdentifiersAsNotApplicable()
    {
        var manageMarkup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");
        var manageScript = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");
        var currentPosition = ReadRepoFile("Pages", "Projects", "Arpp", "_CurrentPositionTable.cshtml");
        var briefing = ReadRepoFile("Services", "ProjectBriefings", "Presentation", "ProjectBriefingSlideComposer.UpdateSheet.cs");

        Assert.Contains("PPP no.", manageMarkup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Serial No. and PPP No. do not apply", manageMarkup, StringComparison.Ordinal);
        Assert.Contains("input.required = !delisted", manageScript, StringComparison.Ordinal);
        Assert.Contains("input.readOnly = delisted", manageScript, StringComparison.Ordinal);
        Assert.Contains("showIssuedIdentifiers", currentPosition, StringComparison.Ordinal);
        Assert.Contains("ArppPppNumberApplicable", briefing, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var root = ResolveRepoRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()));
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }
}

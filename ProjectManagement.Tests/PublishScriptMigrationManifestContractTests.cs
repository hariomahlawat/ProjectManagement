using System.Text.RegularExpressions;
using Xunit;

namespace ProjectManagement.Tests;

// Guards hariomahlawat/ProjectManagement#2683: the offline publish script must derive its
// migration-manifest expectations from the repository instead of a hard-coded count/tail
// that silently goes stale every time a migration is added.
public sealed class PublishScriptMigrationManifestContractTests
{
    [Fact]
    public void PublishScript_DoesNotHardCodeMigrationCountOrIdentifiers()
    {
        var script = ReadRepoFile("ops", "publish", "create-publish-folder.ps1");

        var hardCodedIds = Regex.Matches(script, @"""\d{14}_[A-Za-z0-9_]+""")
            .Select(match => match.Value)
            .ToArray();
        Assert.True(
            hardCodedIds.Length == 0,
            "Publish script hard-codes migration identifiers: " + string.Join(", ", hardCodedIds));

        Assert.DoesNotMatch(new Regex(@"[Mm]anifest\.Count\s+-(ne|eq|lt|gt|le|ge)\s+[1-9]\d*"), script);
    }

    [Fact]
    public void PublishScript_ValidatesSourceAndPublishedManifestsAgainstMigrationSources()
    {
        var script = ReadRepoFile("ops", "publish", "create-publish-folder.ps1");

        Assert.Contains("function Assert-ApplicationMigrationManifest", script, StringComparison.Ordinal);
        Assert.Contains(@"\[Migration\(""([^""]+)""\)\]", script, StringComparison.Ordinal);
        Assert.Contains("CompareOrdinal", script, StringComparison.Ordinal);
        Assert.Contains("$manifest = Assert-ApplicationMigrationManifest", script, StringComparison.Ordinal);
        Assert.Contains(
            "($publishedManifest -join \"`n\") -cne ($manifest -join \"`n\")",
            script,
            StringComparison.Ordinal);

        // Validation must happen before any publish output is produced.
        Assert.True(
            script.IndexOf("$manifest = Assert-ApplicationMigrationManifest", StringComparison.Ordinal)
            < script.IndexOf("dotnet publish", StringComparison.Ordinal));
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, Path.Combine(relativePath));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file: {Path.Combine(relativePath)}");
    }
}

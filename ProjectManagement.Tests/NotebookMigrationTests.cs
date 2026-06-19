using System.Text.RegularExpressions;

namespace ProjectManagement.Tests;

public sealed class NotebookMigrationTests
{
    [Fact]
    public void Notebook_version_migration_sorts_after_module_migration()
    {
        // SECTION: Migration order regression guard
        var migrations = Directory.EnumerateFiles(GetRepoRoot(), "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && Regex.IsMatch(name, @"^\d{14}_"))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var module = Assert.Single(migrations.Where(name => name.EndsWith("_AddNotebookModule", StringComparison.Ordinal)));
        var version = Assert.Single(migrations.Where(name => name.EndsWith("_AddNotebookItemVersion", StringComparison.Ordinal)));

        Assert.True(string.CompareOrdinal(module, version) < 0, $"{module} must sort before {version}.");
    }

    [Fact]
    public void Notebook_version_migration_backfills_before_enforcing_not_null()
    {
        // SECTION: Existing database upgrade regression guard
        var migration = File.ReadAllText(Path.Combine(GetRepoRoot(), "20261125231000_AddNotebookItemVersion.cs"));

        Assert.Contains("nullable: true", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pgcrypto", migration, StringComparison.Ordinal);
        Assert.Contains("SET \"Version\" = gen_random_uuid()", migration, StringComparison.Ordinal);
        Assert.Contains("OR \"Version\" = '00000000-0000-0000-0000-000000000000'", migration, StringComparison.Ordinal);
        Assert.Contains("nullable: false", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue: Guid.Empty", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Notebook_version_repair_migration_is_idempotent()
    {
        // SECTION: Retained database repair guard
        var migration = File.ReadAllText(Path.Combine(GetRepoRoot(), "20261125232000_RepairMissingNotebookItemVersion.cs"));

        Assert.Contains("ADD COLUMN IF NOT EXISTS \"Version\" uuid", migration, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN \"Version\" SET NOT NULL", migration, StringComparison.Ordinal);
    }

    private static string GetRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var migrations = Path.Combine(current, "Migrations");
            if (Directory.Exists(migrations))
            {
                return migrations;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Unable to locate the repository Migrations directory.");
    }
}

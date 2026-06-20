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

        var addColumnIndex = migration.IndexOf("ADD COLUMN IF NOT EXISTS \"Version\" uuid", StringComparison.Ordinal);
        var updateIndex = migration.IndexOf("SET \"Version\" = gen_random_uuid()", StringComparison.Ordinal);
        var setNotNullIndex = migration.IndexOf("ALTER COLUMN \"Version\" SET NOT NULL", StringComparison.Ordinal);

        Assert.True(addColumnIndex >= 0, "The Version column must be added conditionally.");
        Assert.True(updateIndex > addColumnIndex, "Existing rows must be backfilled after the column is added.");
        Assert.True(setNotNullIndex > updateIndex, "NOT NULL must be enforced only after backfilling.");
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

namespace ProjectManagement.Infrastructure;

/// <summary>
/// Loads the immutable migration lineage committed with the application. The manifest is
/// a second, deployment-time guard against accidentally publishing an incomplete migration
/// assembly or omitting historical bridge migrations during a merge.
/// </summary>
public static class MigrationLineageManifest
{
    public static IReadOnlyList<string> LoadRequired(string path, string migrationSetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationSetName);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The immutable migration manifest for {migrationSetName} is missing: {path}. " +
                "Publish the complete application output; partial deployments are unsupported.");
        }

        var ids = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        if (ids.Length == 0)
        {
            throw new InvalidOperationException(
                $"The immutable migration manifest for {migrationSetName} is empty: {path}.");
        }

        var invalid = ids
            .Where(id => id.Length < 16 || !id.Take(14).All(char.IsDigit) || id[14] != '_')
            .ToArray();
        if (invalid.Length > 0)
        {
            throw new InvalidOperationException(
                $"The immutable migration manifest for {migrationSetName} contains invalid identifier(s): " +
                string.Join(", ", invalid));
        }

        var duplicates = ids
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"The immutable migration manifest for {migrationSetName} contains duplicate identifier(s): " +
                string.Join(", ", duplicates));
        }

        var sorted = ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (!ids.SequenceEqual(sorted, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"The immutable migration manifest for {migrationSetName} is not ordered by migration identifier: {path}.");
        }

        return ids;
    }
}

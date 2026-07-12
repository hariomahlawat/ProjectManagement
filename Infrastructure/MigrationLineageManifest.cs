using System.Reflection;

namespace ProjectManagement.Infrastructure;

/// <summary>
/// Loads the immutable migration lineage committed with the application. The manifest is
/// a second, deployment-time guard against accidentally publishing an incomplete migration
/// assembly or omitting historical bridge migrations during a merge.
/// </summary>
public static class MigrationLineageManifest
{
    public static IReadOnlyList<string> LoadRequired(
        string path,
        string migrationSetName,
        Assembly? fallbackAssembly = null,
        string? fallbackResourceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationSetName);

        string[] lines;
        string sourceDescription;

        if (File.Exists(path))
        {
            lines = File.ReadAllLines(path);
            sourceDescription = path;
        }
        else if (fallbackAssembly is not null && !string.IsNullOrWhiteSpace(fallbackResourceName))
        {
            using var stream = fallbackAssembly.GetManifestResourceStream(fallbackResourceName);
            if (stream is null)
            {
                throw new InvalidOperationException(
                    $"The immutable migration manifest for {migrationSetName} is missing from both " +
                    $"the publish output ('{path}') and embedded resource '{fallbackResourceName}'. " +
                    "Publish the complete application output generated from one source revision.");
            }

            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            sourceDescription = $"embedded resource {fallbackResourceName}";
        }
        else
        {
            throw new InvalidOperationException(
                $"The immutable migration manifest for {migrationSetName} is missing: {path}. " +
                "Publish the complete application output; partial deployments are unsupported.");
        }

        var ids = lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        if (ids.Length == 0)
        {
            throw new InvalidOperationException(
                $"The immutable migration manifest for {migrationSetName} is empty: {sourceDescription}.");
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
                $"The immutable migration manifest for {migrationSetName} is not ordered by migration identifier: " +
                sourceDescription + ".");
        }

        return ids;
    }
}

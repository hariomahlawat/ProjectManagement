namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Resolves the bundled publication fonts identically for QuestPDF composition and
/// SkiaSharp pagination. The resolver never contacts the network and does not depend
/// on the IIS worker process current directory.
/// </summary>
public static class PublicationFontContract
{
    public const string ExternalFontRootEnvironmentVariable = "PRISM_PUBLICATION_FONTS_DIR";

    public static IReadOnlyList<string> RequiredDmSansFiles { get; } = Array.AsReadOnly(new[]
    {
        "DMSans-Regular.ttf",
        "DMSans-Medium.ttf",
        "DMSans-SemiBold.ttf",
        "DMSans-Bold.ttf",
        "DMSans-Italic.ttf",
        "DMSans-BoldItalic.ttf"
    });

    public static PublicationDmSansResolution InspectDmSans(
        string? contentRootPath = null,
        string? webRootPath = null)
    {
        var attempted = CandidatePublicationRoots(contentRootPath, webRootPath)
            .Select(root => Path.Combine(root, "dm-sans"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var directory in attempted)
        {
            var missing = RequiredDmSansFiles
                .Where(file => !File.Exists(Path.Combine(directory, file)))
                .ToArray();
            if (missing.Length == 0)
            {
                return new PublicationDmSansResolution(directory, attempted, Array.Empty<string>());
            }
        }

        return new PublicationDmSansResolution(
            null,
            attempted,
            RequiredDmSansFiles.ToArray());
    }

    public static string ResolveRequiredDmSansFile(
        string fileName,
        string? contentRootPath = null,
        string? webRootPath = null)
    {
        if (!RequiredDmSansFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(fileName), fileName, "The requested file is not part of the DM Sans publication contract.");
        }

        var resolution = InspectDmSans(contentRootPath, webRootPath);
        if (!resolution.IsAvailable)
        {
            throw CreateMissingFontException(resolution);
        }

        var path = Path.Combine(resolution.DirectoryPath!, fileName);
        if (!File.Exists(path))
        {
            // The directory may have changed after inspection (for example during an unsafe
            // in-place deployment). Fail deterministically rather than selecting a host font.
            throw CreateMissingFontException(resolution);
        }

        return path;
    }

    public static InvalidOperationException CreateMissingFontException(PublicationDmSansResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var checkedLocations = resolution.AttemptedDirectories.Count == 0
            ? "no publication font directories were configured"
            : string.Join("; ", resolution.AttemptedDirectories);
        return new InvalidOperationException(
            "The complete bundled DM Sans publication font set was not found. "
            + $"Expected: {string.Join(", ", RequiredDmSansFiles)}. Checked: {checkedLocations}. "
            + $"Publish wwwroot/fonts/publications/dm-sans or set {ExternalFontRootEnvironmentVariable} to the offline publication-font root.");
    }

    public static IReadOnlyList<string> CandidatePublicationRoots(
        string? contentRootPath = null,
        string? webRootPath = null)
    {
        var roots = new List<string>();

        AddExternalRoot(roots, Environment.GetEnvironmentVariable(ExternalFontRootEnvironmentVariable));
        AddContentRootCandidates(roots, contentRootPath);

        if (!string.IsNullOrWhiteSpace(webRootPath))
        {
            Add(roots, Path.Combine(webRootPath, "fonts", "publications"));
        }

        // An explicitly supplied host root is authoritative. Do not let an incomplete IIS
        // deployment pass validation by finding a developer checkout, test output directory or
        // machine-local font copy elsewhere in the worker process search path.
        if (!string.IsNullOrWhiteSpace(contentRootPath) || !string.IsNullOrWhiteSpace(webRootPath))
        {
            return roots
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        AddContentRootCandidates(roots, AppContext.BaseDirectory);
        Add(roots, Path.Combine(AppContext.BaseDirectory, "fonts", "publications"));
        AddContentRootCandidates(roots, Directory.GetCurrentDirectory());

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 7; depth++, directory = directory.Parent)
        {
            AddContentRootCandidates(roots, directory.FullName);
        }

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddExternalRoot(ICollection<string> roots, string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return;

        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath.Trim()));
        if (string.Equals(Path.GetFileName(fullPath), "dm-sans", StringComparison.OrdinalIgnoreCase))
        {
            Add(roots, Directory.GetParent(fullPath)?.FullName);
            return;
        }

        Add(roots, fullPath);
    }

    private static void AddContentRootCandidates(ICollection<string> roots, string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath)) return;
        Add(roots, Path.Combine(basePath, "Resources", "Publications", "Fonts"));
        Add(roots, Path.Combine(basePath, "wwwroot", "fonts", "publications"));
    }

    private static void Add(ICollection<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        roots.Add(Path.GetFullPath(path));
    }
}

public sealed record PublicationDmSansResolution(
    string? DirectoryPath,
    IReadOnlyList<string> AttemptedDirectories,
    IReadOnlyList<string> MissingFiles)
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(DirectoryPath) && MissingFiles.Count == 0;
}

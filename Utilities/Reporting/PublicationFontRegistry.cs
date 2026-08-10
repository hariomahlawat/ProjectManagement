using QuestPDF.Drawing;
using ProjectManagement.Services.Publications;

namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Registers publication fonts from PRISM's own web root. The export therefore does not
/// rely on Google Fonts, internet access or fonts installed on the IIS host. If the optional
/// publication font package has not yet been copied into PRISM, QuestPDF's Lato family is
/// used as a deterministic fallback.
/// </summary>
public static class PublicationFontRegistry
{
    public const string PrimaryFamilyName = "PRISM DM Sans";
    public const string DisplayFamilyName = "PRISM Alatsi";
    public const string FallbackFamilyName = "Lato";

    private static readonly object Gate = new();
    private static PublicationFontStatus? _status;
    private static string? _registeredWebRoot;

    private static readonly string[] DmSansFiles =
    [
        "DMSans-Regular.ttf",
        "DMSans-Medium.ttf",
        "DMSans-SemiBold.ttf",
        "DMSans-Bold.ttf",
        "DMSans-Italic.ttf",
        "DMSans-BoldItalic.ttf"
    ];

    public static PublicationFontStatus EnsureRegistered(string? webRootPath)
    {
        var normalizedRoot = string.IsNullOrWhiteSpace(webRootPath)
            ? string.Empty
            : Path.GetFullPath(webRootPath);

        if (normalizedRoot.Length == 0)
        {
            return new PublicationFontStatus(
                FallbackFamilyName,
                FallbackFamilyName,
                false,
                false,
                DmSansFiles);
        }

        lock (Gate)
        {
            if (_status is not null
                && string.Equals(_registeredWebRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return _status;
            }

            var dmSansDirectory = Path.Combine(normalizedRoot, "fonts", "publications", "dm-sans");
            var missingDmSans = DmSansFiles
                .Where(file => !File.Exists(Path.Combine(dmSansDirectory, file)))
                .ToArray();
            var dmSansAvailable = missingDmSans.Length == 0;

            if (dmSansAvailable)
            {
                try
                {
                    foreach (var file in DmSansFiles)
                    {
                        using var stream = File.OpenRead(Path.Combine(dmSansDirectory, file));
                        FontManager.RegisterFontWithCustomName(PrimaryFamilyName, stream);
                    }
                }
                catch (Exception)
                {
                    // A corrupt or incompatible local font package must never take the
                    // Publications workspace down. Fall back to QuestPDF's bundled Lato;
                    // the UI will continue to report DM Sans as unavailable.
                    dmSansAvailable = false;
                    missingDmSans = DmSansFiles;
                }
            }

            var alatsiPath = Path.Combine(normalizedRoot, "fonts", "publications", "alatsi", "Alatsi-Regular.ttf");
            var alatsiAvailable = File.Exists(alatsiPath);
            if (alatsiAvailable)
            {
                try
                {
                    using var stream = File.OpenRead(alatsiPath);
                    FontManager.RegisterFontWithCustomName(DisplayFamilyName, stream);
                }
                catch (Exception)
                {
                    alatsiAvailable = false;
                }
            }

            _registeredWebRoot = normalizedRoot;
            _status = new PublicationFontStatus(
                dmSansAvailable ? PrimaryFamilyName : FallbackFamilyName,
                alatsiAvailable ? DisplayFamilyName : dmSansAvailable ? PrimaryFamilyName : FallbackFamilyName,
                dmSansAvailable,
                alatsiAvailable,
                missingDmSans);
            return _status;
        }
    }
}

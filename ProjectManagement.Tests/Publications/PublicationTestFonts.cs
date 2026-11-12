using System;
using System.IO;
using System.Threading;
using ProjectManagement.Services.Publications;
using ProjectManagement.Utilities.Reporting;
using QuestPDF.Drawing;
using Xunit;

namespace ProjectManagement.Tests.Publications;

/// <summary>
/// The bundled DM Sans for publication tests, provided the way a deployment provides it.
///
/// Without it the print planner (Skia) measures with <c>SKTypeface.Default</c>, whatever the
/// machine's default font is, while QuestPDF renders with its own Lato, so measuring and
/// rendering use different fonts and whether a layout fits depends on the machine. That made two
/// brochure tests fail when GitHub's ubuntu-latest moved from 24.04 to 26.04. With DM Sans both
/// sides use the font production ships.
/// </summary>
internal static class PublicationTestFonts
{
    private static readonly Lazy<string> DmSansDirectory = new(FindDmSans, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly object RegistrationGate = new();
    private static bool _registered;

    /// <summary>DM Sans registered with QuestPDF under the family the builders ask for.</summary>
    public static PublicationFontStatus DmSansStatus { get; } = new(
        PublicationFontService.PrimaryFamilyName,
        PublicationFontService.PrimaryFamilyName,
        DmSansAvailable: true,
        AlatsiAvailable: false,
        MissingDmSansFiles: Array.Empty<string>(),
        SourceDescription: "Test: bundled DM Sans");

    /// <summary>
    /// Copies the bundled DM Sans to <c>{root}/Resources/Publications/Fonts/dm-sans</c>, where the
    /// print measurement service looks for it under its content root.
    /// </summary>
    public static void CopyDmSans(string root)
    {
        var destination = Path.Combine(root, "Resources", "Publications", "Fonts", "dm-sans");
        Directory.CreateDirectory(destination);
        foreach (var file in PublicationFontContract.RequiredDmSansFiles)
        {
            File.Copy(Path.Combine(DmSansDirectory.Value, file), Path.Combine(destination, file), overwrite: true);
        }
    }

    /// <summary>Registers the bundled DM Sans with QuestPDF once per test process.</summary>
    public static PublicationFontStatus RegisterDmSans()
    {
        lock (RegistrationGate)
        {
            if (!_registered)
            {
                foreach (var file in PublicationFontContract.RequiredDmSansFiles)
                {
                    using var stream = File.OpenRead(Path.Combine(DmSansDirectory.Value, file));
                    FontManager.RegisterFontWithCustomName(PublicationFontService.PrimaryFamilyName, stream);
                }

                _registered = true;
            }
        }

        return DmSansStatus;
    }

    private static string FindDmSans()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "ProjectManagement.csproj")))
        {
            repository = repository.Parent;
        }

        Assert.NotNull(repository);
        var source = PublicationFontContract.InspectDmSans(repository!.FullName);
        Assert.True(source.IsAvailable, "The bundled DM Sans fonts must be present for publication layout tests.");
        return source.DirectoryPath!;
    }
}

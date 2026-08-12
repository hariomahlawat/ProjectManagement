using System.Text;
using ProjectManagement.Services.Publications;
using UglyToad.PdfPig;

namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Raised when the physical PDF produced by QuestPDF no longer matches the compact-print plan
/// that was approved by publication preflight. No mismatched PDF should be issued to the user.
/// </summary>
public sealed class BrochurePdfCompositionException : InvalidOperationException
{
    public BrochurePdfCompositionException(
        string message,
        int expectedPageCount,
        int actualPageCount,
        int? expectedSheetNumber = null,
        string? projectName = null)
        : base(message)
    {
        ExpectedPageCount = expectedPageCount;
        ActualPageCount = actualPageCount;
        ExpectedSheetNumber = expectedSheetNumber;
        ProjectName = projectName;
    }

    public int ExpectedPageCount { get; }
    public int ActualPageCount { get; }
    public int? ExpectedSheetNumber { get; }
    public string? ProjectName { get; }
}

/// <summary>
/// Post-composition contract for Print / Compact. The planner decides page membership; after
/// QuestPDF has generated the bytes we re-open that exact PDF and verify both physical page count
/// and project-title membership. This catches renderer pagination drift before preview/download.
/// </summary>
internal static class BrochurePdfCompositionVerifier
{
    private const string ClosingHeading = "Visionary Horizons & Strategic Objectives";

    public static int CountPages(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        using var stream = new MemoryStream(pdfBytes, writable: false);
        using var document = PdfDocument.Open(stream);
        return document.NumberOfPages;
    }

    public static void Verify(
        byte[] pdfBytes,
        BrochurePublicationData data,
        BrochurePrintCompactPlan plan)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(plan);

        using var stream = new MemoryStream(pdfBytes, writable: false);
        using var document = PdfDocument.Open(stream);
        var pages = document.GetPages().ToArray();
        var actualPageCount = pages.Length;
        var expectedPageCount = plan.EstimatedTotalPageCount;

        if (actualPageCount != expectedPageCount)
        {
            throw new BrochurePdfCompositionException(
                $"Compact brochure composition drifted after rendering: preflight planned {expectedPageCount} physical page{Plural(expectedPageCount)}, but the generated PDF contains {actualPageCount}. The PDF was not issued.",
                expectedPageCount,
                actualPageCount);
        }

        var canonicalPages = pages
            .Select(page => Canonical(page.Text))
            .ToArray();

        for (var sheetIndex = 0; sheetIndex < plan.Pages.Count; sheetIndex++)
        {
            var sheet = plan.Pages[sheetIndex];
            var expectedPhysicalPageIndex = sheetIndex + 1; // physical page 1 is the institutional front page
            var expectedPhysicalPageNumber = expectedPhysicalPageIndex + 1;
            var expectedPageText = canonicalPages[expectedPhysicalPageIndex];

            foreach (var plannedProject in sheet.Projects)
            {
                if (plannedProject.ProjectIndex < 0 || plannedProject.ProjectIndex >= data.Projects.Count)
                {
                    throw new BrochurePdfCompositionException(
                        "Compact brochure planning returned an invalid project index. The PDF was not issued.",
                        expectedPageCount,
                        actualPageCount,
                        expectedPhysicalPageNumber);
                }

                var project = data.Projects[plannedProject.ProjectIndex];
                var title = Canonical(project.ProjectName);
                if (title.Length < 3 || expectedPageText.Contains(title, StringComparison.Ordinal))
                {
                    continue;
                }

                var actualPhysicalPage = FindPage(canonicalPages, title);
                var location = actualPhysicalPage.HasValue
                    ? $" It rendered on physical page {actualPhysicalPage.Value}."
                    : " Its title could not be located in the generated PDF.";
                throw new BrochurePdfCompositionException(
                    $"Compact brochure page membership changed after rendering for '{project.ProjectName}'. Preflight assigned the project to physical page {expectedPhysicalPageNumber}.{location} The PDF was not issued.",
                    expectedPageCount,
                    actualPageCount,
                    expectedPhysicalPageNumber,
                    project.ProjectName);
            }

            if (sheet.IncludesClosingMatter)
            {
                var closing = Canonical(ClosingHeading);
                if (!expectedPageText.Contains(closing, StringComparison.Ordinal))
                {
                    var actualClosingPage = FindPage(canonicalPages, closing);
                    var location = actualClosingPage.HasValue
                        ? $" It rendered on physical page {actualClosingPage.Value}."
                        : " The closing heading could not be located in the generated PDF.";
                    throw new BrochurePdfCompositionException(
                        $"Compact brochure closing matter changed page after rendering. Preflight assigned it to physical page {expectedPhysicalPageNumber}.{location} The PDF was not issued.",
                        expectedPageCount,
                        actualPageCount,
                        expectedPhysicalPageNumber);
                }
            }
        }
    }

    private static int? FindPage(IReadOnlyList<string> canonicalPages, string canonicalNeedle)
    {
        if (canonicalNeedle.Length < 3)
        {
            return null;
        }

        for (var index = 0; index < canonicalPages.Count; index++)
        {
            if (canonicalPages[index].Contains(canonicalNeedle, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return null;
    }

    private static string Canonical(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }
        return builder.ToString();
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}

using System.Text;
using UglyToad.PdfPig;

namespace ProjectManagement.Utilities.Reporting;

public sealed class CompendiumPdfCompositionException : InvalidOperationException
{
    public CompendiumPdfCompositionException(
        string message,
        int expectedPageCount,
        int actualPageCount,
        int? expectedPhysicalPage = null,
        string? projectName = null)
        : base(message)
    {
        ExpectedPageCount = expectedPageCount;
        ActualPageCount = actualPageCount;
        ExpectedPhysicalPage = expectedPhysicalPage;
        ProjectName = projectName;
    }

    public int ExpectedPageCount { get; }
    public int ActualPageCount { get; }
    public int? ExpectedPhysicalPage { get; }
    public string? ProjectName { get; }
}

public sealed record CompendiumPdfVerificationResult(bool IsVerified, int PageCount);

public interface ICompendiumPdfCompositionVerifier
{
    CompendiumPdfVerificationResult Verify(
        byte[] pdfBytes,
        CompendiumPdfReportContext context,
        CompendiumPagePlan plan);
}

/// <summary>
/// Reopens the exact PDF bytes produced by QuestPDF and verifies physical page count plus planned
/// project membership before preview/download is released to the browser.
/// </summary>
public sealed class CompendiumPdfCompositionVerifier : ICompendiumPdfCompositionVerifier
{
    public CompendiumPdfVerificationResult Verify(
        byte[] pdfBytes,
        CompendiumPdfReportContext context,
        CompendiumPagePlan plan)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);

        using var stream = new MemoryStream(pdfBytes, writable: false);
        using var document = PdfDocument.Open(stream);
        var pages = document.GetPages().ToArray();
        var actual = pages.Length;
        var expected = plan.ExpectedPageCount;
        if (actual != expected)
        {
            throw new CompendiumPdfCompositionException(
                $"Compendium composition drifted after rendering: the page planner expected {expected} physical page{Plural(expected)}, but the generated PDF contains {actual}. The PDF was not issued.",
                expected,
                actual);
        }

        var canonicalPages = pages.Select(page => Canonical(page.Text)).ToArray();
        VerifyTextOnPage(canonicalPages, context.Title, 1, expected, actual, "Compendium cover");

        foreach (var planned in plan.Pages)
        {
            if (planned.Kind == CompendiumPageKind.Index)
            {
                VerifyTextOnPage(
                    canonicalPages,
                    "Compendium Index",
                    planned.PhysicalPageNumber,
                    expected,
                    actual,
                    "Compendium index");

                foreach (var group in planned.IndexGroups)
                {
                    foreach (var project in group.Projects)
                    {
                        VerifyTextOnPage(
                            canonicalPages,
                            project.ProjectName,
                            planned.PhysicalPageNumber,
                            expected,
                            actual,
                            "Compendium index entry",
                            project.ProjectName);
                    }
                }
                continue;
            }

            if (planned.Kind is not (CompendiumPageKind.Project or CompendiumPageKind.ProjectContinuation)
                || planned.Project is null)
            {
                continue;
            }

            VerifyTextOnPage(
                canonicalPages,
                planned.Project.ProjectName,
                planned.PhysicalPageNumber,
                expected,
                actual,
                planned.Kind == CompendiumPageKind.Project
                    ? "Project section"
                    : "Project continuation",
                planned.Project.ProjectName);

            if (planned.Kind == CompendiumPageKind.ProjectContinuation)
            {
                VerifyTextOnPage(
                    canonicalPages,
                    "continued",
                    planned.PhysicalPageNumber,
                    expected,
                    actual,
                    "Project continuation",
                    planned.Project.ProjectName);
            }
        }

        var back = plan.Pages.Single(page => page.Kind == CompendiumPageKind.BackCover);
        VerifyTextOnPage(canonicalPages, context.Edition, back.PhysicalPageNumber, expected, actual, "Compendium back cover");

        var expectedProjects = context.Categories.SelectMany(category => category.Projects).Select(project => project.ProjectId).ToArray();
        var plannedProjects = plan.ProjectStartPages.Keys.ToArray();
        if (expectedProjects.Length != plannedProjects.Length
            || expectedProjects.Except(plannedProjects).Any()
            || plannedProjects.Except(expectedProjects).Any())
        {
            throw new CompendiumPdfCompositionException(
                "The Compendium page plan does not contain exactly the selected projects. The PDF was not issued.",
                expected,
                actual);
        }

        return new CompendiumPdfVerificationResult(true, actual);
    }

    private static void VerifyTextOnPage(
        IReadOnlyList<string> canonicalPages,
        string? text,
        int physicalPage,
        int expectedPageCount,
        int actualPageCount,
        string description,
        string? projectName = null)
    {
        var needle = Canonical(text);
        if (needle.Length < 3)
        {
            return;
        }

        var pageIndex = physicalPage - 1;
        if (pageIndex >= 0
            && pageIndex < canonicalPages.Count
            && canonicalPages[pageIndex].Contains(needle, StringComparison.Ordinal))
        {
            return;
        }

        var actualPage = FindPage(canonicalPages, needle);
        var location = actualPage.HasValue
            ? $" It rendered on physical page {actualPage.Value}."
            : " The expected text could not be located in the generated PDF.";
        throw new CompendiumPdfCompositionException(
            $"{description} changed physical page after rendering. The planner assigned it to page {physicalPage}.{location} The PDF was not issued.",
            expectedPageCount,
            actualPageCount,
            physicalPage,
            projectName);
    }

    private static int? FindPage(IReadOnlyList<string> pages, string needle)
    {
        for (var index = 0; index < pages.Count; index++)
        {
            if (pages[index].Contains(needle, StringComparison.Ordinal))
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

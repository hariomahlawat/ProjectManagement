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
        string? projectName = null,
        int? actualPhysicalPage = null)
        : base(message)
    {
        ExpectedPageCount = expectedPageCount;
        ActualPageCount = actualPageCount;
        ExpectedPhysicalPage = expectedPhysicalPage;
        ProjectName = projectName;
        ActualPhysicalPage = actualPhysicalPage;
    }

    public int ExpectedPageCount { get; }
    public int ActualPageCount { get; }
    public int? ExpectedPhysicalPage { get; }
    public string? ProjectName { get; }
    public int? ActualPhysicalPage { get; }
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
        var canonicalPages = pages.Select(page => Canonical(page.Text)).ToArray();
        if (actual != expected)
        {
            var drift = FindFirstObservableDrift(canonicalPages, plan);
            var diagnostic = drift is null
                ? string.Empty
                : $" First observable drift: {drift.Description} was planned on physical page {drift.ExpectedPage}, but rendered on page {drift.ActualPage}.";
            throw new CompendiumPdfCompositionException(
                $"Compendium composition drifted after rendering: the page planner expected {expected} physical page{Plural(expected)}, but the generated PDF contains {actual}.{diagnostic} The PDF was not issued.",
                expected,
                actual,
                drift?.ExpectedPage,
                drift?.ProjectName,
                drift?.ActualPage);
        }
        var frontTitle = ResolveEffectiveFrontTitle(context);
        VerifyTextOnPage(canonicalPages, frontTitle, 1, expected, actual, "Compendium cover");

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
        var backEdition = ResolveEffectiveBackEdition(context);
        VerifyTextOnPage(canonicalPages, backEdition, back.PhysicalPageNumber, expected, actual, "Compendium back cover");

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


    private static string? ResolveEffectiveFrontTitle(CompendiumPdfReportContext context)
    {
        var design = context.CoverDesign;
        if (design is null)
        {
            return context.Title;
        }

        if (!design.ShowFrontTitle)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(design.FrontTitle)
            ? context.Title
            : design.FrontTitle;
    }

    private static string? ResolveEffectiveBackEdition(CompendiumPdfReportContext context)
    {
        var design = context.CoverDesign;
        if (design is null)
        {
            return context.Edition;
        }

        if (!design.ShowBackEdition)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(design.BackEdition)
            ? context.Edition
            : design.BackEdition;
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

        var actualPage = FindNearestPage(canonicalPages, needle, physicalPage);
        var location = actualPage.HasValue
            ? $" It rendered on physical page {actualPage.Value}."
            : " The expected text could not be located in the generated PDF.";
        throw new CompendiumPdfCompositionException(
            $"{description} changed physical page after rendering. The planner assigned it to page {physicalPage}.{location} The PDF was not issued.",
            expectedPageCount,
            actualPageCount,
            physicalPage,
            projectName,
            actualPage);
    }

    private static DriftDiagnostic? FindFirstObservableDrift(
        IReadOnlyList<string> pages,
        CompendiumPagePlan plan)
    {
        // Index pages are checked first because an index overflow shifts every downstream dossier.
        // Use a concrete project entry rather than the repeated "Compendium Index" heading so the
        // diagnostic can localise the first page whose planned membership changed.
        foreach (var planned in plan.Pages.Where(page => page.Kind == CompendiumPageKind.Index))
        {
            foreach (var entry in planned.IndexGroups.SelectMany(group => group.Projects))
            {
                var needle = Canonical(entry.ProjectName);
                if (needle.Length < 3) continue;

                var expectedIndex = planned.PhysicalPageNumber - 1;
                if (expectedIndex >= 0
                    && expectedIndex < pages.Count
                    && pages[expectedIndex].Contains(needle, StringComparison.Ordinal))
                {
                    continue;
                }

                var actualPage = FindNearestPage(pages, needle, planned.PhysicalPageNumber);
                if (actualPage.HasValue)
                {
                    return new DriftDiagnostic(
                        planned.PhysicalPageNumber,
                        actualPage.Value,
                        $"Compendium index entry '{entry.ProjectName}'",
                        entry.ProjectName);
                }
            }
        }

        foreach (var planned in plan.Pages)
        {
            if (planned.Kind is not (CompendiumPageKind.Project or CompendiumPageKind.ProjectContinuation)
                || planned.Project is null)
            {
                continue;
            }

            var projectNeedle = Canonical(planned.Project.ProjectName);
            if (projectNeedle.Length < 3) continue;

            var expectedIndex = planned.PhysicalPageNumber - 1;
            var expectedMatches = expectedIndex >= 0
                                  && expectedIndex < pages.Count
                                  && pages[expectedIndex].Contains(projectNeedle, StringComparison.Ordinal);
            if (planned.Kind == CompendiumPageKind.ProjectContinuation)
            {
                expectedMatches = expectedMatches
                                  && pages[expectedIndex].Contains(Canonical("continued"), StringComparison.Ordinal);
            }
            if (expectedMatches) continue;

            int? actualPage;
            if (planned.Kind == CompendiumPageKind.ProjectContinuation)
            {
                actualPage = FindNearestPage(
                    pages,
                    planned.PhysicalPageNumber,
                    page => page.Contains(projectNeedle, StringComparison.Ordinal)
                            && page.Contains(Canonical("continued"), StringComparison.Ordinal));
            }
            else
            {
                actualPage = FindNearestPage(pages, projectNeedle, planned.PhysicalPageNumber);
            }

            if (actualPage.HasValue)
            {
                return new DriftDiagnostic(
                    planned.PhysicalPageNumber,
                    actualPage.Value,
                    planned.Kind == CompendiumPageKind.Project ? "project section" : "project continuation",
                    planned.Project.ProjectName);
            }
        }

        return null;
    }

    private static int? FindNearestPage(
        IReadOnlyList<string> pages,
        string needle,
        int expectedPhysicalPage)
        => FindNearestPage(
            pages,
            expectedPhysicalPage,
            page => page.Contains(needle, StringComparison.Ordinal));

    private static int? FindNearestPage(
        IReadOnlyList<string> pages,
        int expectedPhysicalPage,
        Func<string, bool> predicate)
    {
        if (pages.Count == 0) return null;
        var expectedIndex = Math.Clamp(expectedPhysicalPage - 1, 0, pages.Count - 1);
        for (var distance = 0; distance < pages.Count; distance++)
        {
            var forward = expectedIndex + distance;
            if (forward < pages.Count && predicate(pages[forward])) return forward + 1;

            if (distance == 0) continue;
            var backward = expectedIndex - distance;
            if (backward >= 0 && predicate(pages[backward])) return backward + 1;
        }
        return null;
    }

    private sealed record DriftDiagnostic(
        int ExpectedPage,
        int ActualPage,
        string Description,
        string? ProjectName);

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

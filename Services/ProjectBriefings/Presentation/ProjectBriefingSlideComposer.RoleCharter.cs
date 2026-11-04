using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private static IReadOnlyList<ProjectBriefingRoleCharterPage> PaginateRoleCharter(
        ProjectBriefingRoleCharterData data)
        => ProjectBriefingRoleCharterPaginator.Paginate(data);

    private static void RenderRoleCharter(
        SlideCanvas canvas,
        ProjectBriefingRoleCharterData data,
        ProjectBriefingRoleCharterPage page)
    {
        var title = page.IsContinuation
            ? $"{data.Title} — Continued"
            : data.Title;
        AddProjectSlideHeader(
            canvas,
            Truncate(title, 110),
            subtitle: null,
            variant: ProjectSlideHeaderVariant.Standard);

        var showRole = !page.IsContinuation
            && data.Layout != ProjectBriefingRoleCharterLayout.CharterOnly
            && page.RoleStatements.Count > 0;
        var charterTop = showRole ? 2.82 : 1.42;
        var charterMaximumHeight = showRole
            ? ProjectBriefingRoleCharterPaginator.CharterHeightBelowRole
            : ProjectBriefingRoleCharterPaginator.CharterHeightFullSlide;

        if (showRole)
        {
            RenderRolePanel(canvas, page.RoleStatements);
        }

        RenderCharterSection(
            canvas,
            page.CharterItems,
            data.Layout,
            charterTop,
            charterMaximumHeight,
            page.IsContinuation);
    }

    private static string ResolveRolePanelFill(ProjectBriefingThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        // HeaderAccent is a strong institutional rule colour, but the current theme
        // contract intentionally has no HeaderAccentSoft member. Reuse established
        // palette tokens so this slide remains compatible with both existing themes:
        // a raised surface on Graphite Dark and the restrained soft-maroon surface
        // already represented by CriticalSoft on Editorial Light.
        return theme.IsDark
            ? theme.SurfaceRaised
            : theme.CriticalSoft;
    }

    private static void RenderRolePanel(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingRoleCharterEntry> statements)
    {
        const double x = .72;
        const double y = 1.22;
        const double width = 11.90;
        const double height = 1.12;
        var fill = ResolveRolePanelFill(canvas.Theme);

        canvas.AddGroup(x, y, width, height, "Role panel", () =>
        {
            canvas.AddRoundedRect(x, y, width, height, fill, canvas.Theme.Border, .05, "Role panel background");
            canvas.AddRect(x, y, .075, height, canvas.Theme.HeaderAccent, null, 0, "Role panel accent");
            canvas.AddText(
                x + .24,
                y + .13,
                1.70,
                .22,
                "ROLE",
                11.5,
                canvas.Theme.HeaderAccent,
                true,
                "l",
                name: "Role heading");

            var shown = statements.Take(4).ToArray();
            var (fontSize, spaceAfter) = ResolveRoleTypography(shown, width - .48, height - .48);
            var paragraphs = shown
                .Select(statement => BuildRoleCharterParagraph(
                    statement,
                    canvas.Theme.HeaderAccent,
                    canvas.Theme.TextPrimary,
                    fontSize: fontSize,
                    bullet: null,
                    spaceAfter: spaceAfter))
                .ToArray();
            canvas.AddRichTextBox(
                x + .24,
                y + .38,
                width - .48,
                height - .48,
                paragraphs,
                "Authorised role statements",
                verticalAnchor: "t",
                allowAutoFit: true,
                leftInset: .02,
                rightInset: .04,
                topInset: .01,
                bottomInset: .01);
        });
    }

    // The largest role type (16 pt down to 11 pt) whose estimated lines fit the fixed panel, so two
    // long statements are not clipped; PowerPoint auto-fit remains the last safeguard.
    private static (double FontSize, double SpaceAfter) ResolveRoleTypography(
        IReadOnlyList<ProjectBriefingRoleCharterEntry> statements,
        double widthInches,
        double heightInches)
    {
        var available = heightInches * 72d;
        foreach (var fontSize in new[] { 16.0, 15.0, 14.4, 13.2, 12.0, 11.0 })
        {
            var spaceAfter = fontSize >= 15.0 ? 4.5 : 3.0;
            var charactersPerLine = Math.Max(1, (int)Math.Floor(widthInches * 72d / (fontSize * .52d)));
            var lines = statements.Sum(statement =>
                ProjectBriefingRoleCharterPaginator.EstimateLineCount(statement, charactersPerLine));
            var required = (lines * fontSize * 1.16d) + (Math.Max(0, statements.Count - 1) * spaceAfter);
            if (required <= available)
            {
                return (fontSize, spaceAfter);
            }
        }

        return (11.0, 2.0);
    }

    private static void RenderCharterSection(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingRoleCharterEntry> items,
        ProjectBriefingRoleCharterLayout layout,
        double top,
        double height,
        bool continuation)
    {
        canvas.AddText(
            .80,
            top - .30,
            2.25,
            .22,
            continuation ? "CHARTER — CONTINUED" : "CHARTER",
            11.5,
            canvas.Theme.HeaderAccent,
            true,
            "l",
            name: "Charter heading");
        canvas.AddLine(2.38, top - .18, 4.05, top - .18, canvas.Theme.Divider, .55);

        if (items.Count == 0)
        {
            AddEmptyMessage(canvas, "No charter items have been selected for this slide.");
            return;
        }

        // The paginator already split the charter so these items fit at a readable size; the
        // same measurement picks the largest type that fits them here.
        var typography = ProjectBriefingRoleCharterPaginator.ResolveTypography(items, layout, height);
        if (layout != ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter)
        {
            RenderCharterColumn(canvas, .72, top, ProjectBriefingRoleCharterPaginator.SingleColumnWidth, height, items, typography, "Charter items");
            return;
        }

        var leftCount = (int)Math.Ceiling(items.Count / 2d);
        var left = items.Take(leftCount).ToArray();
        var right = items.Skip(leftCount).ToArray();
        const double x = .72;
        const double gap = .24;
        const double columnWidth = ProjectBriefingRoleCharterPaginator.TwoColumnWidth;
        RenderCharterColumn(canvas, x, top, columnWidth, height, left, typography, "Charter items left column");
        if (right.Length > 0)
        {
            RenderCharterColumn(canvas, x + columnWidth + gap, top, columnWidth, height, right, typography, "Charter items right column");
        }
    }

    private static void RenderCharterColumn(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        double height,
        IReadOnlyList<ProjectBriefingRoleCharterEntry> items,
        ProjectBriefingCharterTypography typography,
        string name)
    {
        var fill = canvas.Theme.IsDark
            ? canvas.Theme.SurfaceRaised
            : canvas.Theme.Surface;
        canvas.AddGroup(x, y, width, height, name, () =>
        {
            canvas.AddRoundedRect(x, y, width, height, fill, canvas.Theme.Border, .046, $"{name} background");
            var paragraphs = items
                .Select(item => BuildRoleCharterParagraph(
                    item,
                    canvas.Theme.HeaderAccent,
                    canvas.Theme.TextPrimary,
                    typography.FontSize,
                    bullet: "✓ ",
                    spaceAfter: typography.SpaceAfterPoints,
                    lineSpacingPoints: typography.LineSpacingPoints))
                .ToArray();
            canvas.AddRichTextBox(
                x + .20,
                y + .16,
                width - .40,
                height - .30,
                paragraphs,
                name,
                verticalAnchor: "t",
                allowAutoFit: true,
                leftInset: .02,
                rightInset: .02,
                topInset: .02,
                bottomInset: .02);
        });
    }

    private static RichTextParagraph BuildRoleCharterParagraph(
        ProjectBriefingRoleCharterEntry entry,
        string accent,
        string text,
        double fontSize,
        string? bullet,
        double spaceAfter,
        double? lineSpacingPoints = null)
    {
        var runs = new List<RichTextRun>();
        if (!string.IsNullOrEmpty(bullet))
        {
            runs.Add(new RichTextRun(bullet, fontSize, accent, Bold: true));
        }
        if (!string.IsNullOrWhiteSpace(entry.LeadPhrase))
        {
            runs.Add(new RichTextRun(entry.LeadPhrase.Trim(), fontSize, accent, Bold: true));
        }
        if (!string.IsNullOrWhiteSpace(entry.Text))
        {
            if (runs.Count > 0)
            {
                runs.Add(new RichTextRun(" — ", fontSize, text));
            }
            runs.Add(new RichTextRun(entry.Text.Trim(), fontSize, text));
        }

        return new RichTextParagraph(
            runs,
            Align: "l",
            LeftMarginInches: string.IsNullOrEmpty(bullet) ? 0 : .18,
            FirstLineIndentInches: string.IsNullOrEmpty(bullet) ? 0 : -.18,
            SpaceAfterPoints: spaceAfter,
            LineSpacingPoints: lineSpacingPoints ?? fontSize * 1.16);
    }
}

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
        var charterMaximumHeight = showRole ? 3.78 : 5.18;

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

            var paragraphs = statements
                .Take(4)
                .Select(statement => BuildRoleCharterParagraph(
                    statement,
                    canvas.Theme.HeaderAccent,
                    canvas.Theme.TextPrimary,
                    fontSize: statements.Count > 2 ? 14.4 : 16.0,
                    bullet: null,
                    spaceAfter: statements.Count > 2 ? 3.0 : 4.5))
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

        // Preserve the user-selected composition on the single Role & Charter slide.
        // Charter content is never pushed to a continuation slide; typography is
        // reduced progressively when the configured list is longer.
        var twoColumns = layout == ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter;
        var resolvedHeight = height;
        if (!twoColumns)
        {
            var typography = ResolveCharterColumnTypography(items, 11.90, resolvedHeight);
            RenderCharterColumn(canvas, .72, top, 11.90, resolvedHeight, items, typography, "Charter items");
            return;
        }

        var leftCount = (int)Math.Ceiling(items.Count / 2d);
        var left = items.Take(leftCount).ToArray();
        var right = items.Skip(leftCount).ToArray();
        const double x = .72;
        const double gap = .24;
        const double columnWidth = 5.83;
        var leftLoad = MeasureCharterColumn(left, columnWidth);
        var rightLoad = MeasureCharterColumn(right, columnWidth);
        var sharedTypography = ResolveCharterTypography(
            Math.Max(left.Length, right.Length),
            Math.Max(leftLoad, rightLoad),
            resolvedHeight);
        RenderCharterColumn(canvas, x, top, columnWidth, resolvedHeight, left, sharedTypography, "Charter items left column");
        if (right.Length > 0)
        {
            RenderCharterColumn(canvas, x + columnWidth + gap, top, columnWidth, resolvedHeight, right, sharedTypography, "Charter items right column");
        }
    }

    private static void RenderCharterColumn(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        double height,
        IReadOnlyList<ProjectBriefingRoleCharterEntry> items,
        CharterTypography typography,
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


    private static CharterTypography ResolveCharterColumnTypography(
        IReadOnlyList<ProjectBriefingRoleCharterEntry> items,
        double width,
        double height)
        => ResolveCharterTypography(
            items.Count,
            MeasureCharterColumn(items, width),
            height);

    private static double MeasureCharterColumn(
        IReadOnlyList<ProjectBriefingRoleCharterEntry> items,
        double width)
    {
        var charactersPerLine = width >= 8d ? 104 : 48;
        return items.Sum(item =>
            ProjectBriefingRoleCharterPaginator.EstimateLineCount(item, charactersPerLine))
            + (items.Count * .45);
    }

    private static CharterTypography ResolveCharterTypography(
        int itemCount,
        double lineLoad,
        double availableHeightInches)
    {
        if (itemCount <= 0)
        {
            return new CharterTypography(13.2, 8.0, 15.0);
        }

        // The charter is an institutional statement and must remain on one slide.
        // Estimate the usable vertical budget in points and choose the largest
        // typography that fits. PowerPoint normal auto-fit remains enabled as a
        // final safeguard for unusual wrapping caused by user-authored text.
        var usableHeightPoints = Math.Max(72d, (availableHeightInches - .34d) * 72d);
        var estimatedTextLines = Math.Max(1d, lineLoad - (itemCount * .45d));

        for (var fontSize = 13.2d; fontSize >= 6.5d; fontSize -= .1d)
        {
            var density = fontSize switch
            {
                >= 12.4d => new CharterTypography(fontSize, 6.0d, fontSize * 1.14d),
                >= 11.2d => new CharterTypography(fontSize, 4.0d, fontSize * 1.12d),
                >= 10.0d => new CharterTypography(fontSize, 2.5d, fontSize * 1.10d),
                >= 8.5d => new CharterTypography(fontSize, 1.25d, fontSize * 1.08d),
                _ => new CharterTypography(fontSize, .5d, fontSize * 1.05d)
            };

            var requiredHeightPoints =
                (estimatedTextLines * density.LineSpacingPoints)
                + (Math.Max(0, itemCount - 1) * density.SpaceAfterPoints);

            if (requiredHeightPoints <= usableHeightPoints)
            {
                return density;
            }
        }

        // Extremely long custom text still remains on the same slide. The low
        // baseline and normal auto-fit prevent clipping while preserving every item.
        return new CharterTypography(6.5d, .25d, 6.8d);
    }

    private sealed record CharterTypography(
        double FontSize,
        double SpaceAfterPoints,
        double LineSpacingPoints);

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

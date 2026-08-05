using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private const int RoleCharterFirstPageCapacity = 10;
    private const int RoleCharterContinuationCapacity = 12;

    private static IReadOnlyList<RoleCharterPage> PaginateRoleCharter(ProjectBriefingRoleCharterData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var pages = new List<RoleCharterPage>();
        var firstItems = data.CharterItems.Take(RoleCharterFirstPageCapacity).ToArray();
        pages.Add(new RoleCharterPage(
            IsContinuation: false,
            PageNumber: 1,
            RoleStatements: data.RoleStatements,
            CharterItems: firstItems));

        var remaining = data.CharterItems.Skip(RoleCharterFirstPageCapacity).ToArray();
        for (var offset = 0; offset < remaining.Length; offset += RoleCharterContinuationCapacity)
        {
            pages.Add(new RoleCharterPage(
                IsContinuation: true,
                PageNumber: pages.Count + 1,
                RoleStatements: Array.Empty<ProjectBriefingRoleCharterEntry>(),
                CharterItems: remaining.Skip(offset).Take(RoleCharterContinuationCapacity).ToArray()));
        }

        return pages;
    }

    private static void RenderRoleCharter(
        SlideCanvas canvas,
        ProjectBriefingRoleCharterData data,
        RoleCharterPage page)
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
        var charterTop = showRole ? 2.82 : 1.34;
        var charterHeight = showRole ? 3.92 : 5.38;

        if (showRole)
        {
            RenderRolePanel(canvas, page.RoleStatements);
        }

        RenderCharterSection(
            canvas,
            page.CharterItems,
            data.Layout,
            charterTop,
            charterHeight,
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
        const double height = 1.32;
        var fill = ResolveRolePanelFill(canvas.Theme);

        canvas.AddGroup(x, y, width, height, "Role panel", () =>
        {
            canvas.AddRoundedRect(x, y, width, height, fill, canvas.Theme.Border, .06, "Role panel background");
            canvas.AddRect(x, y, .075, height, canvas.Theme.HeaderAccent, null, 0, "Role panel accent");
            canvas.AddText(
                x + .24,
                y + .16,
                1.00,
                .28,
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
                    fontSize: statements.Count > 2 ? 14.8 : 16.3,
                    bullet: null,
                    spaceAfter: statements.Count > 2 ? 4.0 : 6.0))
                .ToArray();
            canvas.AddRichTextBox(
                x + 1.18,
                y + .14,
                width - 1.42,
                height - .25,
                paragraphs,
                "Authorised role statements",
                verticalAnchor: "ctr",
                allowAutoFit: false,
                leftInset: .02,
                rightInset: .04,
                topInset: .02,
                bottomInset: .02);
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
            .76,
            top - .34,
            2.0,
            .26,
            continuation ? "CHARTER — CONTINUED" : "CHARTER",
            11.5,
            canvas.Theme.HeaderAccent,
            true,
            "l",
            name: "Charter heading");

        if (items.Count == 0)
        {
            AddEmptyMessage(canvas, "No charter items have been selected for this slide.");
            return;
        }

        var twoColumns = layout == ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter
            || continuation;
        if (!twoColumns)
        {
            RenderCharterColumn(canvas, .72, top, 11.90, height, items, "Charter items");
            return;
        }

        var leftCount = (int)Math.Ceiling(items.Count / 2d);
        var left = items.Take(leftCount).ToArray();
        var right = items.Skip(leftCount).ToArray();
        const double x = .72;
        const double gap = .24;
        const double columnWidth = 5.83;
        RenderCharterColumn(canvas, x, top, columnWidth, height, left, "Charter items left column");
        if (right.Length > 0)
        {
            RenderCharterColumn(canvas, x + columnWidth + gap, top, columnWidth, height, right, "Charter items right column");
        }
    }

    private static void RenderCharterColumn(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        double height,
        IReadOnlyList<ProjectBriefingRoleCharterEntry> items,
        string name)
    {
        var fill = canvas.Theme.IsDark
            ? canvas.Theme.SurfaceRaised
            : canvas.Theme.Surface;
        canvas.AddGroup(x, y, width, height, name, () =>
        {
            canvas.AddRoundedRect(x, y, width, height, fill, canvas.Theme.Border, .055, $"{name} background");
            var compact = items.Count >= 6;
            var fontSize = compact ? 12.8 : 13.8;
            var paragraphs = items
                .Select(item => BuildRoleCharterParagraph(
                    item,
                    canvas.Theme.HeaderAccent,
                    canvas.Theme.TextPrimary,
                    fontSize,
                    bullet: "✓ ",
                    spaceAfter: compact ? 7.0 : 10.0))
                .ToArray();
            canvas.AddRichTextBox(
                x + .20,
                y + .16,
                width - .40,
                height - .30,
                paragraphs,
                name,
                verticalAnchor: "t",
                allowAutoFit: false,
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
        double spaceAfter)
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
            LineSpacingPoints: fontSize * 1.16);
    }

    private sealed record RoleCharterPage(
        bool IsContinuation,
        int PageNumber,
        IReadOnlyList<ProjectBriefingRoleCharterEntry> RoleStatements,
        IReadOnlyList<ProjectBriefingRoleCharterEntry> CharterItems);
}

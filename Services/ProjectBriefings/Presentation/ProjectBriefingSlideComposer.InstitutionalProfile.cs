using System.Globalization;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private static void RenderInstitutionalProfile(
        SlideCanvas canvas,
        ProjectBriefingInstitutionalProfileData profile)
    {
        var theme = canvas.Theme;
        canvas.AddRect(0, 0, SlideWidth, SlideHeight, theme.Canvas, name: "SDD profile canvas");
        canvas.AddRect(0, 0, SlideWidth, .10, theme.HeaderAccent, name: "SDD profile top accent");
        canvas.AddBrandingImages(HeaderVariant.Standard);

        // This slide intentionally follows the authorised SDD heritage composition.
        // It is visually distinct from analytical summaries but uses the deck's theme,
        // typography, branding and footer so it remains part of the same presentation.
        var titleX = canvas.ShowBranding ? 1.28 : .62;
        var titleWidth = canvas.ShowBranding ? 10.78 : 12.10;
        canvas.AddRoundedRect(
            titleX,
            .22,
            titleWidth,
            .62,
            theme.HeaderAccent,
            theme.HeaderAccent,
            .04,
            "SDD profile title band");
        canvas.AddText(
            titleX + .24,
            .30,
            titleWidth - .48,
            .43,
            Truncate(profile.Title, 110),
            23.5,
            theme.TextOnAccent,
            true,
            "ctr",
            name: "SDD profile title");

        var hasHistory = profile.HistoryMilestones.Count > 0;
        var hasCitation = profile.UnitCitationCount.HasValue
            && !string.IsNullOrWhiteSpace(profile.UnitCitationLabel);
        var modulesTop = hasHistory ? 2.48 : 1.12;
        var modulesHeight = hasCitation
            ? 3.34
            : 3.70;

        if (hasHistory)
        {
            RenderInstitutionalHistory(canvas, profile.HistoryMilestones);
        }

        RenderInstitutionalModules(canvas, profile.Modules, modulesTop, modulesHeight);

        if (hasCitation)
        {
            RenderInstitutionalCitation(
                canvas,
                profile.UnitCitationLabel!,
                profile.UnitCitationCount!.Value);
        }

        var dataAsOn = profile.DataAsOnUtc.ToUniversalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        canvas.AddText(
            .76,
            hasCitation ? 6.60 : 6.67,
            11.82,
            .17,
            $"Data as on {dataAsOn} · Source: PRISM ERP",
            7.7,
            theme.TextMuted,
            false,
            "ctr",
            name: "SDD profile data source");
    }

    /// <summary>
    /// Renders the open alternating heritage timeline as one top-level PowerPoint
    /// group. Each milestone is one editable rich-text box containing both year and
    /// authorised description. No native table or visible grid is used.
    /// </summary>
    private static void RenderInstitutionalHistory(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingInstitutionalHistoryMilestone> history)
    {
        var items = history
            .OrderBy(item => item.Year)
            .ThenBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        if (items.Length == 0) return;

        const double left = .82;
        const double right = 12.52;
        const double groupTop = .94;
        const double groupHeight = 1.36;
        const double lineY = 1.61;
        var span = right - left;
        var compact = items.Length >= 7;

        canvas.AddGroup(
            left,
            groupTop,
            span,
            groupHeight,
            "SDD institutional history timeline",
            () =>
            {
                canvas.AddLine(left, lineY, right, lineY, canvas.Theme.Divider, 1.05);

                for (var index = 0; index < items.Length; index++)
                {
                    var item = items[index];
                    var above = index % 2 == 0;
                    var markerX = items.Length == 1
                        ? (left + right) / 2d
                        : left + ((span * index) / (items.Length - 1));
                    var cellWidth = Math.Min(
                        compact ? 1.44 : 1.72,
                        Math.Max(1.18, (span / items.Length) - .08));
                    var textX = Math.Clamp(
                        markerX - (cellWidth / 2d),
                        left,
                        right - cellWidth);
                    var textY = above ? .99 : 1.72;
                    var description = Truncate(item.Text, compact ? 34 : 46);

                    canvas.AddRichTextBox(
                        textX,
                        textY,
                        cellWidth,
                        .52,
                        new[]
                        {
                            new RichTextParagraph(
                                new[]
                                {
                                    new RichTextRun(
                                        item.Year.ToString(CultureInfo.InvariantCulture),
                                        compact ? 9.4 : 10.6,
                                        canvas.Theme.HeaderAccent,
                                        Bold: true)
                                },
                                Align: "ctr",
                                SpaceAfterPoints: 2.0,
                                LineSpacingPoints: compact ? 10.2 : 11.4),
                            new RichTextParagraph(
                                new[]
                                {
                                    new RichTextRun(
                                        description,
                                        compact ? 7.3 : 8.2,
                                        canvas.Theme.TextPrimary,
                                        Bold: true)
                                },
                                Align: "ctr",
                                LineSpacingPoints: compact ? 8.2 : 9.0)
                        },
                        $"SDD milestone {item.Year}",
                        verticalAnchor: above ? "b" : "t",
                        allowAutoFit: true,
                        leftInset: .02,
                        rightInset: .02,
                        topInset: .01,
                        bottomInset: .01);

                    canvas.AddText(
                        markerX - .10,
                        lineY - .12,
                        .20,
                        .24,
                        "●",
                        10.5,
                        canvas.Theme.Warning,
                        true,
                        "ctr",
                        name: $"SDD milestone marker {item.Year}");
                }
            });
    }

    /// <summary>
    /// Renders every selected output module in the original five-column composition.
    /// Each module is one top-level PowerPoint group and contains ordinary shapes and
    /// rich text only. No native PowerPoint table is used.
    /// </summary>
    private static void RenderInstitutionalModules(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingInstitutionalModuleData> modules,
        double top,
        double height)
    {
        if (modules.Count == 0) return;

        var visible = modules.Take(5).ToArray();
        const double availableLeft = .60;
        const double availableRight = 12.73;
        const double gap = .12;
        const double maximumCardWidth = 3.02;
        var availableWidth = availableRight - availableLeft;
        var calculatedWidth = (availableWidth - ((visible.Length - 1) * gap)) / visible.Length;
        var cardWidth = Math.Min(maximumCardWidth, calculatedWidth);
        var usedWidth = (visible.Length * cardWidth) + ((visible.Length - 1) * gap);
        var left = availableLeft + ((availableWidth - usedWidth) / 2d);

        for (var index = 0; index < visible.Length; index++)
        {
            RenderInstitutionalModuleCard(
                canvas,
                visible[index],
                left + (index * (cardWidth + gap)),
                top,
                cardWidth,
                height);
        }
    }

    private static void RenderInstitutionalModuleCard(
        SlideCanvas canvas,
        ProjectBriefingInstitutionalModuleData module,
        double x,
        double y,
        double width,
        double height)
    {
        var accent = InstitutionalModuleAccent(canvas.Theme, module.Module);
        var soft = InstitutionalModuleSoftFill(canvas.Theme, module.Module);
        var isPartnership = module.Module == ProjectBriefingInstitutionalProfileModule.Partnerships;
        var rows = module.Rows.ToArray();
        var hasHeadline = !string.IsNullOrWhiteSpace(module.Headline);
        var hasHighlight = !string.IsNullOrWhiteSpace(module.Highlight);
        var compact = width < 2.45;
        var titleFont = compact ? 7.8 : 8.7;
        var bodyFont = compact ? 7.25 : 8.0;
        var valueFont = compact ? 7.45 : 8.2;
        var headlineFont = compact ? 18.0 : 20.0;
        var headerHeight = .52;
        var highlightHeight = hasHighlight ? .32 : 0d;

        canvas.AddGroup(
            x,
            y,
            width,
            height,
            $"{module.Title} institutional module",
            () =>
            {
                canvas.AddRoundedRect(
                    x,
                    y,
                    width,
                    height,
                    canvas.Theme.Surface,
                    canvas.Theme.Border,
                    .045,
                    $"{module.Title} module background");
                canvas.AddRect(
                    x + .035,
                    y + .02,
                    width - .07,
                    .055,
                    accent,
                    name: $"{module.Title} module accent");
                canvas.AddRect(
                    x + .04,
                    y + .075,
                    width - .08,
                    headerHeight,
                    soft,
                    name: $"{module.Title} module header fill");
                canvas.AddText(
                    x + .10,
                    y + .14,
                    width - .20,
                    headerHeight - .10,
                    Truncate(module.Title, compact ? 48 : 58),
                    titleFont,
                    accent,
                    true,
                    "ctr",
                    name: $"{module.Title} module title");

                var contentTop = y + headerHeight + .16;
                var contentBottom = y + height - .12 - highlightHeight;

                if (isPartnership)
                {
                    var partnershipParagraphs = rows
                        .Take(6)
                        .Select(row => new RichTextParagraph(
                            new[]
                            {
                                new RichTextRun(
                                    $"• {Truncate(row.Label, compact ? 28 : 36)}",
                                    compact ? 7.5 : 8.2,
                                    canvas.Theme.TextSecondary)
                            },
                            SpaceAfterPoints: compact ? 8.0 : 10.0,
                            LineSpacingPoints: compact ? 8.6 : 9.4))
                        .ToArray();

                    if (partnershipParagraphs.Length > 0)
                    {
                        canvas.AddRichTextBox(
                            x + .14,
                            contentTop + .10,
                            width - .28,
                            Math.Max(.58, contentBottom - contentTop - .14),
                            partnershipParagraphs,
                            $"{module.Title} module content",
                            verticalAnchor: "ctr",
                            allowAutoFit: true,
                            leftInset: .02,
                            rightInset: .02,
                            topInset: .02,
                            bottomInset: .02);
                    }

                    return;
                }

                if (hasHeadline)
                {
                    canvas.AddText(
                        x + .12,
                        contentTop,
                        width - .24,
                        .48,
                        module.Headline!,
                        headlineFont,
                        canvas.Theme.TextPrimary,
                        true,
                        "ctr",
                        name: $"{module.Title} module headline");
                    contentTop += .56;
                }

                var availableBodyHeight = Math.Max(.42, contentBottom - contentTop);
                var maximumRows = Math.Max(1, (int)Math.Floor(availableBodyHeight / (compact ? .28 : .34)));
                var visibleRows = rows.Take(maximumRows).ToArray();
                var bodyParagraphs = new List<RichTextParagraph>(visibleRows.Length);
                foreach (var row in visibleRows)
                {
                    bodyParagraphs.Add(new RichTextParagraph(
                        new[]
                        {
                            new RichTextRun(
                                Truncate(row.Label, compact ? 25 : 33),
                                bodyFont,
                                canvas.Theme.TextSecondary),
                            new RichTextRun(
                                row.Value,
                                valueFont,
                                canvas.Theme.TextPrimary,
                                Bold: true)
                        },
                        TabStopInches: Math.Max(.58, width - .46),
                        TabAfterFirstRun: true,
                        SpaceAfterPoints: compact ? 7.0 : 8.0,
                        LineSpacingPoints: compact ? 8.4 : 9.2));
                }

                if (bodyParagraphs.Count > 0)
                {
                    canvas.AddRichTextBox(
                        x + .14,
                        contentTop + .02,
                        width - .28,
                        availableBodyHeight - .04,
                        bodyParagraphs,
                        $"{module.Title} module details",
                        verticalAnchor: "ctr",
                        allowAutoFit: true,
                        leftInset: .01,
                        rightInset: .01,
                        topInset: .01,
                        bottomInset: .01);
                }

                if (hasHighlight)
                {
                    var highlightFill = canvas.Theme.IsDark ? accent : soft;
                    var highlightText = canvas.Theme.IsDark ? canvas.Theme.TextOnAccent : accent;
                    canvas.AddRoundedRect(
                        x + .10,
                        y + height - .38,
                        width - .20,
                        .27,
                        highlightFill,
                        null,
                        .025,
                        $"{module.Title} module highlight fill");
                    canvas.AddText(
                        x + .15,
                        y + height - .355,
                        width - .30,
                        .21,
                        Truncate(module.Highlight, compact ? 54 : 72),
                        compact ? 6.4 : 7.0,
                        highlightText,
                        true,
                        "l",
                        name: $"{module.Title} module highlight");
                }
            });
    }

    private static void RenderInstitutionalCitation(
        SlideCanvas canvas,
        string label,
        int count)
    {
        canvas.AddGroup(
            4.35,
            6.08,
            4.63,
            .40,
            "Institutional recognition strip",
            () =>
            {
                canvas.AddRoundedRect(
                    4.35,
                    6.08,
                    4.63,
                    .40,
                    canvas.Theme.SurfaceRaised,
                    canvas.Theme.HeaderAccent,
                    .035,
                    "Institutional recognition background");
                canvas.AddText(
                    4.56,
                    6.14,
                    4.21,
                    .25,
                    $"{Truncate(label, 58)} — {count:00}",
                    11.0,
                    canvas.Theme.TextPrimary,
                    true,
                    "ctr",
                    name: "Institutional recognition text");
            });
    }

    private static string InstitutionalModuleAccent(
        ProjectBriefingThemeDefinition theme,
        ProjectBriefingInstitutionalProfileModule module)
        => module switch
        {
            ProjectBriefingInstitutionalProfileModule.ProjectsDeveloped => theme.SecondaryAccent,
            ProjectBriefingInstitutionalProfileModule.Proliferation => theme.Accent,
            ProjectBriefingInstitutionalProfileModule.TrainingSupport => theme.HeaderAccent,
            ProjectBriefingInstitutionalProfileModule.IntellectualProperty => theme.Warning,
            ProjectBriefingInstitutionalProfileModule.Partnerships => theme.Positive,
            _ => theme.Accent
        };

    private static string InstitutionalModuleSoftFill(
        ProjectBriefingThemeDefinition theme,
        ProjectBriefingInstitutionalProfileModule module)
        => module switch
        {
            ProjectBriefingInstitutionalProfileModule.ProjectsDeveloped => theme.SecondaryAccentSoft,
            ProjectBriefingInstitutionalProfileModule.Proliferation => theme.AccentSoft,
            ProjectBriefingInstitutionalProfileModule.TrainingSupport => theme.CriticalSoft,
            ProjectBriefingInstitutionalProfileModule.IntellectualProperty => theme.WarningSoft,
            ProjectBriefingInstitutionalProfileModule.Partnerships => theme.PositiveSoft,
            _ => theme.SurfaceMuted
        };
}

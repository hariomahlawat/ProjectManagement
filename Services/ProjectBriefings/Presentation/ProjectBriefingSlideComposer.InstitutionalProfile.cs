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
        AddProjectSlideHeader(
            canvas,
            Truncate(profile.Title, 110),
            subtitle: null,
            variant: ProjectSlideHeaderVariant.Standard);

        var hasHistory = profile.HistoryMilestones.Count > 0;
        var hasFooterStrip = profile.IncludeFooterStrip
            && (!string.IsNullOrWhiteSpace(profile.FooterStripText)
                || !string.IsNullOrWhiteSpace(profile.FooterStripEmphasisValue));
        var modulesTop = hasHistory ? 2.43 : 1.14;
        var modulesHeight = hasFooterStrip ? 3.34 : 3.72;

        if (hasHistory)
        {
            RenderInstitutionalHistory(canvas, profile.HistoryMilestones);
        }

        RenderInstitutionalModules(canvas, profile.Modules, modulesTop, modulesHeight);

        if (hasFooterStrip)
        {
            RenderInstitutionalFooterStrip(canvas, profile);
        }
    }

    /// <summary>
    /// Renders the authorised open alternating timeline as one top-level group.
    /// Each milestone remains one editable year-and-description text object; no table
    /// or visible grid is used.
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
        const double groupTop = 1.08;
        const double groupHeight = 1.30;
        const double lineY = 1.68;
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
                canvas.AddLine(left, lineY, right, lineY, canvas.Theme.Divider, 1.65);

                for (var index = 0; index < items.Length; index++)
                {
                    var item = items[index];
                    var above = index % 2 == 0;
                    var markerX = items.Length == 1
                        ? (left + right) / 2d
                        : left + ((span * index) / (items.Length - 1));
                    var cellWidth = Math.Min(
                        compact ? 1.48 : 1.78,
                        Math.Max(1.24, (span / items.Length) - .05));
                    var textX = Math.Clamp(markerX - (cellWidth / 2d), left, right - cellWidth);
                    var textY = above ? 1.00 : 1.78;
                    var description = Truncate(item.Text, compact ? 36 : 50);

                    canvas.AddRichTextBox(
                        textX,
                        textY,
                        cellWidth,
                        .57,
                        new[]
                        {
                            new RichTextParagraph(
                                new[]
                                {
                                    new RichTextRun(
                                        item.Year.ToString(CultureInfo.InvariantCulture),
                                        compact ? 11.2 : 12.8,
                                        canvas.Theme.HeaderAccent,
                                        Bold: true)
                                },
                                Align: "ctr",
                                SpaceAfterPoints: 2.2,
                                LineSpacingPoints: compact ? 11.8 : 13.2),
                            new RichTextParagraph(
                                new[]
                                {
                                    new RichTextRun(
                                        description,
                                        compact ? 9.2 : 10.4,
                                        canvas.Theme.TextPrimary,
                                        Bold: true)
                                },
                                Align: "ctr",
                                LineSpacingPoints: compact ? 10.0 : 11.3)
                        },
                        $"SDD milestone {item.Year}",
                        verticalAnchor: above ? "b" : "t",
                        allowAutoFit: true,
                        leftInset: .02,
                        rightInset: .02,
                        topInset: .01,
                        bottomInset: .01);

                    canvas.AddText(
                        markerX - .115,
                        lineY - .135,
                        .23,
                        .27,
                        "●",
                        12.8,
                        canvas.Theme.Warning,
                        true,
                        "ctr",
                        name: $"SDD milestone marker {item.Year}");
                }
            });
    }

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

    /// <summary>
    /// Each institutional module is one top-level group. Detail labels and values are
    /// rendered in two aligned multi-paragraph boxes, avoiding fragile per-row shapes
    /// and PowerPoint tab-stop wrapping while keeping the module easy to edit.
    /// </summary>
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
        var titleFont = compact ? 10.0 : 10.8;
        var bodyFont = compact ? 9.6 : 10.2;
        var valueFont = compact ? 9.9 : 10.5;
        var headlineFont = compact ? 22.0 : 23.5;
        const double headerHeight = .60;
        var highlightHeight = hasHighlight ? .54 : 0d;
        var displayTitle = InstitutionalModuleDisplayTitle(module.Module, module.Title);

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
                    InstitutionalModuleHeaderFill(canvas.Theme, module.Module),
                    name: $"{module.Title} module header fill");
                canvas.AddText(
                    x + .10,
                    y + .12,
                    width - .20,
                    headerHeight - .05,
                    displayTitle,
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
                                    $"• {Truncate(row.Label, compact ? 31 : 40)}",
                                    compact ? 10.0 : 10.7,
                                    canvas.Theme.TextSecondary)
                            },
                            SpaceAfterPoints: compact ? 12.0 : 14.0,
                            LineSpacingPoints: compact ? 11.0 : 11.8))
                        .ToArray();

                    if (partnershipParagraphs.Length > 0)
                    {
                        canvas.AddRichTextBox(
                            x + .16,
                            contentTop + .10,
                            width - .32,
                            Math.Max(.58, contentBottom - contentTop - .22),
                            partnershipParagraphs,
                            $"{module.Title} module content",
                            verticalAnchor: "t",
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
                        .50,
                        module.Headline!,
                        headlineFont,
                        canvas.Theme.TextPrimary,
                        true,
                        "ctr",
                        name: $"{module.Title} module headline");
                    contentTop += .54;
                }

                var availableBodyHeight = Math.Max(.42, contentBottom - contentTop);
                var rowPitch = hasHighlight
                    ? compact ? .265 : .29
                    : compact ? .31 : .35;
                var maximumRows = Math.Max(1, (int)Math.Floor(availableBodyHeight / rowPitch));
                var visibleRows = rows.Take(maximumRows).ToArray();
                var isSparse = visibleRows.Length <= 3;
                var labels = new List<RichTextParagraph>(visibleRows.Length);
                var values = new List<RichTextParagraph>(visibleRows.Length);
                foreach (var row in visibleRows)
                {
                    labels.Add(new RichTextParagraph(
                        new[]
                        {
                            new RichTextRun(
                                Truncate(row.Label, compact ? 27 : 36),
                                bodyFont,
                                canvas.Theme.TextSecondary)
                        },
                        Align: "l",
                        SpaceAfterPoints: isSparse ? 16.0 : compact ? 6.0 : 7.0,
                        LineSpacingPoints: compact ? 10.6 : 11.2));
                    values.Add(new RichTextParagraph(
                        new[]
                        {
                            new RichTextRun(
                                row.Value,
                                valueFont,
                                canvas.Theme.TextPrimary,
                                Bold: true)
                        },
                        Align: "r",
                        SpaceAfterPoints: isSparse ? 16.0 : compact ? 6.0 : 7.0,
                        LineSpacingPoints: compact ? 10.6 : 11.2));
                }

                if (labels.Count > 0)
                {
                    var bodyTopOffset = isSparse ? .08 : .02;
                    var bodyAnchor = isSparse ? "t" : "ctr";
                    canvas.AddRichTextBox(
                        x + .14,
                        contentTop + bodyTopOffset,
                        width * .68,
                        availableBodyHeight - bodyTopOffset - .02,
                        labels,
                        $"{module.Title} module labels",
                        verticalAnchor: bodyAnchor,
                        allowAutoFit: true,
                        leftInset: .01,
                        rightInset: .01,
                        topInset: .01,
                        bottomInset: .01);
                    canvas.AddRichTextBox(
                        x + (width * .73),
                        contentTop + bodyTopOffset,
                        (width * .27) - .14,
                        availableBodyHeight - bodyTopOffset - .02,
                        values,
                        $"{module.Title} module values",
                        verticalAnchor: bodyAnchor,
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
                        y + height - .60,
                        width - .20,
                        .50,
                        highlightFill,
                        null,
                        .025,
                        $"{module.Title} module highlight fill");
                    canvas.AddText(
                        x + .15,
                        y + height - .555,
                        width - .30,
                        .43,
                        FormatInstitutionalHighlight(module.Highlight, compact ? 76 : 92),
                        compact ? 8.6 : 9.2,
                        highlightText,
                        true,
                        "l",
                        name: $"{module.Title} module highlight");
                }
            });
    }

    private static string FormatInstitutionalHighlight(string? value, int maximum)
    {
        var text = Truncate(value, maximum);
        var splitAt = text.IndexOf(" trained ", StringComparison.OrdinalIgnoreCase);
        if (splitAt > 0)
        {
            return text[..splitAt].TrimEnd()
                + "\n"
                + text[(splitAt + 1)..].TrimStart();
        }

        return text;
    }

    private static string InstitutionalModuleDisplayTitle(
        ProjectBriefingInstitutionalProfileModule module,
        string fallback)
        => module switch
        {
            ProjectBriefingInstitutionalProfileModule.ProjectsDeveloped => "SIMULATORS / PROJECTS\nDEVELOPED",
            ProjectBriefingInstitutionalProfileModule.Proliferation => "PROLIFERATED",
            ProjectBriefingInstitutionalProfileModule.TrainingSupport => "ASSISTANCE TO\nFIELD FORMATIONS",
            ProjectBriefingInstitutionalProfileModule.IntellectualProperty => "INTELLECTUAL PROPERTY",
            ProjectBriefingInstitutionalProfileModule.Partnerships => "MILITARY–ACADEMIA–\nINDUSTRY SYNERGY",
            _ => Truncate(fallback, 58).ToUpperInvariant()
        };

    private static void RenderInstitutionalFooterStrip(
        SlideCanvas canvas,
        ProjectBriefingInstitutionalProfileData profile)
    {
        const double y = 6.14;
        const double height = .35;
        var theme = canvas.Theme;

        var fill = profile.FooterStripStyle switch
        {
            ProjectBriefingInstitutionalFooterStyle.SolidMaroon => theme.HeaderAccent,
            ProjectBriefingInstitutionalFooterStyle.SubtleNeutral => theme.SurfaceMuted,
            _ => theme.SurfaceRaised
        };
        var border = profile.FooterStripStyle switch
        {
            ProjectBriefingInstitutionalFooterStyle.SolidMaroon => theme.HeaderAccent,
            ProjectBriefingInstitutionalFooterStyle.SubtleNeutral => theme.Divider,
            _ => theme.Divider
        };
        var textColor = profile.FooterStripStyle == ProjectBriefingInstitutionalFooterStyle.SolidMaroon
            ? theme.TextOnAccent
            : theme.TextPrimary;
        var valueColor = profile.FooterStripStyle == ProjectBriefingInstitutionalFooterStyle.SolidMaroon
            ? theme.TextOnAccent
            : theme.HeaderAccent;
        var label = Truncate(profile.FooterStripText, 150);
        var value = Truncate(profile.FooterStripEmphasisValue, 36);
        var combinedLength = string.IsNullOrWhiteSpace(value)
            ? label.Length
            : string.IsNullOrWhiteSpace(label)
                ? value.Length
                : label.Length + value.Length + 3;
        var minimumWidth = profile.FooterStripAlignment
            == ProjectBriefingInstitutionalFooterAlignment.LabelLeftValueRight
            ? 8.15
            : 7.00;
        var width = Math.Clamp(3.20 + (combinedLength * .085), minimumWidth, 12.10);
        var x = (SlideWidth - width) / 2d;

        canvas.AddGroup(
            x,
            y,
            width,
            height,
            "SDD profile footer strip",
            () =>
            {
                canvas.AddRoundedRect(
                    x,
                    y,
                    width,
                    height,
                    fill,
                    border,
                    .024,
                    "SDD profile footer-strip background");

                if (profile.FooterStripAlignment == ProjectBriefingInstitutionalFooterAlignment.LabelLeftValueRight
                    && !string.IsNullOrWhiteSpace(value))
                {
                    canvas.AddText(
                        x + .20,
                        y + .055,
                        width - 1.65,
                        .23,
                        label,
                        11.7,
                        textColor,
                        true,
                        "l",
                        name: "SDD profile footer-strip text");
                    canvas.AddText(
                        x + width - 1.25,
                        y + .045,
                        1.02,
                        .25,
                        value,
                        13.3,
                        valueColor,
                        true,
                        "r",
                        name: "SDD profile footer-strip value");
                }
                else
                {
                    var combined = string.IsNullOrWhiteSpace(value)
                        ? label
                        : string.IsNullOrWhiteSpace(label)
                            ? value
                            : $"{label} — {value}";
                    canvas.AddText(
                        x + .22,
                        y + .045,
                        width - .44,
                        .24,
                        combined,
                        12.2,
                        textColor,
                        true,
                        "ctr",
                        name: "SDD profile footer-strip text");
                }
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

    private static string InstitutionalModuleHeaderFill(
        ProjectBriefingThemeDefinition theme,
        ProjectBriefingInstitutionalProfileModule module)
    {
        if (theme.IsDark)
        {
            return InstitutionalModuleSoftFill(theme, module);
        }

        return module switch
        {
            ProjectBriefingInstitutionalProfileModule.ProjectsDeveloped => "D6E9E9",
            ProjectBriefingInstitutionalProfileModule.Proliferation => "DCE6F6",
            ProjectBriefingInstitutionalProfileModule.TrainingSupport => "F2DCE0",
            ProjectBriefingInstitutionalProfileModule.IntellectualProperty => "F3E3CE",
            ProjectBriefingInstitutionalProfileModule.Partnerships => "DCEDE4",
            _ => theme.SurfaceMuted
        };
    }

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

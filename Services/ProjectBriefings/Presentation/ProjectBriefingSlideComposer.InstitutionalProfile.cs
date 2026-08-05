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

        // Retain the authorised original institutional-slide composition rather than
        // introducing the standard analytical-deck header on this heritage slide.
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
        var modulesTop = hasHistory ? 2.47 : 1.12;
        var modulesBottom = hasCitation ? 6.22 : 6.50;
        var sourceY = hasCitation ? 6.60 : 6.72;

        if (hasHistory)
        {
            RenderInstitutionalHistory(canvas, profile.HistoryMilestones);
        }

        RenderInstitutionalModules(
            canvas,
            profile.Modules,
            modulesTop,
            modulesBottom - modulesTop);

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
            sourceY,
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
    /// Uses one borderless editable table for all milestone text while retaining the
    /// open alternating timeline from the original authorised slide. The chronology
    /// rule and gold markers remain deliberately simple presentation shapes so the
    /// table never dictates a spreadsheet-like appearance.
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
        const double tableTop = .94;
        const double lineY = 1.62;
        var span = right - left;
        var widths = Enumerable.Repeat(span / items.Length, items.Length).ToArray();
        var compact = items.Length >= 7;
        var noBorders = NativeTableBorders.None;

        var topYears = new List<NativeTableCell>(items.Length);
        var topText = new List<NativeTableCell>(items.Length);
        var spacer = new List<NativeTableCell>(items.Length);
        var bottomYears = new List<NativeTableCell>(items.Length);
        var bottomText = new List<NativeTableCell>(items.Length);

        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var above = index % 2 == 0;
            var year = item.Year.ToString(CultureInfo.InvariantCulture);
            var description = Truncate(item.Text, compact ? 34 : 46);

            topYears.Add(InstitutionalCell(
                above ? year : string.Empty,
                compact ? 9.2 : 10.5,
                canvas.Theme.HeaderAccent,
                true,
                "ctr",
                canvas.Theme.Canvas,
                noBorders));
            topText.Add(InstitutionalCell(
                above ? description : string.Empty,
                compact ? 7.4 : 8.3,
                canvas.Theme.TextPrimary,
                true,
                "ctr",
                canvas.Theme.Canvas,
                noBorders,
                verticalAnchor: "t"));
            spacer.Add(InstitutionalCell(
                string.Empty,
                1,
                canvas.Theme.TextMuted,
                false,
                "ctr",
                canvas.Theme.Canvas,
                noBorders));
            bottomYears.Add(InstitutionalCell(
                above ? string.Empty : year,
                compact ? 9.2 : 10.5,
                canvas.Theme.HeaderAccent,
                true,
                "ctr",
                canvas.Theme.Canvas,
                noBorders));
            bottomText.Add(InstitutionalCell(
                above ? string.Empty : description,
                compact ? 7.4 : 8.3,
                canvas.Theme.TextPrimary,
                true,
                "ctr",
                canvas.Theme.Canvas,
                noBorders,
                verticalAnchor: "t"));
        }

        canvas.AddNativeTable(
            left,
            tableTop,
            widths,
            new[] { .20, .38, .18, .20, .38 },
            new IReadOnlyList<NativeTableCell>[]
            {
                topYears,
                topText,
                spacer,
                bottomYears,
                bottomText
            },
            "SDD institutional history timeline text");

        canvas.AddLine(left, lineY, right, lineY, canvas.Theme.Divider, 1.05);
        for (var index = 0; index < items.Length; index++)
        {
            var x = items.Length == 1
                ? (left + right) / 2d
                : left + ((span * index) / (items.Length - 1));
            canvas.AddText(
                x - .10,
                lineY - .12,
                .20,
                .24,
                "●",
                10.5,
                canvas.Theme.Warning,
                true,
                "ctr",
                name: $"SDD milestone marker {items[index].Year}");
        }
    }

    /// <summary>
    /// Retains the original five-column institutional-output composition. Each card
    /// contains one borderless native table, so users edit meaningful content in one
    /// place instead of maintaining many independent label/value text boxes.
    /// </summary>
    private static void RenderInstitutionalModules(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingInstitutionalModuleData> modules,
        double top,
        double height)
    {
        if (modules.Count == 0) return;

        var visible = modules.Take(5).ToArray();
        const double left = .60;
        const double right = 12.73;
        const double gap = .12;
        var cardWidth = (right - left - ((visible.Length - 1) * gap)) / visible.Length;

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
        var noBorders = NativeTableBorders.None;
        var isPartnership = module.Module == ProjectBriefingInstitutionalProfileModule.Partnerships;
        var rows = module.Rows.ToArray();
        var hasHeadline = !string.IsNullOrWhiteSpace(module.Headline);
        var hasHighlight = !string.IsNullOrWhiteSpace(module.Highlight);

        canvas.AddRoundedRect(
            x,
            y,
            width,
            height,
            canvas.Theme.Surface,
            canvas.Theme.Border,
            .045,
            $"{module.Title} institutional module card");

        // The colour accent and content table are intentionally inset so square table
        // corners never interfere with the rounded institutional-card silhouette.
        canvas.AddRect(
            x + .04,
            y + .02,
            width - .08,
            .065,
            accent,
            name: $"{module.Title} institutional accent");

        var tableX = x + .055;
        var tableY = y + .085;
        var tableWidth = width - .11;
        var tableHeight = height - .15;
        var headerHeight = .57;
        var headlineHeight = hasHeadline ? .50 : 0d;
        var highlightHeight = hasHighlight ? .36 : 0d;
        var bodyHeight = Math.Max(.48, tableHeight - headerHeight - headlineHeight - highlightHeight);
        var visibleRows = rows.Length == 0
            ? Array.Empty<ProjectBriefingInstitutionalMetricRow>()
            : rows.Take(Math.Max(1, (int)Math.Floor(bodyHeight / .28))).ToArray();
        var detailRowHeight = visibleRows.Length == 0 ? bodyHeight : bodyHeight / visibleRows.Length;

        if (isPartnership)
        {
            var partnershipRows = new List<IReadOnlyList<NativeTableCell>>
            {
                new[]
                {
                    InstitutionalCell(
                        Truncate(module.Title, 55),
                        width < 2.30 ? 8.0 : 8.8,
                        accent,
                        true,
                        "ctr",
                        soft,
                        noBorders,
                        leftMargin: .10,
                        rightMargin: .10)
                }
            };
            var heights = new List<double> { headerHeight };
            var partnershipBody = Math.Max(.60, tableHeight - headerHeight);
            var partnershipItems = rows.Take(6).ToArray();
            var rowHeight = partnershipItems.Length == 0
                ? partnershipBody
                : partnershipBody / partnershipItems.Length;

            if (partnershipItems.Length == 0)
            {
                partnershipRows.Add(new[]
                {
                    InstitutionalCell(string.Empty, 8, canvas.Theme.TextMuted, false, "l", canvas.Theme.Surface, noBorders)
                });
                heights.Add(partnershipBody);
            }
            else
            {
                foreach (var row in partnershipItems)
                {
                    partnershipRows.Add(new[]
                    {
                        InstitutionalCell(
                            $"• {Truncate(row.Label, 34)}",
                            width < 2.30 ? 7.7 : 8.2,
                            canvas.Theme.TextSecondary,
                            false,
                            "l",
                            canvas.Theme.Surface,
                            noBorders,
                            leftMargin: .13,
                            rightMargin: .10)
                    });
                    heights.Add(rowHeight);
                }
            }

            canvas.AddNativeTable(
                tableX,
                tableY,
                new[] { tableWidth },
                heights,
                partnershipRows,
                $"{module.Title} institutional module table");
            return;
        }

        var labelWidth = Math.Max(.86, tableWidth - .62);
        var valueWidth = tableWidth - labelWidth;
        var tableRows = new List<IReadOnlyList<NativeTableCell>>();
        var tableHeights = new List<double>();

        tableRows.Add(MergedInstitutionalRow(
            Truncate(module.Title, 50),
            width < 2.30 ? 8.1 : 9.0,
            accent,
            true,
            "ctr",
            soft,
            noBorders));
        tableHeights.Add(headerHeight);

        if (hasHeadline)
        {
            tableRows.Add(MergedInstitutionalRow(
                module.Headline!,
                width < 2.30 ? 18.5 : 20.5,
                canvas.Theme.TextPrimary,
                true,
                "ctr",
                canvas.Theme.Surface,
                noBorders));
            tableHeights.Add(headlineHeight);
        }

        if (visibleRows.Length == 0)
        {
            tableRows.Add(MergedInstitutionalRow(
                string.Empty,
                8,
                canvas.Theme.TextMuted,
                false,
                "l",
                canvas.Theme.Surface,
                noBorders));
            tableHeights.Add(bodyHeight);
        }
        else
        {
            foreach (var row in visibleRows)
            {
                tableRows.Add(new[]
                {
                    InstitutionalCell(
                        Truncate(row.Label, width < 2.30 ? 27 : 34),
                        width < 2.30 ? 7.6 : 8.1,
                        canvas.Theme.TextSecondary,
                        false,
                        "l",
                        canvas.Theme.Surface,
                        noBorders,
                        leftMargin: .10,
                        rightMargin: .03),
                    InstitutionalCell(
                        row.Value,
                        width < 2.30 ? 7.8 : 8.4,
                        canvas.Theme.TextPrimary,
                        true,
                        "r",
                        canvas.Theme.Surface,
                        noBorders,
                        leftMargin: .02,
                        rightMargin: .08)
                });
                tableHeights.Add(detailRowHeight);
            }
        }

        if (hasHighlight)
        {
            tableRows.Add(MergedInstitutionalRow(
                Truncate(module.Highlight, width < 2.30 ? 58 : 72),
                width < 2.30 ? 6.8 : 7.3,
                canvas.Theme.IsDark ? canvas.Theme.TextOnAccent : accent,
                true,
                "l",
                canvas.Theme.IsDark ? accent : soft,
                noBorders));
            tableHeights.Add(highlightHeight);
        }

        canvas.AddNativeTable(
            tableX,
            tableY,
            new[] { labelWidth, valueWidth },
            tableHeights,
            tableRows,
            $"{module.Title} institutional module table");
    }

    private static void RenderInstitutionalCitation(
        SlideCanvas canvas,
        string label,
        int count)
    {
        canvas.AddRoundedRect(
            4.35,
            6.31,
            4.63,
            .40,
            canvas.Theme.SurfaceRaised,
            canvas.Theme.HeaderAccent,
            .035,
            "Institutional recognition strip");
        canvas.AddText(
            4.56,
            6.37,
            4.21,
            .25,
            $"{Truncate(label, 58)} — {count:00}",
            11.0,
            canvas.Theme.TextPrimary,
            true,
            "ctr",
            name: "Institutional recognition text");
    }

    private static IReadOnlyList<NativeTableCell> MergedInstitutionalRow(
        string value,
        double fontSize,
        string color,
        bool bold,
        string align,
        string fill,
        NativeTableBorders borders)
        => new[]
        {
            InstitutionalCell(
                value,
                fontSize,
                color,
                bold,
                align,
                fill,
                borders,
                leftMargin: .10,
                rightMargin: .10,
                gridSpan: 2),
            InstitutionalCell(
                string.Empty,
                fontSize,
                color,
                bold,
                align,
                fill,
                borders,
                horizontalMerge: true)
        };

    private static NativeTableCell InstitutionalCell(
        string? value,
        double fontSize,
        string color,
        bool bold,
        string align,
        string fill,
        NativeTableBorders? borders = null,
        string verticalAnchor = "ctr",
        double leftMargin = .06,
        double rightMargin = .06,
        double topMargin = .02,
        double bottomMargin = .02,
        int gridSpan = 1,
        bool horizontalMerge = false)
        => new(
            value ?? string.Empty,
            fontSize,
            color,
            bold,
            align,
            fill,
            borders ?? NativeTableBorders.None,
            verticalAnchor,
            leftMargin,
            rightMargin,
            topMargin,
            bottomMargin,
            gridSpan,
            horizontalMerge);

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

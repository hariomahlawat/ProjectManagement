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
        AddSlideTitle(canvas, Truncate(profile.Title, 110));

        var hasHistory = profile.HistoryMilestones.Count > 0;
        var hasCitation = profile.UnitCitationCount.HasValue
            && !string.IsNullOrWhiteSpace(profile.UnitCitationLabel);
        var contentTop = hasHistory ? 2.30 : 1.16;
        var contentBottom = hasCitation ? 6.30 : 6.64;

        if (hasHistory)
        {
            RenderInstitutionalHistory(canvas, profile.HistoryMilestones);
        }

        RenderInstitutionalModules(
            canvas,
            profile.Modules,
            contentTop,
            contentBottom - contentTop);

        if (hasCitation)
        {
            RenderInstitutionalCitation(
                canvas,
                profile.UnitCitationLabel!,
                profile.UnitCitationCount!.Value);
        }

        var dataAsOn = profile.DataAsOnUtc.ToUniversalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        canvas.AddText(
            .78,
            6.80,
            11.78,
            .16,
            $"Data as on {dataAsOn} · Source: PRISM ERP",
            7.6,
            canvas.Theme.TextMuted,
            false,
            "ctr",
            name: "SDD profile data source");
    }

    /// <summary>
    /// Renders the complete institutional timeline as one native PowerPoint table.
    /// The table is borderless except for the central chronology rule, so it remains
    /// visually richer than a conventional grid while staying easy to edit as one object.
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

        const double left = .62;
        const double totalWidth = 12.10;
        var widths = Enumerable.Repeat(totalWidth / items.Length, items.Length).ToArray();
        var compact = items.Length >= 7;
        var yearFont = compact ? 9.0 : 10.3;
        var textFont = compact ? 7.25 : 8.1;
        var noBorders = NativeTableBorders.None;
        var timelineBorder = new NativeTableBorders(
            LeftWidth: 0,
            RightWidth: 0,
            TopWidth: 0,
            BottomColor: canvas.Theme.Divider,
            BottomWidth: 1.05);

        var topYears = new List<NativeTableCell>(items.Length);
        var topText = new List<NativeTableCell>(items.Length);
        var markers = new List<NativeTableCell>(items.Length);
        var bottomYears = new List<NativeTableCell>(items.Length);
        var bottomText = new List<NativeTableCell>(items.Length);

        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var above = index % 2 == 0;
            var year = item.Year.ToString(CultureInfo.InvariantCulture);
            var description = Truncate(item.Text, compact ? 34 : 46);

            topYears.Add(InstitutionalTableCell(
                above ? year : string.Empty,
                yearFont,
                canvas.Theme.HeaderAccent,
                true,
                "ctr",
                canvas.Theme.Canvas,
                noBorders));
            topText.Add(InstitutionalTableCell(
                above ? description : string.Empty,
                textFont,
                canvas.Theme.TextPrimary,
                true,
                "ctr",
                canvas.Theme.Canvas,
                noBorders,
                verticalAnchor: "t"));
            markers.Add(InstitutionalTableCell(
                "●",
                compact ? 8.7 : 9.7,
                canvas.Theme.Warning,
                true,
                "ctr",
                canvas.Theme.Canvas,
                timelineBorder,
                bottomMargin: .01));
            bottomYears.Add(InstitutionalTableCell(
                above ? string.Empty : year,
                yearFont,
                canvas.Theme.HeaderAccent,
                true,
                "ctr",
                canvas.Theme.Canvas,
                noBorders));
            bottomText.Add(InstitutionalTableCell(
                above ? string.Empty : description,
                textFont,
                canvas.Theme.TextPrimary,
                true,
                "ctr",
                canvas.Theme.Canvas,
                noBorders,
                verticalAnchor: "t"));
        }

        canvas.AddNativeTable(
            left,
            1.08,
            widths,
            new[] { .20, .36, .18, .20, .36 },
            new IReadOnlyList<NativeTableCell>[]
            {
                topYears,
                topText,
                markers,
                bottomYears,
                bottomText
            },
            "SDD institutional history timeline");
    }

    /// <summary>
    /// Automatic table-first layout. Numeric modules remain generous equal panels;
    /// the non-numeric partnership module becomes a full-width institutional band.
    /// This preserves all selected content without forcing five narrow columns.
    /// </summary>
    private static void RenderInstitutionalModules(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingInstitutionalModuleData> modules,
        double top,
        double height)
    {
        if (modules.Count == 0) return;

        var visible = modules.Take(6).ToArray();
        var partnership = visible.FirstOrDefault(module =>
            module.Module == ProjectBriefingInstitutionalProfileModule.Partnerships);
        var metrics = visible
            .Where(module => module.Module != ProjectBriefingInstitutionalProfileModule.Partnerships)
            .ToArray();

        const double left = .62;
        const double right = 12.72;
        const double gap = .12;
        var hasPartnershipBand = partnership is not null;
        var partnershipHeight = hasPartnershipBand ? .72 : 0d;
        var metricHeight = Math.Max(
            1.18,
            height - (hasPartnershipBand ? partnershipHeight + gap : 0d));

        if (metrics.Length > 0)
        {
            RenderInstitutionalMetricGrid(
                canvas,
                metrics,
                left,
                top,
                right - left,
                metricHeight,
                gap);
        }

        if (partnership is not null)
        {
            var partnershipTop = metrics.Length == 0
                ? top
                : top + metricHeight + gap;
            var bandHeight = metrics.Length == 0
                ? Math.Min(1.30, height)
                : partnershipHeight;
            RenderInstitutionalPartnershipBand(
                canvas,
                partnership,
                left,
                partnershipTop,
                right - left,
                bandHeight);
        }
    }

    private static void RenderInstitutionalMetricGrid(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingInstitutionalModuleData> modules,
        double left,
        double top,
        double width,
        double height,
        double gap)
    {
        if (modules.Count <= 4)
        {
            var cardWidth = modules.Count switch
            {
                1 => Math.Min(6.30, width),
                2 => (width - gap) / 2d,
                3 => (width - (2d * gap)) / 3d,
                _ => (width - (3d * gap)) / 4d
            };
            var startX = modules.Count == 1
                ? left + ((width - cardWidth) / 2d)
                : left;

            for (var index = 0; index < modules.Count; index++)
            {
                RenderInstitutionalMetricModuleTable(
                    canvas,
                    modules[index],
                    startX + (index * (cardWidth + gap)),
                    top,
                    cardWidth,
                    height);
            }
            return;
        }

        // Future-proof fallback for additional numeric blocks: approved 3 + 2/3 layout.
        var firstRowCount = 3;
        var secondRowCount = modules.Count - firstRowCount;
        var rowGap = gap;
        var rowHeight = (height - rowGap) / 2d;
        RenderInstitutionalMetricGrid(canvas, modules.Take(firstRowCount).ToArray(), left, top, width, rowHeight, gap);
        RenderInstitutionalMetricGrid(canvas, modules.Skip(firstRowCount).Take(secondRowCount).ToArray(), left, top + rowHeight + rowGap, width, rowHeight, gap);
    }

    private static void RenderInstitutionalMetricModuleTable(
        SlideCanvas canvas,
        ProjectBriefingInstitutionalModuleData module,
        double x,
        double y,
        double width,
        double height)
    {
        var accent = InstitutionalModuleAccent(canvas.Theme, module.Module);
        var soft = InstitutionalModuleSoftFill(canvas.Theme, module.Module);
        var headerFill = canvas.Theme.IsDark ? accent : soft;
        var headerText = canvas.Theme.IsDark ? canvas.Theme.TextOnAccent : accent;
        var highlightFill = canvas.Theme.IsDark ? accent : soft;
        var highlightText = canvas.Theme.IsDark ? canvas.Theme.TextOnAccent : accent;
        var detailRows = module.Rows.ToArray();
        var hasHeadline = !string.IsNullOrWhiteSpace(module.Headline);
        var hasHighlight = !string.IsNullOrWhiteSpace(module.Highlight);
        var fixedHeight = .45 + (hasHeadline ? .46 : 0d) + (hasHighlight ? .34 : 0d);
        var bodyHeight = Math.Max(.42, height - fixedHeight);
        var rowHeight = detailRows.Length == 0
            ? bodyHeight
            : Math.Max(.24, bodyHeight / detailRows.Length);
        var widths = new[] { Math.Max(.85, width - .78), .78 };
        var rows = new List<IReadOnlyList<NativeTableCell>>();
        var heights = new List<double>();

        rows.Add(MergedInstitutionalRow(
            Truncate(module.Title, 54),
            width < 2.65 ? 8.2 : 9.1,
            headerText,
            true,
            "ctr",
            headerFill));
        heights.Add(.45);

        if (hasHeadline)
        {
            rows.Add(MergedInstitutionalRow(
                module.Headline!,
                width < 2.65 ? 17.2 : 19.5,
                canvas.Theme.TextPrimary,
                true,
                "ctr",
                canvas.Theme.Surface));
            heights.Add(.46);
        }

        if (detailRows.Length == 0)
        {
            rows.Add(MergedInstitutionalRow(
                string.Empty,
                8.2,
                canvas.Theme.TextMuted,
                false,
                "l",
                canvas.Theme.Surface));
            heights.Add(bodyHeight);
        }
        else
        {
            for (var index = 0; index < detailRows.Length; index++)
            {
                var row = detailRows[index];
                var fill = index % 2 == 0 ? canvas.Theme.Surface : canvas.Theme.SurfaceMuted;
                rows.Add(new[]
                {
                    InstitutionalTableCell(
                        Truncate(row.Label, width < 2.65 ? 28 : 38),
                        width < 2.65 ? 7.7 : 8.2,
                        canvas.Theme.TextSecondary,
                        false,
                        "l",
                        fill),
                    InstitutionalTableCell(
                        row.Value,
                        width < 2.65 ? 7.9 : 8.5,
                        canvas.Theme.TextPrimary,
                        true,
                        "r",
                        fill,
                        rightMargin: .08)
                });
                heights.Add(rowHeight);
            }
        }

        if (hasHighlight)
        {
            rows.Add(MergedInstitutionalRow(
                Truncate(module.Highlight, width < 2.65 ? 78 : 96),
                width < 2.65 ? 7.0 : 7.5,
                highlightText,
                true,
                "l",
                highlightFill));
            heights.Add(.34);
        }

        canvas.AddNativeTable(
            x,
            y,
            widths,
            heights,
            rows,
            $"{module.Title} institutional module table");
    }

    private static void RenderInstitutionalPartnershipBand(
        SlideCanvas canvas,
        ProjectBriefingInstitutionalModuleData module,
        double x,
        double y,
        double width,
        double height)
    {
        var accent = InstitutionalModuleAccent(canvas.Theme, module.Module);
        var soft = InstitutionalModuleSoftFill(canvas.Theme, module.Module);
        var headerFill = canvas.Theme.IsDark ? accent : soft;
        var headerText = canvas.Theme.IsDark ? canvas.Theme.TextOnAccent : accent;
        var entries = module.Rows
            .Select(row => row.Label)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var entryText = entries.Length == 0
            ? string.Empty
            : string.Join("  ·  ", entries.Select(value => Truncate(value, 70)));

        canvas.AddNativeTable(
            x,
            y,
            new[] { width },
            new[] { Math.Min(.30, height * .43), Math.Max(.32, height - Math.Min(.30, height * .43)) },
            new IReadOnlyList<NativeTableCell>[]
            {
                new[]
                {
                    InstitutionalTableCell(
                        Truncate(module.Title, 80),
                        9.1,
                        headerText,
                        true,
                        "ctr",
                        headerFill)
                },
                new[]
                {
                    InstitutionalTableCell(
                        entryText,
                        entries.Length >= 6 ? 7.8 : 8.6,
                        canvas.Theme.TextPrimary,
                        false,
                        "ctr",
                        canvas.Theme.Surface,
                        verticalAnchor: "ctr",
                        leftMargin: .16,
                        rightMargin: .16)
                }
            },
            "Military academia industry synergy table");
    }

    private static void RenderInstitutionalCitation(
        SlideCanvas canvas,
        string label,
        int count)
    {
        canvas.AddNativeTable(
            .62,
            6.40,
            new[] { 10.85, 1.25 },
            new[] { .31 },
            new IReadOnlyList<NativeTableCell>[]
            {
                new[]
                {
                    InstitutionalTableCell(
                        $"{Truncate(label, 70)} —",
                        8.9,
                        canvas.Theme.TextPrimary,
                        true,
                        "l",
                        canvas.Theme.SurfaceRaised,
                        leftMargin: .16),
                    InstitutionalTableCell(
                        count.ToString("00", CultureInfo.InvariantCulture),
                        11.0,
                        canvas.Theme.HeaderAccent,
                        true,
                        "r",
                        canvas.Theme.SurfaceRaised,
                        rightMargin: .16)
                }
            },
            "Institutional recognition table");
    }

    private static IReadOnlyList<NativeTableCell> MergedInstitutionalRow(
        string value,
        double fontSize,
        string color,
        bool bold,
        string align,
        string fill)
        => new[]
        {
            InstitutionalTableCell(
                value,
                fontSize,
                color,
                bold,
                align,
                fill,
                gridSpan: 2,
                leftMargin: .12,
                rightMargin: .12),
            InstitutionalTableCell(
                string.Empty,
                fontSize,
                color,
                bold,
                align,
                fill,
                horizontalMerge: true)
        };

    private static NativeTableCell InstitutionalTableCell(
        string? value,
        double fontSize,
        string color,
        bool bold,
        string align,
        string fill,
        NativeTableBorders? borders = null,
        string verticalAnchor = "ctr",
        double leftMargin = .08,
        double rightMargin = .08,
        double topMargin = .025,
        double bottomMargin = .025,
        int gridSpan = 1,
        bool horizontalMerge = false)
        => new(
            value ?? string.Empty,
            fontSize,
            color,
            bold,
            align,
            fill,
            borders,
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

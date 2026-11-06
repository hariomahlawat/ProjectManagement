using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

/// <summary>
/// The key points layout ("Option B", docs/ux/briefing-decks.md), used by every capability and
/// project brief slide of a deck that shows key points. Stage and cost sit on one line under the
/// title; the photograph (16:9) and the capability or brief text share the top row; a full-width
/// band at the foot holds the present status remark and, on the slide the deck chose, the key
/// points. Whatever a deck or project leaves out gives its space back to the top row. Decks without
/// key points keep the established layouts.
/// </summary>
public sealed partial class ProjectBriefingSlideComposer
{
    private const double KeyPointLayoutTop = 1.28;
    private const double KeyPointLayoutBottom = 6.70;
    private const double KeyPointLayoutLeft = .60;
    private const double KeyPointLayoutRight = 12.73;
    private const double KeyPointRowGap = .16;
    private const double KeyPointColumnGap = .20;
    private const double KeyPointPhotoMaxWidth = 5.70;

    // Band geometry: a heading column beside the text, as on the mock-up.
    private const double KeyPointBandPaddingTop = .12;
    private const double KeyPointBandPaddingBottom = .10;
    private const double KeyPointBandLabelX = .88;
    private const double KeyPointBandLabelWidth = 1.95;
    private const double KeyPointBandTextX = KeyPointBandLabelX + KeyPointBandLabelWidth + .15;
    private const double KeyPointBandTextWidth = KeyPointLayoutRight - .28 - KeyPointBandTextX;
    private const double KeyPointBandHeadingHeight = .24;
    private const double KeyPointBandTextFont = 11.0;
    private const double KeyPointBandLabelFont = 10.0;
    private const double KeyPointBandLineHeight = KeyPointBandTextFont * 1.2 / 72d;
    // 11 pt across the 9.47 in text column holds about 145 characters; 138 keeps a margin, so a
    // point at the 130-character limit stays on one line.
    private const int KeyPointBandCharactersPerLine = 138;
    private const int KeyPointStatusMaximumCharacters = 260;
    private const int KeyPointStatusMaximumLines = 3;

    /// <summary>True when the deck's capability and project brief slides use the key points layout.</summary>
    public static bool UsesKeyPointLayout(ProjectBriefingPresentationData data)
        => data.Layout == ProjectBriefingLayout.StandardBriefing
            && data.StandardSlideOptions.KeyPoints != ProjectBriefingKeyPointPlacement.None;

    /// <summary>The project slide that carries the key points, or null when the deck shows none.</summary>
    public static ProjectBriefingSlideRole? KeyPointSlide(ProjectBriefingPresentationData data)
    {
        if (!UsesKeyPointLayout(data))
        {
            return null;
        }

        return data.NarrativeMode switch
        {
            ProjectBriefingNarrativeMode.CapabilityOverview => ProjectBriefingSlideRole.ProjectCapability,
            ProjectBriefingNarrativeMode.ProjectBrief => ProjectBriefingSlideRole.ProjectBrief,
            _ => data.StandardSlideOptions.KeyPoints == ProjectBriefingKeyPointPlacement.CapabilitySlide
                ? ProjectBriefingSlideRole.ProjectCapability
                : ProjectBriefingSlideRole.ProjectBrief
        };
    }

    /// <summary>
    /// Whether the project's slides draw the lifecycle subtitle ("Ongoing · category"): capability
    /// slides and continuations do; in the key points layout a first capability slide does only when
    /// it has no stage and cost line.
    /// </summary>
    public static bool DrawsLifecycleSubtitle(
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
    {
        var briefContinues = DrawsProjectBriefSlides(data.NarrativeMode)
            && ProjectBriefSlideCount(data, project) > 1;
        if (!DrawsCapabilitySlides(data.NarrativeMode))
        {
            return briefContinues;
        }

        if (!UsesKeyPointLayout(data))
        {
            return true;
        }

        var hasMetaLine = data.CostMode != ProjectBriefingCostMode.None
            || (data.StandardSlideOptions.ShowPresentStage
                && !string.IsNullOrWhiteSpace(NormalizePresentationText(project.PresentStage)));
        return !hasMetaLine
            || PaginateKeyPointCapability(data, project).Pages.Count > 1
            || briefContinues;
    }

    private sealed record KeyPointBandRow(string Label, string Text, bool IsStatus, double Height);

    private sealed record KeyPointSlideLayout(
        double PhotoX,
        double PhotoY,
        double PhotoWidth,
        double PhotoHeight,
        double TextX,
        double TextY,
        double TextWidth,
        double TextHeight,
        double BandY,
        double BandHeight,
        IReadOnlyList<KeyPointBandRow> StatusRows,
        IReadOnlyList<KeyPointBandRow> KeyPointRows);

    // The photograph gives way before the text continues: the widest photograph whose slide keeps
    // its capability or brief text on one page, else the widest (and the text continues).
    private static readonly double[] KeyPointPhotoWidths = { KeyPointPhotoMaxWidth, 5.00, 4.40 };

    private static KeyPointSlideLayout ResolveKeyPointSlideLayout(
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project,
        ProjectBriefingSlideRole role)
    {
        KeyPointSlideLayout? widest = null;
        foreach (var photoWidth in KeyPointPhotoWidths)
        {
            var layout = ResolveKeyPointSlideLayout(data, project, role, photoWidth);
            widest ??= layout;
            var pages = role == ProjectBriefingSlideRole.ProjectCapability
                ? KeyPointCapabilityPages(project, layout).Pages.Count
                : KeyPointProjectBriefPages(project, layout).Count;
            if (pages == 1)
            {
                return layout;
            }
        }

        return widest!;
    }

    private static KeyPointSlideLayout ResolveKeyPointSlideLayout(
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project,
        ProjectBriefingSlideRole role,
        double maximumPhotoWidth)
    {
        var statusRows = new List<KeyPointBandRow>(1);
        if (data.StandardSlideOptions.ShowPresentStatus)
        {
            var status = TruncateAtWord(NormalizeExternalStatus(project.ExternalStatus), KeyPointStatusMaximumCharacters)
                .Replace('\n', ' ');
            if (!string.IsNullOrWhiteSpace(status))
            {
                statusRows.Add(new KeyPointBandRow(
                    "PRESENT STATUS",
                    status,
                    IsStatus: true,
                    KeyPointBandRowHeight(status, KeyPointStatusMaximumLines)));
            }
        }

        var keyPointRows = KeyPointSlide(data) == role
            ? project.KeyPoints
                .Where(point => !string.IsNullOrWhiteSpace(point.Text))
                .Select(point => new KeyPointBandRow(
                    point.Heading,
                    point.Text.Trim(),
                    IsStatus: false,
                    KeyPointBandRowHeight(point.Text, maximumLines: 2)))
                .ToList()
            : new List<KeyPointBandRow>();

        var bandHeight = statusRows.Count + keyPointRows.Count == 0
            ? 0d
            : KeyPointBandPaddingTop
                + statusRows.Sum(row => row.Height)
                + (keyPointRows.Count > 0 ? KeyPointBandHeadingHeight + keyPointRows.Sum(row => row.Height) : 0d)
                + KeyPointBandPaddingBottom;
        var bandY = KeyPointLayoutBottom - bandHeight;
        var rowBottom = bandHeight > 0 ? bandY - KeyPointRowGap : KeyPointLayoutBottom;
        var rowHeight = rowBottom - KeyPointLayoutTop;

        // The photograph keeps its 16:9 shape: as tall as the row, up to a cap on its width, and
        // centred in the row when the cap leaves it shorter.
        var photoWidth = Math.Min(maximumPhotoWidth, ((rowHeight - .20) * 16d / 9d) + .20);
        var photoHeight = Math.Min(rowHeight, ((photoWidth - .20) * 9d / 16d) + .20);
        var photoY = KeyPointLayoutTop + ((rowHeight - photoHeight) / 2d);
        var textX = KeyPointLayoutLeft + photoWidth + KeyPointColumnGap;

        return new KeyPointSlideLayout(
            KeyPointLayoutLeft,
            photoY,
            photoWidth,
            photoHeight,
            textX,
            KeyPointLayoutTop,
            KeyPointLayoutRight - textX,
            rowHeight,
            bandY,
            bandHeight,
            statusRows,
            keyPointRows);
    }

    private static double KeyPointBandRowHeight(string text, int maximumLines)
    {
        var lines = Math.Clamp(EstimateWrappedLines(text, KeyPointBandCharactersPerLine), 1, maximumLines);
        return (lines * KeyPointBandLineHeight) + .12;
    }

    // Capability pages for the key points layout: the first page fits the top row's text panel.
    private static ProjectBriefingCapabilityPagination PaginateKeyPointCapability(
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
        => KeyPointCapabilityPages(
            project,
            ResolveKeyPointSlideLayout(data, project, ProjectBriefingSlideRole.ProjectCapability));

    private static IReadOnlyList<ProjectBriefingUpdateSheetBriefPage> PaginateKeyPointProjectBrief(
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
        => KeyPointProjectBriefPages(
            project,
            ResolveKeyPointSlideLayout(data, project, ProjectBriefingSlideRole.ProjectBrief));

    private static ProjectBriefingCapabilityPagination KeyPointCapabilityPages(
        ProjectBriefingPresentationProject project,
        KeyPointSlideLayout layout)
    {
        // The established panel (7.48 in wide, 5.42 in tall) holds 72 characters a line in 4.38 in.
        var charactersPerLine = (int)Math.Floor((layout.TextWidth - .60) * 72d / 6.88d);
        return ProjectBriefingCapabilityPaginator.Paginate(
            project.BriefDescription,
            layout.TextHeight - 1.04,
            charactersPerLine);
    }

    private static IReadOnlyList<ProjectBriefingUpdateSheetBriefPage> KeyPointProjectBriefPages(
        ProjectBriefingPresentationProject project,
        KeyPointSlideLayout layout)
        => PaginateProjectBrief(
            ProjectBriefingUpdateSheetPlanner.NormalizeBrief(project.ProjectBrief),
            layout.TextWidth,
            layout.TextHeight);

    private static void RenderKeyPointCapabilitySlide(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project,
        ProjectBriefingCapabilityPage page)
    {
        var layout = ResolveKeyPointSlideLayout(data, project, ProjectBriefingSlideRole.ProjectCapability);
        AddKeyPointSlideHeader(canvas, data, project, $"{project.LifecycleDisplay} · {CategoryLine(project)}");
        AddProjectPhoto(
            canvas,
            project,
            layout.PhotoX,
            layout.PhotoY,
            layout.PhotoWidth,
            layout.PhotoHeight,
            "Project photograph frame");
        RenderCapabilityPanel(
            canvas,
            layout.TextX,
            layout.TextY,
            layout.TextWidth,
            layout.TextHeight,
            "CAPABILITY OVERVIEW",
            page);
        AddKeyPointBand(canvas, layout);
    }

    private static void RenderKeyPointProjectBriefSlide(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project,
        ProjectBriefingUpdateSheetBriefPage page)
    {
        var layout = ResolveKeyPointSlideLayout(data, project, ProjectBriefingSlideRole.ProjectBrief);
        AddKeyPointSlideHeader(canvas, data, project, fallbackSubtitle: null);
        AddProjectPhoto(
            canvas,
            project,
            layout.PhotoX,
            layout.PhotoY,
            layout.PhotoWidth,
            layout.PhotoHeight,
            "Project brief photograph frame");
        AddProjectBriefPanel(
            canvas,
            layout.TextX,
            layout.TextY,
            layout.TextWidth,
            layout.TextHeight,
            page,
            "PROJECT BRIEF");
        AddKeyPointBand(canvas, layout);
    }

    // The title, then stage and cost on one line beneath it (in place of the context and cost
    // cards). With neither shown, the slide keeps its usual subtitle, if any.
    private static void AddKeyPointSlideHeader(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project,
        string? fallbackSubtitle)
    {
        var runs = KeyPointMetaRuns(canvas, data, project);
        if (runs.Count == 0)
        {
            AddSlideTitle(canvas, Truncate(project.ProjectName, 82), fallbackSubtitle);
            return;
        }

        AddSlideTitle(canvas, Truncate(project.ProjectName, 82));
        canvas.AddRichTextBox(
            canvas.ShowBranding ? 1.30 : .62,
            .70,
            canvas.ShowBranding ? 10.73 : 11.40,
            .26,
            new[]
            {
                new RichTextParagraph(runs, Align: canvas.ShowBranding ? "ctr" : "l")
            },
            "Stage and cost",
            verticalAnchor: "ctr",
            allowAutoFit: true,
            leftInset: .03,
            rightInset: .03,
            topInset: 0,
            bottomInset: 0);
    }

    private static IReadOnlyList<RichTextRun> KeyPointMetaRuns(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
    {
        const double size = 10.5;
        var theme = canvas.Theme;
        var parts = new List<IReadOnlyList<RichTextRun>>(3);

        var stage = data.StandardSlideOptions.ShowPresentStage
            ? NormalizePresentationText(project.PresentStage)
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(stage))
        {
            parts.Add(new[] { new RichTextRun(stage, size, theme.TextPrimary, Bold: true) });
        }

        if (data.CostMode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both)
        {
            var available = project.CostRd.IsAvailable;
            var cost = new List<RichTextRun>
            {
                new("Cost (R&D) ", size, theme.TextSecondary),
                new(available ? project.CostRd.DisplayValue : "Not recorded", size, available ? theme.TextPrimary : theme.TextMuted, Bold: available)
            };
            if (available && !string.IsNullOrWhiteSpace(project.CostRd.BasisDisplay))
            {
                cost.Add(new RichTextRun($" {project.CostRd.BasisDisplay}", size, theme.TextMuted));
            }

            parts.Add(cost);
        }

        if (data.CostMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both
            && ProjectBriefingContentGaps.ShowsProliferationCost(project.LifecycleStatus, project.ProliferationCost))
        {
            var available = project.ProliferationCost.IsAvailable;
            parts.Add(new[]
            {
                new RichTextRun("Proliferation ", size, theme.TextSecondary),
                new RichTextRun(
                    available ? project.ProliferationCost.DisplayValue : "Not recorded",
                    size,
                    available ? theme.TextPrimary : theme.TextMuted,
                    Bold: available)
            });
        }

        var runs = new List<RichTextRun>();
        for (var index = 0; index < parts.Count; index++)
        {
            if (index > 0)
            {
                runs.Add(new RichTextRun("   |   ", size, theme.Divider));
            }

            runs.AddRange(parts[index]);
        }

        return runs;
    }

    private static void AddKeyPointBand(SlideCanvas canvas, KeyPointSlideLayout layout)
    {
        if (layout.BandHeight <= 0)
        {
            return;
        }

        var theme = canvas.Theme;
        canvas.AddRoundedRect(
            KeyPointLayoutLeft,
            layout.BandY,
            KeyPointLayoutRight - KeyPointLayoutLeft,
            layout.BandHeight,
            theme.Surface,
            theme.Border,
            .08,
            "Status and key points band");

        var y = layout.BandY + KeyPointBandPaddingTop;
        foreach (var row in layout.StatusRows)
        {
            AddKeyPointBandRow(canvas, row, y, theme.OperationalAccent, 9.0);
            y += row.Height;
        }

        if (layout.KeyPointRows.Count == 0)
        {
            return;
        }

        if (layout.StatusRows.Count > 0)
        {
            canvas.AddLine(
                KeyPointBandLabelX,
                y - .04,
                KeyPointLayoutRight - .28,
                y - .04,
                theme.Divider,
                .75);
        }

        canvas.AddText(
            KeyPointBandLabelX,
            y,
            4.0,
            KeyPointBandHeadingHeight - .04,
            "KEY POINTS",
            9.0,
            theme.NarrativeAccent,
            true,
            "l",
            "t",
            "Key points heading");
        y += KeyPointBandHeadingHeight;

        for (var index = 0; index < layout.KeyPointRows.Count; index++)
        {
            var row = layout.KeyPointRows[index];
            AddKeyPointBandRow(canvas, row, y, theme.TextSecondary, KeyPointBandLabelFont);
            y += row.Height;
            if (index < layout.KeyPointRows.Count - 1)
            {
                canvas.AddLine(
                    KeyPointBandLabelX,
                    y - .05,
                    KeyPointLayoutRight - .28,
                    y - .05,
                    theme.Border,
                    .5);
            }
        }
    }

    private static void AddKeyPointBandRow(
        SlideCanvas canvas,
        KeyPointBandRow row,
        double y,
        string labelColor,
        double labelFont)
    {
        var height = row.Height - .08;
        canvas.AddText(
            KeyPointBandLabelX,
            y,
            KeyPointBandLabelWidth,
            height,
            row.Label,
            labelFont,
            labelColor,
            true,
            "l",
            "t",
            row.IsStatus ? "Present status label" : $"{row.Label} label");
        canvas.AddText(
            KeyPointBandTextX,
            y,
            KeyPointBandTextWidth,
            height,
            row.Text,
            KeyPointBandTextFont,
            canvas.Theme.TextPrimary,
            false,
            "l",
            "t",
            row.IsStatus ? "Present status" : row.Label);
    }
}

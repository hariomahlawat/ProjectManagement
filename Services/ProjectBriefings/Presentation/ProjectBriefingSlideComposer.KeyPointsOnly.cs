using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

/// <summary>
/// The "Key points only" project slide (docs/ux/briefing-decks.md, Plan: Blocks, Project Officer
/// decks and focus view, PR 5): a large photograph on the left and the project's key points down
/// the right, each under its fixed heading, with no capability or brief text. It is meant for
/// visitors, so the photograph and the points lead and status and cost stay modest: stage and cost
/// sit on the line under the title, as in the key points layout, and the present status remark,
/// when the deck shows it, runs under the photograph and the points. A project without key points
/// shows the gap as a placeholder; nothing else is put in their place. Always one slide.
/// </summary>
public sealed partial class ProjectBriefingSlideComposer
{
    private const double KeyPointsOnlyPhotoWidth = 6.10;
    private const double KeyPointsOnlyColumnGap = .24;
    private const double KeyPointsOnlyCardGap = .08;
    private const double KeyPointsOnlyCardMaximumHeight = 1.30;
    private const double KeyPointsOnlyCardPaddingX = .22;
    private const double KeyPointsOnlyCardPaddingY = .08;
    private const double KeyPointsOnlyHeadingFont = 10.5;
    private const double KeyPointsOnlyHeadingHeight = .22;
    private const double KeyPointsOnlyAccentWidth = .06;

    // The largest size every point fits at; 11 pt is the floor the key point band already uses.
    private static readonly double[] KeyPointsOnlyTextSizes = { 13.0, 12.0, 11.0 };

    private sealed record KeyPointsOnlyLayout(
        double PhotoY,
        double PhotoHeight,
        double PointsX,
        double PointsY,
        double PointsWidth,
        double PointsHeight,
        double CardHeight,
        double CardGap,
        double TextSize,
        IReadOnlyList<ProjectBriefingKeyPoint> Points,
        IReadOnlyList<KeyPointBandRow> StatusRows,
        double StatusBandY,
        double StatusBandHeight);

    /// <summary>The slide plan for one project's "Key points only" slide.</summary>
    private static SlidePlan KeyPointsOnlyPlan(
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
        => new(
            SlidePlanKind.Project,
            canvas => RenderKeyPointsOnlySlide(canvas, data, project),
            ProjectBriefingSlideRole.ProjectKeyPointsOnly,
            project.ProjectId);

    /// <summary>
    /// Composes a deck of "Key points only" slides, one per project with slides of its own, so the
    /// design can be tested and previewed before the deck's block list can choose it.
    /// </summary>
    internal ProjectBriefingComposition ComposeKeyPointsOnlySlides(ProjectBriefingPresentationData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var plans = OrderProjects(data.Projects)
            .Where(project => !project.TableRowOnly)
            .Select(project => KeyPointsOnlyPlan(data, project))
            .ToList();
        return ComposePlans(data, plans);
    }

    private static void RenderKeyPointsOnlySlide(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
    {
        var layout = ResolveKeyPointsOnlyLayout(data, project);
        AddKeyPointSlideHeader(canvas, data, project, $"{project.LifecycleDisplay} · {CategoryLine(project)}");
        AddProjectPhoto(
            canvas,
            project,
            KeyPointLayoutLeft,
            layout.PhotoY,
            KeyPointsOnlyPhotoWidth,
            layout.PhotoHeight,
            "Project photograph frame");

        if (layout.Points.Count == 0)
        {
            AddKeyPointsOnlyPlaceholder(canvas, layout);
        }
        else
        {
            AddKeyPointsOnlyCards(canvas, layout);
        }

        if (layout.StatusRows.Count > 0)
        {
            AddKeyPointStatusBand(canvas, layout.StatusBandY, layout.StatusBandHeight, layout.StatusRows);
        }
    }

    private static KeyPointsOnlyLayout ResolveKeyPointsOnlyLayout(
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
    {
        var statusRows = KeyPointStatusRows(data, project);
        var statusBandHeight = KeyPointBandHeight(statusRows, Array.Empty<KeyPointBandRow>());
        var statusBandY = KeyPointLayoutBottom - statusBandHeight;
        var rowBottom = statusBandHeight > 0 ? statusBandY - KeyPointRowGap : KeyPointLayoutBottom;
        var rowHeight = rowBottom - KeyPointLayoutTop;

        // The photograph keeps its 16:9 shape inside the frame's .10 in margin and is centred in
        // the row beside the points.
        var photoHeight = Math.Min(rowHeight, ((KeyPointsOnlyPhotoWidth - .20) * 9d / 16d) + .20);
        var photoY = KeyPointLayoutTop + ((rowHeight - photoHeight) / 2d);

        var pointsX = KeyPointLayoutLeft + KeyPointsOnlyPhotoWidth + KeyPointsOnlyColumnGap;
        var pointsWidth = KeyPointLayoutRight - pointsX;
        var points = project.KeyPoints
            .Where(point => !string.IsNullOrWhiteSpace(point.Text))
            .Select(point => point with
            {
                Heading = NormalizePresentationText(point.Heading),
                Text = NormalizePresentationText(point.Text).Replace('\n', ' ')
            })
            .ToArray();

        if (points.Length == 0)
        {
            return new KeyPointsOnlyLayout(
                photoY, photoHeight, pointsX, KeyPointLayoutTop, pointsWidth, rowHeight,
                0, 0, KeyPointsOnlyTextSizes[^1], points, statusRows, statusBandY, statusBandHeight);
        }

        // Every card has the same height: the row shared out, up to a cap so a project with only
        // one or two points does not stretch them; the stack is centred in the row.
        var gaps = (points.Length - 1) * KeyPointsOnlyCardGap;
        var cardHeight = Math.Min(KeyPointsOnlyCardMaximumHeight, (rowHeight - gaps) / points.Length);
        var textWidth = pointsWidth - KeyPointsOnlyAccentWidth - (2 * KeyPointsOnlyCardPaddingX);
        var textSize = KeyPointsOnlyTextSizes[^1];
        foreach (var size in KeyPointsOnlyTextSizes)
        {
            if (points.All(point => KeyPointsOnlyCardContentHeight(point.Text, textWidth, size) <= cardHeight))
            {
                textSize = size;
                break;
            }
        }

        var stackHeight = (points.Length * cardHeight) + gaps;
        return new KeyPointsOnlyLayout(
            photoY,
            photoHeight,
            pointsX,
            KeyPointLayoutTop + ((rowHeight - stackHeight) / 2d),
            pointsWidth,
            stackHeight,
            cardHeight,
            KeyPointsOnlyCardGap,
            textSize,
            points,
            statusRows,
            statusBandY,
            statusBandHeight);
    }

    // The height a card needs for its heading and text at one size. A character is taken as half an
    // em, wider than the key point band's measure (138 characters of 11 pt in 9.47 in, about .45 em):
    // a point that only just wraps in the preview's Aptos must not gain a line in PowerPoint and
    // overflow its card, so the estimate errs towards more lines.
    internal const double KeyPointsOnlyCharacterWidthEm = .50;

    private static double KeyPointsOnlyCardContentHeight(string text, double textWidth, double size)
    {
        var charactersPerLine = (int)Math.Floor(textWidth * 72d / (size * KeyPointsOnlyCharacterWidthEm));
        var lines = EstimateWrappedLines(text, charactersPerLine);
        return (2 * KeyPointsOnlyCardPaddingY) + KeyPointsOnlyHeadingHeight + (lines * size * 1.2 / 72d);
    }

    private static void AddKeyPointsOnlyCards(SlideCanvas canvas, KeyPointsOnlyLayout layout)
    {
        var theme = canvas.Theme;
        var y = layout.PointsY;
        foreach (var point in layout.Points)
        {
            canvas.AddRoundedRect(
                layout.PointsX,
                y,
                layout.PointsWidth,
                layout.CardHeight,
                theme.Surface,
                theme.Border,
                .08,
                $"{point.Heading} card");
            canvas.AddRect(
                layout.PointsX,
                y,
                KeyPointsOnlyAccentWidth,
                layout.CardHeight,
                theme.NarrativeAccent,
                name: $"{point.Heading} accent");
            canvas.AddRichTextBox(
                layout.PointsX + KeyPointsOnlyAccentWidth + KeyPointsOnlyCardPaddingX,
                y + KeyPointsOnlyCardPaddingY,
                layout.PointsWidth - KeyPointsOnlyAccentWidth - (2 * KeyPointsOnlyCardPaddingX),
                layout.CardHeight - (2 * KeyPointsOnlyCardPaddingY),
                new[]
                {
                    new RichTextParagraph(
                        new[] { new RichTextRun(point.Heading, KeyPointsOnlyHeadingFont, theme.NarrativeAccent, Bold: true) },
                        SpaceAfterPoints: 2),
                    new RichTextParagraph(
                        new[] { new RichTextRun(point.Text, layout.TextSize, theme.TextPrimary) })
                },
                point.Heading,
                verticalAnchor: "ctr",
                allowAutoFit: true,
                leftInset: 0,
                rightInset: 0,
                topInset: 0,
                bottomInset: 0);
            y += layout.CardHeight + layout.CardGap;
        }
    }

    // No key points recorded: the gap shows as the photograph's does, with nothing in its place.
    private static void AddKeyPointsOnlyPlaceholder(SlideCanvas canvas, KeyPointsOnlyLayout layout)
        => canvas.AddTextShape(
            layout.PointsX,
            layout.PointsY,
            layout.PointsWidth,
            layout.PointsHeight,
            canvas.Theme.Placeholder,
            canvas.Theme.Border,
            .75,
            "roundRect",
            new[]
            {
                new RichTextParagraph(
                    new[] { new RichTextRun("KEY POINTS NOT RECORDED", 8.7, canvas.Theme.TextMuted, Bold: true) },
                    Align: "ctr")
            },
            "Key points placeholder",
            verticalAnchor: "ctr",
            allowAutoFit: false,
            leftInset: .12,
            rightInset: .12,
            topInset: .06,
            bottomInset: .06);
}

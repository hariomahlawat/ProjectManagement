using System.Globalization;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ProjectBriefings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private static List<SlidePlan> BuildProjectUpdateSheetPlans(ProjectBriefingPresentationData data)
    {
        var plans = new List<SlidePlan>();
        AddIntroductoryPlans(plans, data);

        foreach (var project in OrderProjects(data.Projects))
        {
            var planningRows = ResolveProjectUpdateRows(
                project,
                data.UpdateSheetOptions,
                text: "191B20",
                muted: "667085");
            var planningLayout = ResolveProjectUpdateSheetLayout(planningRows, project.ProjectBrief);
            var briefChunks = SplitUpdateSheetBrief(
                project.ProjectBrief,
                planningLayout.BriefWidth - .38,
                planningLayout.BriefHeight - .62);

            var capturedProject = project;
            var firstChunk = briefChunks[0];
            plans.Add(new SlidePlan(SlidePlanKind.Project, canvas =>
                RenderProjectUpdateSheet(canvas, data, capturedProject, firstChunk)));

            for (var index = 1; index < briefChunks.Count; index++)
            {
                var continuationChunk = briefChunks[index];
                var continuationNumber = index + 1;
                plans.Add(new SlidePlan(SlidePlanKind.Project, canvas =>
                    RenderProjectUpdateSheetBriefContinuation(
                        canvas,
                        capturedProject,
                        continuationChunk,
                        continuationNumber)));
            }
        }

        return plans;
    }

    private static void RenderProjectUpdateSheet(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project,
        string projectBrief)
    {
        var theme = canvas.Theme;
        var labelFill = theme.ProjectUpdateLabelFill;
        var serialFill = theme.SurfaceMuted;
        var bodyFill = theme.Surface;

        AddProjectSlideHeader(
            canvas,
            project.ProjectName,
            subtitle: null,
            variant: ProjectSlideHeaderVariant.ProjectUpdateSheet);

        var rows = ResolveProjectUpdateRows(project, data.UpdateSheetOptions, theme.TextPrimary, theme.TextMuted);
        var layout = ResolveProjectUpdateSheetLayout(rows, projectBrief);
        var layoutName = UpdateSheetLayoutName(layout.Variant);

        canvas.AddRect(
            layout.FactsX,
            layout.FactsY,
            layout.FactsWidth,
            layout.FactsHeight,
            theme.Surface,
            theme.Border,
            .8,
            $"Project facts panel - {layoutName}");
        canvas.AddNativeTable(
            layout.FactsX,
            layout.FactsY,
            layout.TableColumnWidths,
            layout.RowHeights,
            BuildProjectUpdateTableRows(rows, theme.TextPrimary, theme.TextMuted, serialFill, labelFill, bodyFill),
            $"Project update facts table - {layoutName}");

        RenderProjectUpdateSheetPhoto(canvas, project, layout, layoutName);
        RenderProjectUpdateSheetBrief(canvas, projectBrief, layout, labelFill, layoutName);
    }

    private static ProjectUpdateSheetLayout ResolveProjectUpdateSheetLayout(
        IReadOnlyList<UpdateSheetResolvedRow> rows,
        string? projectBrief)
    {
        const double contentY = 1.08;
        const double contentX = .50;
        const double contentWidth = 12.33;
        const double contentBottom = 6.93;
        const double gap = .16;

        // The user selects information, not a visual template. Choose a stable layout family
        // automatically, while promoting unusually tall tables to the detailed geometry.
        var naturalTableHeight = rows.Sum(row => row.Height);
        var variant = rows.Count <= 2 && naturalTableHeight <= 1.55
            ? ProjectUpdateSheetLayoutVariant.Compact
            : rows.Count >= 6 || naturalTableHeight > 3.05
                ? ProjectUpdateSheetLayoutVariant.Detailed
                : ProjectUpdateSheetLayoutVariant.Standard;
        var rowHeights = BuildProjectUpdateRowHeights(rows, variant);
        var tableHeight = rowHeights.Sum();

        if (variant == ProjectUpdateSheetLayoutVariant.Compact)
        {
            var factsHeight = tableHeight;
            var lowerY = contentY + factsHeight + gap;
            var lowerHeight = contentBottom - lowerY;
            var photoWidth = CompactPhotoWidth(projectBrief);
            var briefX = contentX + photoWidth + gap;
            var briefWidth = contentWidth - photoWidth - gap;

            return new ProjectUpdateSheetLayout(
                variant,
                contentX,
                contentY,
                contentWidth,
                factsHeight,
                new[] { .38, 2.45, 9.50 },
                rowHeights,
                contentX,
                lowerY,
                photoWidth,
                lowerHeight,
                briefX,
                lowerY,
                briefWidth,
                lowerHeight);
        }

        if (variant == ProjectUpdateSheetLayoutVariant.Detailed)
        {
            const double factsWidth = 7.18;
            const double photoX = 7.93;
            const double photoWidth = 4.90;
            var factsHeight = Math.Clamp(Math.Max(tableHeight, 3.15), 3.15, 4.30);
            var briefY = contentY + factsHeight + gap;

            return new ProjectUpdateSheetLayout(
                variant,
                contentX,
                contentY,
                factsWidth,
                factsHeight,
                new[] { .34, 2.15, 4.69 },
                rowHeights,
                photoX,
                contentY,
                photoWidth,
                factsHeight,
                contentX,
                briefY,
                contentWidth,
                contentBottom - briefY);
        }

        const double standardFactsWidth = 6.27;
        const double standardPhotoX = 7.02;
        const double standardPhotoWidth = 5.81;
        var standardFactsHeight = Math.Clamp(Math.Max(tableHeight, 2.85), 2.85, 3.70);
        var standardBriefY = contentY + standardFactsHeight + gap;
        return new ProjectUpdateSheetLayout(
            variant,
            contentX,
            contentY,
            standardFactsWidth,
            standardFactsHeight,
            new[] { .34, 1.88, 4.05 },
            rowHeights,
            standardPhotoX,
            contentY,
            standardPhotoWidth,
            standardFactsHeight,
            contentX,
            standardBriefY,
            contentWidth,
            contentBottom - standardBriefY);
    }

    private static void RenderProjectUpdateSheetPhoto(
        SlideCanvas canvas,
        ProjectBriefingPresentationProject project,
        ProjectUpdateSheetLayout layout,
        string layoutName)
    {
        var theme = canvas.Theme;
        canvas.AddRect(
            layout.PhotoX,
            layout.PhotoY,
            layout.PhotoWidth,
            layout.PhotoHeight,
            theme.Placeholder,
            theme.Border,
            .8,
            $"Project photograph frame - {layoutName}");

        if (project.CoverPhoto is { Length: > 0 })
        {
            const double inset = .08;
            var imageX = layout.PhotoX + inset;
            var imageY = layout.PhotoY + inset;
            var imageWidth = layout.PhotoWidth - (inset * 2);
            var imageHeight = layout.PhotoHeight - (inset * 2);
            var prepared = PrepareUpdateSheetPhoto(
                project.CoverPhoto,
                project.CoverPhotoContentType,
                imageWidth,
                imageHeight);

            canvas.AddImage(
                prepared.Content,
                prepared.ContentType,
                imageX,
                imageY,
                imageWidth,
                imageHeight,
                $"{project.ProjectName} photograph");
            return;
        }

        canvas.AddText(
            layout.PhotoX + .35,
            layout.PhotoY + Math.Max(.35, (layout.PhotoHeight - .55) / 2d),
            layout.PhotoWidth - .70,
            .55,
            "PHOTOGRAPH NOT AVAILABLE",
            10.2,
            theme.TextMuted,
            true,
            "ctr",
            name: "Photograph not available");
    }

    private static UpdateSheetPreparedPhoto PrepareUpdateSheetPhoto(
        byte[] content,
        string? contentType,
        double targetWidth,
        double targetHeight)
    {
        try
        {
            using var image = Image.Load(content);
            var targetAspect = Math.Max(.1d, targetWidth / Math.Max(.1d, targetHeight));
            var sourceAspect = image.Width / (double)Math.Max(1, image.Height);

            SixLabors.ImageSharp.Rectangle crop;
            if (sourceAspect > targetAspect)
            {
                var cropWidth = Math.Max(1, (int)Math.Round(image.Height * targetAspect));
                crop = new SixLabors.ImageSharp.Rectangle(
                    Math.Max(0, (image.Width - cropWidth) / 2),
                    0,
                    Math.Min(cropWidth, image.Width),
                    image.Height);
            }
            else
            {
                var cropHeight = Math.Max(1, (int)Math.Round(image.Width / targetAspect));
                crop = new SixLabors.ImageSharp.Rectangle(
                    0,
                    Math.Max(0, (image.Height - cropHeight) / 2),
                    image.Width,
                    Math.Min(cropHeight, image.Height));
            }

            const int maximumPixelWidth = 1_600;
            const int maximumPixelHeight = 1_100;
            var outputWidth = maximumPixelWidth;
            var outputHeight = Math.Max(1, (int)Math.Round(outputWidth / targetAspect));
            if (outputHeight > maximumPixelHeight)
            {
                outputHeight = maximumPixelHeight;
                outputWidth = Math.Max(1, (int)Math.Round(outputHeight * targetAspect));
            }

            image.Mutate(context => context
                .Crop(crop)
                .Resize(outputWidth, outputHeight));

            using var stream = new MemoryStream();
            image.Save(stream, new PngEncoder());
            return new UpdateSheetPreparedPhoto(stream.ToArray(), "image/png");
        }
        catch
        {
            // The source photo has already passed the PRISM media pipeline. If an
            // unexpected decoder issue occurs, preserve generation and use the
            // original file rather than replacing it with a placeholder.
            return new UpdateSheetPreparedPhoto(content, contentType);
        }
    }

    private static void RenderProjectUpdateSheetBrief(
        SlideCanvas canvas,
        string projectBrief,
        ProjectUpdateSheetLayout layout,
        string labelFill,
        string layoutName)
    {
        var theme = canvas.Theme;
        canvas.AddRect(
            layout.BriefX,
            layout.BriefY,
            layout.BriefWidth,
            layout.BriefHeight,
            theme.Surface,
            theme.Border,
            .8,
            $"Project brief panel - {layoutName}");
        canvas.AddRect(
            layout.BriefX,
            layout.BriefY,
            layout.BriefWidth,
            .38,
            labelFill,
            theme.Border,
            .8,
            "Project brief heading");
        canvas.AddText(
            layout.BriefX + .18,
            layout.BriefY + .04,
            layout.BriefWidth - .36,
            .29,
            "BRIEF OF THE PROJECT",
            11.6,
            theme.TextPrimary,
            true,
            "l",
            name: "Project brief heading text");
        canvas.AddRichTextBox(
            layout.BriefX + .19,
            layout.BriefY + .48,
            layout.BriefWidth - .38,
            layout.BriefHeight - .62,
            BuildUpdateSheetBriefParagraphs(projectBrief, theme.TextPrimary, theme.TextMuted),
            "Project brief",
            verticalAnchor: "t",
            allowAutoFit: false,
            leftInset: .03,
            rightInset: .03,
            topInset: .01,
            bottomInset: .01);
    }

    private static void RenderProjectUpdateSheetBriefContinuation(
        SlideCanvas canvas,
        ProjectBriefingPresentationProject project,
        string projectBrief,
        int continuationNumber)
    {
        var theme = canvas.Theme;
        AddProjectSlideHeader(
            canvas,
            project.ProjectName,
            subtitle: null,
            variant: ProjectSlideHeaderVariant.ProjectUpdateSheet);

        const double x = .50;
        const double y = 1.08;
        const double width = 12.33;
        const double height = 5.85;
        canvas.AddRect(
            x,
            y,
            width,
            height,
            theme.Surface,
            theme.Border,
            .8,
            $"Project brief continuation panel {continuationNumber}");
        canvas.AddRect(
            x,
            y,
            width,
            .42,
            theme.ProjectUpdateLabelFill,
            theme.Border,
            .8,
            $"Project brief continuation heading {continuationNumber}");
        canvas.AddText(
            x + .22,
            y + .05,
            width - .44,
            .31,
            "BRIEF OF THE PROJECT — CONTINUED",
            12.0,
            theme.TextPrimary,
            true,
            "l",
            name: $"Project brief continuation heading text {continuationNumber}");
        canvas.AddRichTextBox(
            x + .26,
            y + .58,
            width - .52,
            height - .78,
            BuildUpdateSheetBriefParagraphs(projectBrief, theme.TextPrimary, theme.TextMuted),
            $"Project brief continuation {continuationNumber}",
            verticalAnchor: "t",
            allowAutoFit: false,
            leftInset: .04,
            rightInset: .04,
            topInset: .02,
            bottomInset: .02);
    }

    private static IReadOnlyList<string> SplitUpdateSheetBrief(
        string? projectBrief,
        double firstSlideWidth,
        double firstSlideHeight)
    {
        var normalized = NormalizeUpdateSheetBrief(projectBrief);
        if (IsMissingUpdateSheetBrief(normalized))
        {
            return new[] { normalized };
        }

        var firstCapacity = EstimateUpdateSheetBriefCapacity(firstSlideWidth, firstSlideHeight);
        const double continuationWidth = 11.81;
        const double continuationHeight = 5.07;
        var continuationCapacity = EstimateUpdateSheetBriefCapacity(continuationWidth, continuationHeight);

        var chunks = new List<string>();
        var remaining = normalized;
        var capacity = firstCapacity;
        while (remaining.Length > capacity)
        {
            var splitAt = FindUpdateSheetBriefSplit(remaining, capacity);
            chunks.Add(remaining[..splitAt].Trim());
            remaining = remaining[splitAt..].TrimStart();
            capacity = continuationCapacity;
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            chunks.Add(remaining.Trim());
        }

        return chunks.Count > 0 ? chunks : new[] { normalized };
    }

    private static int EstimateUpdateSheetBriefCapacity(double width, double height)
    {
        const double minimumFontSize = 12.0;
        const double lineSpacingPoints = 14.8;
        var charactersPerLine = Math.Max(38, (int)Math.Floor((width * 72d) / (minimumFontSize * .54d)));
        var availableLines = Math.Max(3, (int)Math.Floor((height * 72d) / lineSpacingPoints));
        return Math.Max(300, (int)Math.Floor(charactersPerLine * availableLines * .84d));
    }

    private static int FindUpdateSheetBriefSplit(string value, int capacity)
    {
        var minimumAcceptable = Math.Max(1, (int)Math.Floor(capacity * .58d));
        var boundedCapacity = Math.Min(capacity, value.Length - 1);

        var paragraphBreak = value.LastIndexOf("\n\n", boundedCapacity, StringComparison.Ordinal);
        if (paragraphBreak >= minimumAcceptable)
        {
            return paragraphBreak + 2;
        }

        for (var index = boundedCapacity; index >= minimumAcceptable; index--)
        {
            if (index + 1 < value.Length
                && (value[index] == '.' || value[index] == '?' || value[index] == '!')
                && char.IsWhiteSpace(value[index + 1]))
            {
                return index + 1;
            }
        }

        var wordBreak = value.LastIndexOf(' ', boundedCapacity);
        return wordBreak >= minimumAcceptable ? wordBreak + 1 : boundedCapacity;
    }

    private static string NormalizeUpdateSheetBrief(string? projectBrief)
        => string.IsNullOrWhiteSpace(projectBrief)
            ? "Project brief not recorded."
            : projectBrief
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Trim();

    private static bool IsMissingUpdateSheetBrief(string value)
        => string.IsNullOrWhiteSpace(value)
            || string.Equals(value.Trim(), "Project brief not recorded.", StringComparison.OrdinalIgnoreCase);

    private static double CompactPhotoWidth(string? projectBrief)
    {
        _ = projectBrief;
        // Compact sheets intentionally retain an approximately 50:50 visual split.
        // Long briefs continue on a dedicated narrative slide rather than shrinking
        // the photograph or forcing unreadably small text.
        return 6.30;
    }

    private static string UpdateSheetLayoutName(ProjectUpdateSheetLayoutVariant variant)
        => variant switch
        {
            ProjectUpdateSheetLayoutVariant.Compact => "Compact",
            ProjectUpdateSheetLayoutVariant.Detailed => "Detailed",
            _ => "Standard"
        };

    private static IReadOnlyList<UpdateSheetResolvedRow> ResolveProjectUpdateRows(
        ProjectBriefingPresentationProject project,
        ProjectBriefingUpdateSheetOptions options,
        string text,
        string muted)
    {
        var status = UpdateSheetStatus(project.ExternalStatus);
        var arppDetails = BuildArppDetails(project);
        var supplyOrder = BuildSupplyOrderDisplay(project.SupplyOrderDate, project.JdpNames);
        var milestone = ResolveMilestoneRow(project);
        var all = options.Rows
            .Select(row => row switch
            {
                ProjectBriefingUpdateSheetRow.ProjectCost => new UpdateSheetResolvedRow(
                    row,
                    "Project Cost",
                    project.CostRd.IsAvailable ? project.CostRd.DisplayValue : "Not recorded",
                    project.CostRd.IsAvailable,
                    KeepWhenBlank: false,
                    FontSize: 10.8,
                    TextColor: project.CostRd.IsAvailable ? text : muted,
                    Height: .46),
                ProjectBriefingUpdateSheetRow.ArppPppNumber => new UpdateSheetResolvedRow(
                    row,
                    "ARPP/PPP Number",
                    project.ArppPppNumberApplicable ? DisplayOrNotRecorded(project.ArppReference) : string.Empty,
                    project.ArppPppNumberApplicable && IsRecorded(project.ArppReference),
                    KeepWhenBlank: false,
                    FontSize: 10.2,
                    TextColor: IsRecorded(project.ArppReference) ? text : muted,
                    Height: .46),
                ProjectBriefingUpdateSheetRow.FundingAuthority => new UpdateSheetResolvedRow(
                    row,
                    "Fund, DFPDS Sch and CFA",
                    arppDetails,
                    HasAnyArppDetail(project),
                    KeepWhenBlank: false,
                    FontSize: 9.4,
                    TextColor: HasAnyArppDetail(project) ? text : muted,
                    Height: .64),
                ProjectBriefingUpdateSheetRow.AonDate => new UpdateSheetResolvedRow(
                    row,
                    "AoN Date",
                    FormatUpdateDate(project.AonDate),
                    project.AonDate.HasValue,
                    KeepWhenBlank: false,
                    FontSize: 10.5,
                    TextColor: project.AonDate.HasValue ? text : muted,
                    Height: .46),
                ProjectBriefingUpdateSheetRow.SupplyOrder => new UpdateSheetResolvedRow(
                    row,
                    "SO Date and Name of Firm",
                    supplyOrder,
                    HasAnySupplyOrderDetail(project.SupplyOrderDate, project.JdpNames),
                    KeepWhenBlank: false,
                    FontSize: 9.4,
                    TextColor: HasAnySupplyOrderDetail(project.SupplyOrderDate, project.JdpNames) ? text : muted,
                    Height: .60),
                ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus => new UpdateSheetResolvedRow(
                    row,
                    milestone.Label,
                    milestone.Value,
                    milestone.HasRecordedValue,
                    KeepWhenBlank: true,
                    FontSize: 10.5,
                    TextColor: milestone.HasRecordedValue ? text : muted,
                    Height: .46),
                ProjectBriefingUpdateSheetRow.PresentStatus => new UpdateSheetResolvedRow(
                    row,
                    "Present Status",
                    status,
                    !string.Equals(status, "Not recorded", StringComparison.Ordinal),
                    KeepWhenBlank: false,
                    FontSize: UpdateSheetStatusFontSize(status),
                    TextColor: string.Equals(status, "Not recorded", StringComparison.Ordinal) ? muted : text,
                    Height: UpdateSheetStatusRowHeight(status)),
                ProjectBriefingUpdateSheetRow.ProjectOfficer => new UpdateSheetResolvedRow(
                    row,
                    "Project Officer",
                    DisplayOrNotRecorded(project.ProjectOfficer),
                    IsRecorded(project.ProjectOfficer),
                    KeepWhenBlank: false,
                    FontSize: 10.2,
                    TextColor: IsRecorded(project.ProjectOfficer) ? text : muted,
                    Height: .46),
                ProjectBriefingUpdateSheetRow.LineDirectorate => new UpdateSheetResolvedRow(
                    row,
                    "Line Directorate",
                    DisplayOrNotRecorded(project.LineDirectorate),
                    IsRecorded(project.LineDirectorate),
                    KeepWhenBlank: false,
                    FontSize: 10.2,
                    TextColor: IsRecorded(project.LineDirectorate) ? text : muted,
                    Height: .46),
                _ => null
            })
            .Where(row => row is not null)
            .Select(row => row!)
            .ToArray();

        if (!options.HideEmptyValues)
        {
            return all;
        }

        var filtered = all.Where(row => row.HasRecordedValue || row.KeepWhenBlank).ToArray();
        return filtered.Length > 0 ? filtered : all.Take(1).ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<NativeTableCell>> BuildProjectUpdateTableRows(
        IReadOnlyList<UpdateSheetResolvedRow> rows,
        string text,
        string muted,
        string serialFill,
        string labelFill,
        string bodyFill)
        => rows.Select((row, index) => (IReadOnlyList<NativeTableCell>)new[]
        {
            Cell((index + 1).ToString(CultureInfo.InvariantCulture) + ".", 9.6, muted, false, "ctr", serialFill),
            Cell(row.Label, 10.3, text, false, "l", labelFill),
            Cell(row.Value, row.FontSize, row.TextColor, false, "l", bodyFill)
        }).ToArray();

    private static double[] BuildProjectUpdateRowHeights(
        IReadOnlyList<UpdateSheetResolvedRow> rows,
        ProjectUpdateSheetLayoutVariant variant)
    {
        if (rows.Count == 0) return new[] { .46 };

        var heights = rows.Select(row => row.Height).ToArray();
        var total = heights.Sum();
        var targetMinimum = variant switch
        {
            ProjectUpdateSheetLayoutVariant.Compact => rows.Count == 1 ? .72 : 1.20,
            ProjectUpdateSheetLayoutVariant.Detailed => 3.18,
            _ => rows.Count <= 4 ? 2.08 : 2.62
        };
        if (total >= targetMinimum) return heights;

        var maximumExtra = variant switch
        {
            ProjectUpdateSheetLayoutVariant.Compact => .30,
            ProjectUpdateSheetLayoutVariant.Detailed => .10,
            _ => .16
        };
        var extraPerRow = Math.Min(maximumExtra, (targetMinimum - total) / rows.Count);
        return heights.Select(height => height + extraPerRow).ToArray();
    }

    private static UpdateSheetMilestone ResolveMilestoneRow(ProjectBriefingPresentationProject project)
    {
        if (project.LifecycleStatus == ProjectLifecycleStatus.Completed)
        {
            return new UpdateSheetMilestone(
                "Completion Status",
                string.IsNullOrWhiteSpace(project.CompletionStatusDisplay)
                    ? "Project completed"
                    : project.CompletionStatusDisplay,
                HasRecordedValue: true);
        }

        if (project.LifecycleStatus == ProjectLifecycleStatus.Cancelled)
        {
            return new UpdateSheetMilestone("Project Status", "Project cancelled", HasRecordedValue: true);
        }

        var pdc = string.Equals(project.PresentStageCode, StageCodes.DEVP, StringComparison.OrdinalIgnoreCase)
            && project.DevelopmentPdcDate.HasValue
                ? project.DevelopmentPdcDate.Value.ToString("dd MMM yy", CultureInfo.InvariantCulture)
                : string.Empty;
        return new UpdateSheetMilestone("PDC Date", pdc, !string.IsNullOrWhiteSpace(pdc));
    }

    private static IReadOnlyList<RichTextParagraph> BuildUpdateSheetBriefParagraphs(
        string? projectBrief,
        string text,
        string muted)
    {
        var normalized = NormalizeUpdateSheetBrief(projectBrief);
        if (IsMissingUpdateSheetBrief(normalized))
        {
            return new[]
            {
                new RichTextParagraph(
                    new[] { new RichTextRun("Project brief not recorded.", 12.0, muted, Italic: true) },
                    LineSpacingPoints: 14.8)
            };
        }

        var typography = ResolveUpdateSheetBriefTypography(normalized);
        return normalized
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(paragraph => new RichTextParagraph(
                new[]
                {
                    new RichTextRun(
                        paragraph.Replace("\n", " ", StringComparison.Ordinal),
                        typography.BodyFontSize,
                        text)
                },
                SpaceAfterPoints: typography.SpaceAfterPoints,
                LineSpacingPoints: typography.LineSpacingPoints))
            .ToArray();
    }

    private static UpdateSheetBriefTypography ResolveUpdateSheetBriefTypography(string value)
    {
        var characters = value.Length;
        if (characters <= 560)
        {
            return new UpdateSheetBriefTypography(15.0, 18.8, 8.0);
        }

        if (characters <= 900)
        {
            return new UpdateSheetBriefTypography(13.4, 16.8, 6.0);
        }

        return new UpdateSheetBriefTypography(12.0, 14.8, 4.5);
    }

    private static string BuildArppDetails(ProjectBriefingPresentationProject project)
        => string.Join("\n", new[]
        {
            FieldValue("Fund", project.Fund),
            FieldValue("DFPDS", project.DfpdsSchedule),
            FieldValue("CFA", project.Cfa)
        });

    private static string BuildSupplyOrderDisplay(
        DateOnly? supplyOrderDate,
        IReadOnlyList<string> jdpNames)
    {
        var firms = jdpNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!supplyOrderDate.HasValue && firms.Length == 0)
        {
            return "Not recorded";
        }

        return string.Join("\n", new[]
        {
            $"SO Date: {(supplyOrderDate.HasValue ? supplyOrderDate.Value.ToString("dd MMM yy", CultureInfo.InvariantCulture) : "Not recorded")}",
            $"Firm: {(firms.Length > 0 ? string.Join("; ", firms) : "Not recorded")}"
        });
    }

    private static string UpdateSheetStatus(string? value)
        => string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "No external status recorded", StringComparison.OrdinalIgnoreCase)
                ? "Not recorded"
                : value.Trim();

    private static bool HasAnyArppDetail(ProjectBriefingPresentationProject project)
        => IsRecorded(project.Fund)
            || IsRecorded(project.DfpdsSchedule)
            || IsRecorded(project.Cfa);

    private static bool HasAnySupplyOrderDetail(
        DateOnly? supplyOrderDate,
        IReadOnlyList<string> jdpNames)
        => supplyOrderDate.HasValue
            || jdpNames.Any(IsRecorded);

    private static bool IsRecorded(string? value)
        => !string.IsNullOrWhiteSpace(value);

    private static string FieldValue(string label, string? value)
        => $"{label}: {DisplayOrNotRecorded(value)}";

    private static string DisplayOrNotRecorded(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value.Trim();

    private static string FormatUpdateDate(DateOnly? value)
        => value.HasValue ? value.Value.ToString("dd MMM yy", CultureInfo.InvariantCulture) : "Not recorded";

    private static double UpdateSheetTitleFontSize(string title)
        => title.Length switch
        {
            <= 52 => 18.2,
            <= 72 => 16.8,
            <= 94 => 15.2,
            <= 118 => 13.8,
            _ => 12.6
        };

    private static double UpdateSheetStatusFontSize(string status)
        => status.Length switch
        {
            <= 95 => 10.4,
            <= 165 => 10.0,
            <= 250 => 9.6,
            _ => 9.2
        };

    private static double UpdateSheetStatusRowHeight(string status)
        => status.Length switch
        {
            <= 95 => .56,
            <= 165 => .66,
            <= 250 => .78,
            _ => .92
        };

    private enum ProjectUpdateSheetLayoutVariant
    {
        Compact,
        Standard,
        Detailed
    }

    private sealed record ProjectUpdateSheetLayout(
        ProjectUpdateSheetLayoutVariant Variant,
        double FactsX,
        double FactsY,
        double FactsWidth,
        double FactsHeight,
        IReadOnlyList<double> TableColumnWidths,
        IReadOnlyList<double> RowHeights,
        double PhotoX,
        double PhotoY,
        double PhotoWidth,
        double PhotoHeight,
        double BriefX,
        double BriefY,
        double BriefWidth,
        double BriefHeight);

    private sealed record UpdateSheetResolvedRow(
        ProjectBriefingUpdateSheetRow Key,
        string Label,
        string Value,
        bool HasRecordedValue,
        bool KeepWhenBlank,
        double FontSize,
        string TextColor,
        double Height);

    private sealed record UpdateSheetPreparedPhoto(byte[] Content, string? ContentType);

    private sealed record UpdateSheetBriefTypography(
        double BodyFontSize,
        double LineSpacingPoints,
        double SpaceAfterPoints);

    private sealed record UpdateSheetMilestone(string Label, string Value, bool HasRecordedValue);
}

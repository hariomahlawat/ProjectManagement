using System.Globalization;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private static List<SlidePlan> BuildProjectUpdateSheetPlans(ProjectBriefingPresentationData data)
    {
        var plans = new List<SlidePlan>();
        if (data.IncludeCoverSlide)
        {
            plans.Add(new SlidePlan(SlidePlanKind.Cover, canvas => RenderCover(canvas, data)));
        }
        if (data.IncludePortfolioSummarySlide)
        {
            plans.Add(new SlidePlan(SlidePlanKind.Summary, canvas => RenderPortfolioSummary(canvas, data)));
        }

        foreach (var project in OrderProjects(data.Projects))
        {
            var capturedProject = project;
            plans.Add(new SlidePlan(SlidePlanKind.Project, canvas =>
                RenderProjectUpdateSheet(canvas, data, capturedProject)));
        }

        return plans;
    }

    private static void RenderProjectUpdateSheet(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
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
        var layout = ResolveProjectUpdateSheetLayout(rows, project.ProjectBrief);
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
        RenderProjectUpdateSheetBrief(canvas, project, layout, labelFill, layoutName);
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
            const double inset = .10;
            var maximumWidth = layout.PhotoWidth - (inset * 2);
            var maximumHeight = layout.PhotoHeight - (inset * 2);
            var imageWidth = Math.Min(maximumWidth, maximumHeight * 16d / 9d);
            var imageHeight = imageWidth * 9d / 16d;
            canvas.AddImage(
                project.CoverPhoto,
                project.CoverPhotoContentType,
                layout.PhotoX + ((layout.PhotoWidth - imageWidth) / 2d),
                layout.PhotoY + ((layout.PhotoHeight - imageHeight) / 2d),
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
            10.5,
            theme.TextMuted,
            true,
            "ctr",
            name: "Photograph not available");
    }

    private static void RenderProjectUpdateSheetBrief(
        SlideCanvas canvas,
        ProjectBriefingPresentationProject project,
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
            .31,
            labelFill,
            theme.Border,
            .8,
            "Project brief heading");
        canvas.AddText(
            layout.BriefX + .18,
            layout.BriefY + .02,
            layout.BriefWidth - .36,
            .26,
            "BRIEF OF THE PROJECT",
            10.2,
            theme.TextPrimary,
            true,
            "l",
            name: "Project brief heading text");
        canvas.AddRichTextBox(
            layout.BriefX + .16,
            layout.BriefY + .39,
            layout.BriefWidth - .32,
            layout.BriefHeight - .49,
            BuildUpdateSheetBriefParagraphs(project.ProjectBrief, theme.TextPrimary, theme.TextMuted),
            "Project brief",
            verticalAnchor: "t",
            allowAutoFit: true,
            leftInset: .03,
            rightInset: .03,
            topInset: .01,
            bottomInset: .01);
    }

    private static double CompactPhotoWidth(string? projectBrief)
    {
        var length = string.IsNullOrWhiteSpace(projectBrief)
            ? 0
            : string.Join(" ", projectBrief.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Length;
        return length switch
        {
            <= 550 => 6.45,
            <= 900 => 6.00,
            _ => 5.55
        };
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
                    FontSize: 9.2,
                    TextColor: project.CostRd.IsAvailable ? text : muted,
                    Height: .38),
                ProjectBriefingUpdateSheetRow.ArppPppNumber => new UpdateSheetResolvedRow(
                    row,
                    "ARPP/PPP Number",
                    project.ArppPppNumberApplicable ? DisplayOrNotRecorded(project.ArppReference) : string.Empty,
                    project.ArppPppNumberApplicable && IsRecorded(project.ArppReference),
                    KeepWhenBlank: false,
                    FontSize: 8.8,
                    TextColor: IsRecorded(project.ArppReference) ? text : muted,
                    Height: .40),
                ProjectBriefingUpdateSheetRow.FundingAuthority => new UpdateSheetResolvedRow(
                    row,
                    "Fund, DFPDS Sch and CFA",
                    arppDetails,
                    HasAnyArppDetail(project),
                    KeepWhenBlank: false,
                    FontSize: 8.0,
                    TextColor: HasAnyArppDetail(project) ? text : muted,
                    Height: .58),
                ProjectBriefingUpdateSheetRow.AonDate => new UpdateSheetResolvedRow(
                    row,
                    "AoN Date",
                    FormatUpdateDate(project.AonDate),
                    project.AonDate.HasValue,
                    KeepWhenBlank: false,
                    FontSize: 9.0,
                    TextColor: project.AonDate.HasValue ? text : muted,
                    Height: .38),
                ProjectBriefingUpdateSheetRow.SupplyOrder => new UpdateSheetResolvedRow(
                    row,
                    "SO Date and Name of Firm",
                    supplyOrder,
                    HasAnySupplyOrderDetail(project.SupplyOrderDate, project.JdpNames),
                    KeepWhenBlank: false,
                    FontSize: 8.1,
                    TextColor: HasAnySupplyOrderDetail(project.SupplyOrderDate, project.JdpNames) ? text : muted,
                    Height: .54),
                ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus => new UpdateSheetResolvedRow(
                    row,
                    milestone.Label,
                    milestone.Value,
                    milestone.HasRecordedValue,
                    KeepWhenBlank: true,
                    FontSize: 9.0,
                    TextColor: milestone.HasRecordedValue ? text : muted,
                    Height: .38),
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
                    FontSize: 8.8,
                    TextColor: IsRecorded(project.ProjectOfficer) ? text : muted,
                    Height: .38),
                ProjectBriefingUpdateSheetRow.LineDirectorate => new UpdateSheetResolvedRow(
                    row,
                    "Line Directorate",
                    DisplayOrNotRecorded(project.LineDirectorate),
                    IsRecorded(project.LineDirectorate),
                    KeepWhenBlank: false,
                    FontSize: 8.8,
                    TextColor: IsRecorded(project.LineDirectorate) ? text : muted,
                    Height: .38),
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
            Cell((index + 1).ToString(CultureInfo.InvariantCulture) + ".", 8.8, muted, false, "ctr", serialFill),
            Cell(row.Label, 8.7, text, false, "l", labelFill),
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
            ProjectUpdateSheetLayoutVariant.Compact => rows.Count == 1 ? .64 : 1.08,
            ProjectUpdateSheetLayoutVariant.Detailed => 3.10,
            _ => rows.Count <= 4 ? 2.00 : 2.55
        };
        if (total >= targetMinimum) return heights;

        var maximumExtra = variant switch
        {
            ProjectUpdateSheetLayoutVariant.Compact => .28,
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
        var isMissing = string.IsNullOrWhiteSpace(projectBrief)
            || string.Equals(projectBrief.Trim(), "Project brief not recorded.", StringComparison.OrdinalIgnoreCase);
        if (isMissing)
        {
            return new[]
            {
                new RichTextParagraph(
                    new[] { new RichTextRun("Project brief not recorded.", 11.2, muted, Italic: true) },
                    LineSpacingPoints: 13.2)
            };
        }

        var normalized = projectBrief!
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var typography = ProjectBriefingNarrativeTypography.ResolveUpdateSheetBrief(normalized);

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
            <= 95 => 8.2,
            <= 165 => 7.9,
            <= 250 => 7.6,
            _ => 7.4
        };

    private static double UpdateSheetStatusRowHeight(string status)
        => status.Length switch
        {
            <= 95 => .48,
            <= 165 => .57,
            <= 250 => .68,
            _ => .80
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

    private sealed record UpdateSheetMilestone(string Label, string Value, bool HasRecordedValue);
}

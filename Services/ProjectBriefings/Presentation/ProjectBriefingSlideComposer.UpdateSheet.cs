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
        var rowHeights = BuildProjectUpdateRowHeights(rows);
        var tableHeight = rowHeights.Sum();
        var factsZoneHeight = Math.Clamp(Math.Max(tableHeight, 2.85), 2.85, 4.35);

        const double contentY = 1.08;
        const double factsX = .50;
        const double factsWidth = 6.27;
        canvas.AddRect(
            factsX,
            contentY,
            factsWidth,
            factsZoneHeight,
            theme.Surface,
            theme.Border,
            .8,
            "Project facts panel");
        canvas.AddNativeTable(
            factsX,
            contentY,
            new[] { .34, 1.88, 4.05 },
            rowHeights,
            BuildProjectUpdateTableRows(rows, theme.TextPrimary, theme.TextMuted, serialFill, labelFill, bodyFill),
            "Project update facts table");

        const double photoX = 7.02;
        const double photoWidth = 5.81;
        canvas.AddRect(
            photoX,
            contentY,
            photoWidth,
            factsZoneHeight,
            theme.Placeholder,
            theme.Border,
            .8,
            "Project photograph frame");
        if (project.CoverPhoto is { Length: > 0 })
        {
            const double inset = .10;
            var maximumWidth = photoWidth - (inset * 2);
            var maximumHeight = factsZoneHeight - (inset * 2);
            var imageWidth = Math.Min(maximumWidth, maximumHeight * 16d / 9d);
            var imageHeight = imageWidth * 9d / 16d;
            canvas.AddImage(
                project.CoverPhoto,
                project.CoverPhotoContentType,
                photoX + ((photoWidth - imageWidth) / 2d),
                contentY + ((factsZoneHeight - imageHeight) / 2d),
                imageWidth,
                imageHeight,
                $"{project.ProjectName} photograph");
        }
        else
        {
            canvas.AddText(
                photoX + .35,
                contentY + Math.Max(.35, (factsZoneHeight - .55) / 2d),
                photoWidth - .70,
                .55,
                "PHOTOGRAPH NOT AVAILABLE",
                10.5,
                theme.TextMuted,
                true,
                "ctr",
                name: "Photograph not available");
        }

        var briefY = contentY + factsZoneHeight + .16;
        var briefHeight = 6.93 - briefY;
        canvas.AddRect(.50, briefY, 12.33, briefHeight, theme.Surface, theme.Border, .8, "Project brief panel");
        canvas.AddRect(.50, briefY, 12.33, .31, labelFill, theme.Border, .8, "Project brief heading");
        canvas.AddText(
            .68,
            briefY + .02,
            11.95,
            .26,
            "BRIEF OF THE PROJECT",
            10.2,
            theme.TextPrimary,
            true,
            "l",
            name: "Project brief heading text");
        canvas.AddRichTextBox(
            .66,
            briefY + .39,
            12.02,
            briefHeight - .49,
            BuildUpdateSheetBriefParagraphs(project.ProjectBrief, theme.TextPrimary, theme.TextMuted),
            "Project brief",
            verticalAnchor: "t",
            allowAutoFit: true,
            leftInset: .03,
            rightInset: .03,
            topInset: .01,
            bottomInset: .01);
    }

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

    private static double[] BuildProjectUpdateRowHeights(IReadOnlyList<UpdateSheetResolvedRow> rows)
    {
        if (rows.Count == 0) return new[] { .46 };

        var heights = rows.Select(row => row.Height).ToArray();
        var total = heights.Sum();
        var targetMinimum = rows.Count switch
        {
            <= 4 => 2.00,
            <= 6 => 2.55,
            _ => total
        };
        if (total >= targetMinimum) return heights;

        var extraPerRow = Math.Min(.16, (targetMinimum - total) / rows.Count);
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

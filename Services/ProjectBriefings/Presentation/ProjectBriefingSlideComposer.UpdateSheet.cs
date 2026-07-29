using System.Globalization;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private const string UpdateSheetAccent = "8F0D21";

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
                RenderProjectUpdateSheet(canvas, capturedProject)));
        }

        return plans;
    }

    private static void RenderProjectUpdateSheet(
        SlideCanvas canvas,
        ProjectBriefingPresentationProject project)
    {
        const string white = "FFFFFF";
        const string text = "172033";
        const string muted = "5C667A";
        const string border = "B8C0CC";
        const string labelFill = "EDF1F6";
        const string serialFill = "F7F8FA";
        const string bodyFill = "FFFFFF";

        canvas.AddRect(0, 0, SlideWidth, SlideHeight, white);
        canvas.AddRect(0, 0, SlideWidth, .065, UpdateSheetAccent, name: "Project sheet top accent");
        canvas.AddBrandingImages(HeaderVariant.ProjectUpdateSheet);

        var titleX = canvas.ShowBranding ? 1.16 : .62;
        var titleWidth = canvas.ShowBranding ? 11.01 : 12.09;
        canvas.AddRichTextBox(
            titleX,
            .15,
            titleWidth,
            .64,
            new[]
            {
                new RichTextParagraph(
                    new[]
                    {
                        new RichTextRun(
                            project.ProjectName,
                            UpdateSheetTitleFontSize(project.ProjectName),
                            UpdateSheetAccent,
                            Bold: true)
                    },
                    Align: "ctr",
                    LineSpacingPoints: UpdateSheetTitleFontSize(project.ProjectName) * 1.05)
            },
            "Project name",
            verticalAnchor: "ctr",
            allowAutoFit: true,
            leftInset: .03,
            rightInset: .03,
            topInset: 0,
            bottomInset: 0);
        canvas.AddLine(.55, .92, 12.78, .92, border, .65);

        var status = UpdateSheetStatus(project.ExternalStatus);
        var arppDetails = BuildArppDetails(project);
        var supplyOrder = BuildSupplyOrderDisplay(project.SupplyOrderDate, project.JdpNames);
        var rows = BuildProjectUpdateRows(
            project,
            status,
            arppDetails,
            supplyOrder,
            text,
            muted,
            serialFill,
            labelFill,
            bodyFill);
        var heights = BuildProjectUpdateRowHeights(status);
        var factsHeight = heights.Sum();

        const double contentY = 1.08;
        canvas.AddNativeTable(
            .50,
            contentY,
            new[] { .34, 1.78, 4.15 },
            heights,
            rows,
            "Project update facts table");

        const double photoX = 7.02;
        const double photoWidth = 5.81;
        canvas.AddRect(
            photoX,
            contentY,
            photoWidth,
            factsHeight,
            "F7F8FA",
            border,
            .8,
            "Project photograph frame");
        if (project.CoverPhoto is { Length: > 0 })
        {
            const double inset = .10;
            var maximumWidth = photoWidth - (inset * 2);
            var maximumHeight = factsHeight - (inset * 2);
            var imageWidth = Math.Min(maximumWidth, maximumHeight * 16d / 9d);
            var imageHeight = imageWidth * 9d / 16d;
            canvas.AddImage(
                project.CoverPhoto,
                project.CoverPhotoContentType,
                photoX + ((photoWidth - imageWidth) / 2d),
                contentY + ((factsHeight - imageHeight) / 2d),
                imageWidth,
                imageHeight,
                $"{project.ProjectName} photograph");
        }
        else
        {
            canvas.AddText(
                photoX + .35,
                contentY + Math.Max(.35, (factsHeight - .55) / 2d),
                photoWidth - .70,
                .55,
                "PHOTOGRAPH NOT AVAILABLE",
                10.5,
                muted,
                true,
                "ctr",
                name: "Photograph not available");
        }

        var briefY = contentY + factsHeight + .16;
        var briefHeight = 6.93 - briefY;
        canvas.AddRect(.50, briefY, 12.33, briefHeight, white, border, .8, "Project brief panel");
        canvas.AddRect(.50, briefY, 12.33, .31, labelFill, border, .8, "Project brief heading");
        canvas.AddText(
            .68,
            briefY + .02,
            11.95,
            .26,
            "BRIEF OF THE PROJECT",
            10.2,
            text,
            true,
            "l",
            name: "Project brief heading text");
        canvas.AddRichTextBox(
            .66,
            briefY + .39,
            12.02,
            briefHeight - .49,
            BuildUpdateSheetBriefParagraphs(project.ProjectBrief, text, muted),
            "Project brief",
            verticalAnchor: "t",
            allowAutoFit: true,
            leftInset: .03,
            rightInset: .03,
            topInset: .01,
            bottomInset: .01);
    }

    private static IReadOnlyList<IReadOnlyList<NativeTableCell>> BuildProjectUpdateRows(
        ProjectBriefingPresentationProject project,
        string status,
        string arppDetails,
        string supplyOrder,
        string text,
        string muted,
        string serialFill,
        string labelFill,
        string bodyFill)
    {
        var pdc = string.Equals(project.PresentStageCode, StageCodes.DEVP, StringComparison.OrdinalIgnoreCase)
            ? FormatDate(project.DevelopmentPdcDate)
            : string.Empty;

        var values = new (string Label, string Value, double FontSize, string Color)[]
        {
            ("Name of Project", DisplayOrNotRecorded(project.ProjectName), 9.2, text),
            ("Project Cost", project.CostRd.IsAvailable ? project.CostRd.DisplayValue : "Not recorded", 9.2, project.CostRd.IsAvailable ? text : muted),
            ("ARPP/PPP Number", project.ArppPppNumberApplicable ? DisplayOrNotRecorded(project.ArppReference) : string.Empty, 8.8, IsRecorded(project.ArppReference) ? text : muted),
            ("Fund, DFPDS Sch and CFA", arppDetails, 8.0, HasAnyArppDetail(project) ? text : muted),
            ("AoN Date", FormatDate(project.AonDate), 9.0, project.AonDate.HasValue ? text : muted),
            ("SO Date and Name of Firm", supplyOrder, 8.1, HasAnySupplyOrderDetail(project.SupplyOrderDate, project.JdpNames) ? text : muted),
            ("PDC Date", pdc, 9.0, string.IsNullOrWhiteSpace(pdc) ? muted : text),
            ("Present Status", status, UpdateSheetStatusFontSize(status), string.Equals(status, "Not recorded", StringComparison.Ordinal) ? muted : text),
            ("Project Officer", DisplayOrNotRecorded(project.ProjectOfficer), 8.8, IsRecorded(project.ProjectOfficer) ? text : muted),
            ("Line Directorate", DisplayOrNotRecorded(project.LineDirectorate), 8.8, IsRecorded(project.LineDirectorate) ? text : muted)
        };

        return values.Select((row, index) => (IReadOnlyList<NativeTableCell>)new[]
        {
            Cell((index + 1).ToString(CultureInfo.InvariantCulture) + ".", 8.8, muted, false, "ctr", serialFill),
            Cell(row.Label, 8.7, text, false, "l", labelFill),
            Cell(row.Value, row.FontSize, row.Color, false, "l", bodyFill)
        }).ToArray();
    }

    private static double[] BuildProjectUpdateRowHeights(string status)
        => new[]
        {
            .31,
            .30,
            .32,
            .58,
            .30,
            .48,
            .30,
            UpdateSheetStatusRowHeight(status),
            .30,
            .30
        };

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
        var fontSize = UpdateSheetBriefFontSize(normalized.Length);
        var lineSpacing = Math.Max(fontSize + 1.5, 8.0);

        return normalized
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(paragraph => new RichTextParagraph(
                new[] { new RichTextRun(paragraph.Replace("\n", " ", StringComparison.Ordinal), fontSize, text) },
                SpaceAfterPoints: Math.Max(2.5, fontSize * .42),
                LineSpacingPoints: lineSpacing))
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
            $"SO Date: {(supplyOrderDate.HasValue ? FormatDate(supplyOrderDate) : "Not recorded")}",
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

    private static string FormatDate(DateOnly? value)
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

    private static double UpdateSheetBriefFontSize(int characterCount)
        => characterCount switch
        {
            <= 650 => 10.8,
            <= 1_000 => 9.8,
            <= 1_500 => 8.8,
            <= 2_100 => 7.8,
            _ => 6.8
        };
}

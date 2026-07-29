using System.Globalization;
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
                RenderProjectUpdateSheet(canvas, capturedProject)));
        }

        return plans;
    }

    private static void RenderProjectUpdateSheet(
        SlideCanvas canvas,
        ProjectBriefingPresentationProject project)
    {
        const string accent = "8F0D21";
        const string white = "FFFFFF";
        const string text = "172033";
        const string muted = "5C667A";
        const string border = "B8C0CC";
        const string labelFill = "EDF1F6";
        const string serialFill = "F7F8FA";
        const string bodyFill = "FFFFFF";

        canvas.AddRect(0, 0, SlideWidth, SlideHeight, white);
        canvas.AddRect(0, 0, SlideWidth, .07, accent, name: "Project sheet top accent");
        canvas.AddText(
            .55,
            .17,
            2.40,
            .17,
            "PROJECT UPDATE SHEET",
            7.8,
            accent,
            true,
            "l",
            name: "Project sheet label");
        canvas.AddText(
            .55,
            .36,
            12.23,
            .42,
            project.ProjectName,
            UpdateSheetTitleFontSize(project.ProjectName),
            text,
            true,
            "l",
            name: "Project name");
        canvas.AddLine(.55, .92, 12.78, .92, border, .65);

        var rows = BuildProjectUpdateRows(project, text, muted, serialFill, labelFill, bodyFill);
        var heights = new[] { .31, .31, .34, .46, .31, .50, .31, .48, .31, .31 };
        canvas.AddNativeTable(
            .50,
            1.08,
            new[] { .34, 1.78, 4.15 },
            heights,
            rows,
            "Project update facts table");

        const double photoX = 7.02;
        const double photoY = 1.08;
        const double photoWidth = 5.81;
        const double photoHeight = 3.64;
        canvas.AddRect(photoX, photoY, photoWidth, photoHeight, "F7F8FA", border, .8, "Project photograph frame");
        if (project.CoverPhoto is { Length: > 0 })
        {
            const double inset = .10;
            var maximumWidth = photoWidth - (inset * 2);
            var maximumHeight = photoHeight - (inset * 2);
            var imageWidth = Math.Min(maximumWidth, maximumHeight * 16d / 9d);
            var imageHeight = imageWidth * 9d / 16d;
            canvas.AddImage(
                project.CoverPhoto,
                project.CoverPhotoContentType,
                photoX + ((photoWidth - imageWidth) / 2d),
                photoY + ((photoHeight - imageHeight) / 2d),
                imageWidth,
                imageHeight,
                $"{project.ProjectName} photograph");
        }
        else
        {
            canvas.AddText(
                photoX + .35,
                photoY + 1.44,
                photoWidth - .70,
                .55,
                "PHOTOGRAPH NOT AVAILABLE",
                10.5,
                muted,
                true,
                "ctr",
                name: "Photograph not available");
        }

        const double briefY = 4.91;
        const double briefHeight = 2.02;
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
        string text,
        string muted,
        string serialFill,
        string labelFill,
        string bodyFill)
    {
        var pdc = string.Equals(project.PresentStageCode, StageCodes.DEVP, StringComparison.OrdinalIgnoreCase)
            ? FormatDate(project.DevelopmentPdcDate)
            : string.Empty;
        var status = string.IsNullOrWhiteSpace(project.ExternalStatus)
            || string.Equals(project.ExternalStatus, "No external status recorded", StringComparison.OrdinalIgnoreCase)
                ? "Not recorded"
                : project.ExternalStatus.Trim();
        var arppDetails = string.Join(" · ", new[]
        {
            FieldValue("Fund", project.Fund),
            FieldValue("DFPDS", project.DfpdsSchedule),
            FieldValue("CFA", project.Cfa)
        });
        var supplyOrder = BuildSupplyOrderDisplay(project.SupplyOrderDate, project.JdpNames);

        var values = new (string Label, string Value, double FontSize)[]
        {
            ("Name of Project", DisplayOrNotRecorded(project.ProjectName), 9.2),
            ("Project Cost", project.CostRd.IsAvailable ? project.CostRd.DisplayValue : "Not recorded", 9.2),
            ("ARPP/PPP Number", DisplayOrNotRecorded(project.ArppReference), 8.8),
            ("Fund, DFPDS Sch and CFA", arppDetails, 8.2),
            ("AoN Date", FormatDate(project.AonDate), 9.0),
            ("SO Date and Name of Firm", supplyOrder, 8.3),
            ("PDC Date", pdc, 9.0),
            ("Present Status", status, UpdateSheetStatusFontSize(status)),
            ("Project Officer", DisplayOrNotRecorded(project.ProjectOfficer), 8.8),
            ("Line Directorate", DisplayOrNotRecorded(project.LineDirectorate), 8.8)
        };

        return values.Select((row, index) => (IReadOnlyList<NativeTableCell>)new[]
        {
            Cell((index + 1).ToString(CultureInfo.InvariantCulture) + ".", 8.8, muted, false, "ctr", serialFill),
            Cell(row.Label, 8.7, text, false, "l", labelFill),
            Cell(row.Value, row.FontSize, text, false, "l", bodyFill)
        }).ToArray();
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

    private static string BuildSupplyOrderDisplay(
        DateOnly? supplyOrderDate,
        IReadOnlyList<string> jdpNames)
    {
        var hasDate = supplyOrderDate.HasValue;
        var firms = jdpNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!hasDate && firms.Length == 0)
        {
            return "Not recorded";
        }

        var lines = new List<string>(2)
        {
            hasDate ? FormatDate(supplyOrderDate) : "SO date: Not recorded",
            firms.Length > 0 ? string.Join("; ", firms) : "Firm: Not recorded"
        };
        return string.Join("\n", lines);
    }

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
            <= 72 => 16.6,
            <= 94 => 15.0,
            _ => 13.4
        };

    private static double UpdateSheetStatusFontSize(string status)
        => status.Length switch
        {
            <= 90 => 8.0,
            <= 150 => 7.4,
            <= 230 => 6.8,
            _ => 6.2
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

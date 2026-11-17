using System.Globalization;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.Imaging;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    // One formal sheet per project with slides of its own, with brief continuations.
    private static void AddProjectUpdateSheetPlans(
        List<SlidePlan> plans,
        ProjectBriefingPresentationData data,
        IReadOnlyList<ProjectBriefingPresentationProject> orderedProjects)
    {
        foreach (var project in orderedProjects.Where(project => !project.TableRowOnly))
        {
            var rows = ProjectBriefingUpdateSheetPlanner.ResolveRows(project, data.UpdateSheetOptions);
            var plan = ProjectBriefingUpdateSheetPlanner.Plan(
                rows,
                project.CoverPhoto is { Length: > 0 },
                project.ProjectBrief);
            var capturedProject = project;
            var capturedRows = rows;
            var capturedPlan = plan;

            plans.Add(new SlidePlan(SlidePlanKind.Project, canvas =>
                RenderProjectUpdateSheet(canvas, capturedProject, capturedRows, capturedPlan),
                ProjectBriefingSlideRole.ProjectUpdateSheet,
                capturedProject.ProjectId));

            for (var index = 1; index < plan.BriefPages.Count; index++)
            {
                var continuationPage = plan.BriefPages[index];
                var continuationNumber = index + 1;
                plans.Add(new SlidePlan(SlidePlanKind.Project, canvas =>
                    RenderProjectUpdateSheetBriefContinuation(
                        canvas,
                        capturedProject,
                        continuationPage,
                        continuationNumber),
                    ProjectBriefingSlideRole.ProjectUpdateSheetContinuation,
                    capturedProject.ProjectId));
            }
        }
    }

    private static void RenderProjectUpdateSheet(
        SlideCanvas canvas,
        ProjectBriefingPresentationProject project,
        IReadOnlyList<ProjectBriefingUpdateSheetPlanningRow> rows,
        ProjectBriefingUpdateSheetPlan plan)
    {
        var theme = canvas.Theme;
        var layoutName = UpdateSheetLayoutName(plan.Variant);

        AddProjectSlideHeader(
            canvas,
            project.ProjectName,
            subtitle: null,
            variant: ProjectSlideHeaderVariant.ProjectUpdateSheet);

        canvas.AddRect(
            plan.FactsX,
            plan.FactsY,
            plan.FactsWidth,
            plan.FactsHeight,
            theme.Surface,
            theme.Border,
            .8,
            $"Project facts panel - {layoutName}");
        canvas.AddNativeTable(
            plan.FactsX,
            plan.FactsY,
            plan.TableColumnWidths,
            plan.RowHeights,
            BuildProjectUpdateTableRows(rows, theme),
            $"Project update facts table - {layoutName}");

        if (plan.RenderPhotograph)
        {
            RenderProjectUpdateSheetPhoto(canvas, project, plan, layoutName);
        }

        RenderProjectUpdateSheetBrief(
            canvas,
            plan.BriefPages[0],
            plan,
            theme.ProjectUpdateLabelFill,
            layoutName);
    }

    private static void RenderProjectUpdateSheetPhoto(
        SlideCanvas canvas,
        ProjectBriefingPresentationProject project,
        ProjectBriefingUpdateSheetPlan plan,
        string layoutName)
    {
        if (project.CoverPhoto is not { Length: > 0 })
        {
            return;
        }

        var theme = canvas.Theme;
        canvas.AddRect(
            plan.PhotoX,
            plan.PhotoY,
            plan.PhotoWidth,
            plan.PhotoHeight,
            theme.Placeholder,
            theme.Border,
            .8,
            $"Project photograph frame - {layoutName}");

        const double inset = .08;
        var imageX = plan.PhotoX + inset;
        var imageY = plan.PhotoY + inset;
        var imageWidth = plan.PhotoWidth - (inset * 2);
        var imageHeight = plan.PhotoHeight - (inset * 2);
        // The whole photo in its frame, never cropped (owner, 17 Nov 2026).
        canvas.AddImageContained(
            project.CoverPhoto,
            imageX,
            imageY,
            imageWidth,
            imageHeight,
            $"{project.ProjectName} photograph",
            project.CoverPhotoContentType);
    }

    private static void RenderProjectUpdateSheetBrief(
        SlideCanvas canvas,
        ProjectBriefingUpdateSheetBriefPage page,
        ProjectBriefingUpdateSheetPlan plan,
        string labelFill,
        string layoutName)
    {
        var theme = canvas.Theme;
        canvas.AddRect(
            plan.BriefX,
            plan.BriefY,
            plan.BriefWidth,
            plan.BriefHeight,
            theme.Surface,
            theme.Border,
            .8,
            $"Project brief panel - {layoutName}");
        canvas.AddRect(
            plan.BriefX,
            plan.BriefY,
            plan.BriefWidth,
            .38,
            labelFill,
            theme.Border,
            .8,
            "Project brief heading");
        canvas.AddText(
            plan.BriefX + .18,
            plan.BriefY + .04,
            plan.BriefWidth - .36,
            .29,
            "BRIEF OF THE PROJECT",
            11.6,
            theme.TextPrimary,
            true,
            "l",
            name: "Project brief heading text");
        canvas.AddRichTextBox(
            plan.BriefX + .19,
            plan.BriefY + .48,
            plan.BriefWidth - .38,
            Math.Max(.20, plan.BriefHeight - .62),
            BuildUpdateSheetBriefParagraphs(page, theme.TextPrimary, theme.TextMuted),
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
        ProjectBriefingUpdateSheetBriefPage page,
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
            BuildUpdateSheetBriefParagraphs(page, theme.TextPrimary, theme.TextMuted),
            $"Project brief continuation {continuationNumber}",
            verticalAnchor: "t",
            allowAutoFit: false,
            leftInset: .04,
            rightInset: .04,
            topInset: .02,
            bottomInset: .02);
    }

    private static IReadOnlyList<IReadOnlyList<NativeTableCell>> BuildProjectUpdateTableRows(
        IReadOnlyList<ProjectBriefingUpdateSheetPlanningRow> rows,
        ProjectBriefingThemeDefinition theme)
        => rows.Select((row, index) => (IReadOnlyList<NativeTableCell>)new[]
        {
            new NativeTableCell(
                (index + 1).ToString(CultureInfo.InvariantCulture) + ".",
                9.4,
                theme.TextMuted,
                Bold: false,
                Align: "ctr",
                Fill: theme.SurfaceMuted,
                VerticalAnchor: "ctr",
                LeftMargin: .02,
                RightMargin: .02,
                TopMargin: .015,
                BottomMargin: .015),
            new NativeTableCell(
                row.Label,
                row.LabelFontSize,
                theme.TextPrimary,
                Bold: false,
                Align: "l",
                Fill: theme.ProjectUpdateLabelFill,
                VerticalAnchor: "ctr",
                LeftMargin: .055,
                RightMargin: .04,
                TopMargin: .015,
                BottomMargin: .015),
            new NativeTableCell(
                row.Value,
                row.FontSize,
                row.HasRecordedValue ? theme.TextPrimary : theme.TextMuted,
                Bold: false,
                Align: "l",
                Fill: theme.Surface,
                VerticalAnchor: row.Value.Contains('\n') ? "t" : "ctr",
                LeftMargin: .055,
                RightMargin: .045,
                TopMargin: .018,
                BottomMargin: .018)
        }).ToArray();

    private static IReadOnlyList<RichTextParagraph> BuildUpdateSheetBriefParagraphs(
        ProjectBriefingUpdateSheetBriefPage page,
        string text,
        string muted)
    {
        if (page.IsMissing)
        {
            return new[]
            {
                new RichTextParagraph(
                    new[]
                    {
                        new RichTextRun(
                            "Project brief not recorded.",
                            page.Typography.BodyFontSize,
                            muted,
                            Italic: true)
                    },
                    LineSpacingPoints: page.Typography.LineSpacingPoints)
            };
        }

        return page.Text
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(paragraph => new RichTextParagraph(
                new[]
                {
                    new RichTextRun(
                        paragraph.Replace("\n", " ", StringComparison.Ordinal),
                        page.Typography.BodyFontSize,
                        text)
                },
                SpaceAfterPoints: page.Typography.SpaceAfterPoints,
                LineSpacingPoints: page.Typography.LineSpacingPoints))
            .ToArray();
    }

    private static string UpdateSheetLayoutName(ProjectBriefingUpdateSheetLayoutVariant variant)
        => variant switch
        {
            ProjectBriefingUpdateSheetLayoutVariant.Compact => "Compact",
            ProjectBriefingUpdateSheetLayoutVariant.Detailed => "Detailed",
            ProjectBriefingUpdateSheetLayoutVariant.FactsFirst => "Facts first",
            ProjectBriefingUpdateSheetLayoutVariant.NoPhotograph => "No photograph",
            _ => "Standard"
        };

    private static double UpdateSheetTitleFontSize(string title)
        => title.Length switch
        {
            <= 52 => 18.2,
            <= 72 => 16.8,
            <= 94 => 15.2,
            <= 118 => 13.8,
            _ => 12.6
        };

}

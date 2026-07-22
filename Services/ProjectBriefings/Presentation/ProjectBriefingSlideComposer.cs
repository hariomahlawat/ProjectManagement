using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.AspNetCore.Hosting;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed class ProjectBriefingSlideComposer : IProjectBriefingSlideComposer
{
    private const double SlideWidth = 13.333333;
    private const double SlideHeight = 7.5;

    private const string Navy = "12223A";
    private const string DeepNavy = "0C1829";
    private const string Blue = "3367E8";
    private const string Teal = "238B8D";
    private const string Green = "2E8B57";
    private const string Amber = "D97706";
    private const string Red = "C83B3B";
    private const string LightBackground = "F4F7FB";
    private const string CardBackground = "FFFFFF";
    private const string Text = "111827";
    private const string Muted = "667085";
    private const string Border = "D8E0EA";
    private const string LightBlue = "EAF1FF";
    private const string LightGreen = "EAF7F0";
    private const string LightAmber = "FFF5E5";

    private readonly string _templatePath;
    private readonly string? _logoPath;

    public ProjectBriefingSlideComposer(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _templatePath = Path.Combine(
            environment.ContentRootPath,
            "Resources",
            "ProjectBriefing",
            "ProjectBriefingTemplate.pptx");

        var logoCandidate = Path.Combine(environment.WebRootPath, "img", "logos", "sdd.png");
        _logoPath = File.Exists(logoCandidate) ? logoCandidate : null;
    }

    public (byte[] Content, int SlideCount) Compose(ProjectBriefingPresentationData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!File.Exists(_templatePath))
        {
            throw new FileNotFoundException("Project briefing PowerPoint template was not found.", _templatePath);
        }

        var plans = BuildPlans(data);
        var templateBytes = File.ReadAllBytes(_templatePath);
        using var stream = new MemoryStream(templateBytes.Length + 4_000_000);
        stream.Write(templateBytes, 0, templateBytes.Length);
        stream.Position = 0;

        using (var document = PresentationDocument.Open(stream, true))
        {
            var presentationPart = document.PresentationPart
                ?? throw new InvalidOperationException("The PowerPoint template has no presentation part.");
            var layoutPart = FindBlankLayout(presentationPart)
                ?? throw new InvalidOperationException("The PowerPoint template has no slide layout.");

            RemoveTemplateSlides(presentationPart);
            var slideIdList = presentationPart.Presentation.SlideIdList;
            if (slideIdList is null)
            {
                slideIdList = new SlideIdList();
                presentationPart.Presentation.Append(slideIdList);
            }

            uint nextSlideId = 256;
            var logo = _logoPath is null ? null : File.ReadAllBytes(_logoPath);

            for (var index = 0; index < plans.Count; index++)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.AddPart(layoutPart);
                var canvas = new SlideCanvas(slidePart);
                plans[index].Render(canvas, logo);
                AddFooter(canvas, data, index + 1, plans.Count, plans[index].IsCover);
                canvas.Commit();

                slideIdList.Append(new SlideId
                {
                    Id = nextSlideId++,
                    RelationshipId = presentationPart.GetIdOfPart(slidePart)
                });
            }

            presentationPart.Presentation.Save();
            document.PackageProperties.Title = data.DeckName;
            document.PackageProperties.Subject = "Project briefing deck generated from PRISM ERP";
            document.PackageProperties.Creator = "Simulator Development Division";
            document.PackageProperties.LastModifiedBy = "PRISM ERP";
            document.PackageProperties.Modified = data.GeneratedAtUtc.UtcDateTime;
        }

        return (stream.ToArray(), plans.Count);
    }

    private static List<SlidePlan> BuildPlans(ProjectBriefingPresentationData data)
    {
        var plans = new List<SlidePlan>
        {
            new(true, (canvas, logo) => RenderCover(canvas, data, logo)),
            new(false, (canvas, _) => RenderPortfolioSummary(canvas, data))
        };

        if (data.IncludeStageSummary)
        {
            AddStageSummarySlides(plans, data);
        }

        if (data.IncludeProjectCategorySummary)
        {
            AddSummaryChartSlides(plans, data, "Project-category summary", "Selected projects by project category", data.Summary.ProjectCategorySummary, Teal);
        }

        if (data.IncludeTechnicalCategorySummary)
        {
            AddSummaryChartSlides(plans, data, "Technical-category summary", "Selected projects by technical category", data.Summary.TechnicalCategorySummary, Green);
        }

        if (data.PresentationMode is ProjectBriefingPresentationMode.ExecutiveTable
            or ProjectBriefingPresentationMode.Combined)
        {
            AddExecutiveTableSlides(plans, data);
        }

        if (data.PresentationMode is ProjectBriefingPresentationMode.DetailedProjects
            or ProjectBriefingPresentationMode.Combined)
        {
            foreach (var project in data.Projects.OrderBy(project => project.SortOrder))
            {
                var captured = project;
                plans.Add(new SlidePlan(false, (canvas, _) => RenderProjectDetail(canvas, data, captured)));
            }
        }

        return plans;
    }

    private static void RenderCover(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        byte[]? logo)
    {
        canvas.AddRect(0, 0, SlideWidth, SlideHeight, Navy);
        canvas.AddRect(0, 0, .17, SlideHeight, Blue);
        canvas.AddRect(8.75, 0, 4.58, SlideHeight, DeepNavy);
        canvas.AddRect(9.22, .72, 3.25, .10, Blue);
        canvas.AddRect(9.22, .96, 2.48, .10, Teal);
        canvas.AddRect(9.22, 1.20, 1.72, .10, Green);

        if (logo is not null)
        {
            canvas.AddImage(logo, "image/png", .63, .50, .68, .68, "SDD logo");
        }

        canvas.AddText(1.47, .55, 6.7, .36, "SIMULATOR DEVELOPMENT DIVISION", 15, "DCE7FF", true, "l");
        canvas.AddText(.70, 1.95, 7.45, 1.38, data.DeckName.ToUpperInvariant(), CoverTitleFontSize(data.DeckName), "FFFFFF", true, "l");
        canvas.AddText(.72, 3.47, 7.15, .75,
            string.IsNullOrWhiteSpace(data.DeckDescription)
                ? "PROJECT BRIEFING DECK"
                : Truncate(data.DeckDescription, 145),
            18, "C7D5E7", false, "l");
        canvas.AddText(.72, 5.55, 7.2, .32, "SELECTED PROJECTS · CURRENT PRISM POSITION", 12, "9FB4CD", true, "l");
        var generatedAtIst = TimeZoneInfo.ConvertTime(data.GeneratedAtUtc, TimeZoneHelper.GetIst());
        canvas.AddText(.72, 6.02, 7.2, .30, $"Generated {generatedAtIst:dd MMM yyyy, HH:mm} IST", 10.5, "91A6BF", false, "l");

        AddCoverMetric(canvas, 9.25, 2.10, data.Summary.ProjectCount, "PROJECTS");
        AddCoverMetric(canvas, 9.25, 3.50, data.Summary.OngoingCount, "ONGOING");
        AddCoverMetric(canvas, 9.25, 4.90, data.Summary.CompletedCount, "COMPLETED");

        if (!string.IsNullOrWhiteSpace(data.HandlingMarking))
        {
            canvas.AddRoundedRect(9.15, 6.42, 3.42, .45, "283B57", "516680", .05);
            canvas.AddText(9.30, 6.49, 3.12, .26, data.HandlingMarking!, 10, "FFFFFF", true, "ctr");
        }
    }

    private static void AddCoverMetric(SlideCanvas canvas, double x, double y, int value, string label)
    {
        canvas.AddText(x, y, 3.1, .48, value.ToString(CultureInfo.InvariantCulture), 28, "FFFFFF", true, "l");
        canvas.AddText(x, y + .52, 3.1, .28, label, 11, "AFC0D3", true, "l");
    }

    private static void RenderPortfolioSummary(SlideCanvas canvas, ProjectBriefingPresentationData data)
    {
        AddSlideTitle(canvas, "Portfolio at a glance", "Selected projects and briefing-data readiness");

        var cards = new[]
        {
            ("SELECTED PROJECTS", data.Summary.ProjectCount, Blue),
            ("ONGOING", data.Summary.OngoingCount, Teal),
            ("COMPLETED", data.Summary.CompletedCount, Green),
            ("STATUS MISSING", data.Summary.MissingExternalStatusCount, data.Summary.MissingExternalStatusCount > 0 ? Amber : Green)
        };

        for (var index = 0; index < cards.Length; index++)
        {
            var x = .64 + (index * 3.08);
            canvas.AddRoundedRect(x, 1.30, 2.73, 1.35, CardBackground, Border, .08);
            canvas.AddRect(x, 1.30, .08, 1.35, cards[index].Item3);
            canvas.AddText(x + .26, 1.56, 2.15, .48, cards[index].Item2.ToString(CultureInfo.InvariantCulture), 27, Text, true, "l");
            canvas.AddText(x + .26, 2.12, 2.20, .28, cards[index].Item1, 10.5, Muted, true, "l");
        }

        var costMode = data.CostMode;
        var showRd = costMode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both;
        var showProliferation = costMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both;

        if (showRd && showProliferation)
        {
            AddCostSummaryCard(canvas, .67, 3.05, 5.85, "COST (R&D)", data.Summary.TotalCostRdInRupees,
                data.Summary.CostRdRecordedCount, data.Summary.ProjectCount, Blue, LightBlue,
                "Resolved L1 → AoN → IPA");
            AddCostSummaryCard(canvas, 6.80, 3.05, 5.85, "PROLIFERATION COST", data.Summary.TotalProliferationCostInRupees,
                data.Summary.ProliferationCostRecordedCount, data.Summary.ProjectCount, Green, LightGreen,
                "Latest recorded indicative cost");
        }
        else if (showRd)
        {
            AddCostSummaryCard(canvas, 2.05, 3.05, 9.22, "COST (R&D)", data.Summary.TotalCostRdInRupees,
                data.Summary.CostRdRecordedCount, data.Summary.ProjectCount, Blue, LightBlue,
                "Resolved L1 → AoN → IPA");
        }
        else if (showProliferation)
        {
            AddCostSummaryCard(canvas, 2.05, 3.05, 9.22, "PROLIFERATION COST", data.Summary.TotalProliferationCostInRupees,
                data.Summary.ProliferationCostRecordedCount, data.Summary.ProjectCount, Green, LightGreen,
                "Latest recorded indicative cost");
        }
        else
        {
            canvas.AddRoundedRect(.67, 3.05, 11.98, 1.55, CardBackground, Border, .08);
            canvas.AddText(.95, 3.47, 11.42, .35, "Cost information is not included in this deck.", 17, Text, true, "ctr");
        }

        canvas.AddRoundedRect(.67, 5.00, 11.98, 1.35, CardBackground, Border, .08);
        canvas.AddText(.94, 5.25, 3.1, .28, "DATA READINESS", 11, Muted, true, "l");
        AddReadinessMetric(canvas, .95, 5.68, "External status", data.Summary.ProjectCount - data.Summary.MissingExternalStatusCount, data.Summary.ProjectCount, Blue);
        AddReadinessMetric(canvas, 4.25, 5.68, "PowerPoint-ready photo", data.Summary.ProjectCount - data.Summary.MissingPhotoCount, data.Summary.ProjectCount, Teal);
        AddReadinessMetric(canvas, 7.55, 5.68, "Cost (R&D)", data.Summary.CostRdRecordedCount, data.Summary.ProjectCount, Blue);
        AddReadinessMetric(canvas, 10.15, 5.68, "Proliferation", data.Summary.ProliferationCostRecordedCount, data.Summary.ProjectCount, Green);
    }

    private static void AddCostSummaryCard(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        string title,
        decimal amount,
        int recorded,
        int total,
        string accent,
        string fill,
        string note)
    {
        canvas.AddRoundedRect(x, y, width, 1.58, fill, accent, .08);
        canvas.AddText(x + .28, y + .25, width - .56, .28, title, 11, accent, true, "l");
        var amountDisplay = recorded > 0
            ? ProjectBriefingCurrencyFormatter.FormatRupees(amount)
            : "Not recorded";
        canvas.AddText(x + .28, y + .61, width - .56, .42, amountDisplay, 23, Text, true, "l");
        canvas.AddText(x + .28, y + 1.12, width - .56, .24, $"Recorded for {recorded} of {total} projects · {note}", 9.5, Muted, false, "l");
    }

    private static void AddReadinessMetric(
        SlideCanvas canvas,
        double x,
        double y,
        string label,
        int available,
        int total,
        string accent)
    {
        canvas.AddText(x, y, 1.35, .27, $"{available}/{total}", 16, accent, true, "l");
        canvas.AddText(x + .75, y + .02, 2.0, .23, label, 9.5, Muted, true, "l");
    }

    private static void AddStageSummarySlides(
        List<SlidePlan> plans,
        ProjectBriefingPresentationData data)
    {
        plans.Add(new SlidePlan(false, (canvas, _) => RenderBarChart(
            canvas,
            "Stage-wise summary",
            "Selected projects by present stage · reverse workflow order",
            data.Summary.StageSummary,
            Blue,
            data.Summary.ProjectCount,
            showShare: true)));

        plans.Add(new SlidePlan(false, (canvas, _) => RenderStageSummaryTable(canvas, data)));
    }

    private static void AddSummaryChartSlides(
        List<SlidePlan> plans,
        ProjectBriefingPresentationData data,
        string title,
        string subtitle,
        IReadOnlyList<ProjectBriefingSummaryPoint> points,
        string accent)
    {
        var chunks = points.Count == 0
            ? new[] { Array.Empty<ProjectBriefingSummaryPoint>() }
            : points.Chunk(10).Select(chunk => chunk.ToArray()).ToArray();

        for (var index = 0; index < chunks.Length; index++)
        {
            var captured = chunks[index];
            var capturedIndex = index;
            plans.Add(new SlidePlan(false, (canvas, _) => RenderBarChart(
                canvas,
                title + (chunks.Length > 1 ? $" ({capturedIndex + 1}/{chunks.Length})" : string.Empty),
                subtitle,
                captured,
                accent)));
        }
    }

    private static void RenderBarChart(
        SlideCanvas canvas,
        string title,
        string subtitle,
        IReadOnlyList<ProjectBriefingSummaryPoint> points,
        string accent,
        int total = 0,
        bool showShare = false)
    {
        AddSlideTitle(canvas, title, subtitle);
        if (points.Count == 0)
        {
            AddEmptyMessage(canvas, "No summary data is available for the selected projects.");
            return;
        }

        var maximum = Math.Max(1, points.Max(point => point.Count));
        var rowHeight = Math.Min(.49, 5.08 / points.Count);
        var startY = 1.42;
        var labelFont = points.Count > 12 ? 9.8 : 10.8;
        var barHeight = Math.Clamp(rowHeight * .56, .18, .27);

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var y = startY + (index * rowHeight);
            var barWidth = 7.05 * point.Count / maximum;
            canvas.AddText(.72, y, 3.15, rowHeight, Truncate(point.Label, 42), labelFont, Text, true, "l");
            canvas.AddRoundedRect(3.90, y + ((rowHeight - barHeight) / 2), 7.25, barHeight, "E8EDF4", "E8EDF4", .04);
            canvas.AddRoundedRect(3.90, y + ((rowHeight - barHeight) / 2), Math.Max(.16, barWidth), barHeight, accent, accent, .04);
            canvas.AddText(11.30, y, .50, rowHeight, point.Count.ToString(CultureInfo.InvariantCulture), 11.5, Text, true, "r");
            if (showShare && total > 0)
            {
                var share = point.Count * 100d / total;
                canvas.AddText(11.86, y, .55, rowHeight, $"{share:0.#}%", 9.5, Muted, false, "r");
            }
        }

        canvas.AddRoundedRect(.72, 6.48, 11.58, .30, LightBackground, Border, .04);
        canvas.AddText(.92, 6.52, 11.18, .20,
            showShare
                ? "Stages are shown from the most advanced position back to IPA. Bars and labels remain editable."
                : "Bars are native editable PowerPoint shapes. Projects are counted once in the selected category.",
            8.5, Muted, false, "l");
    }

    private static void RenderStageSummaryTable(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data)
    {
        AddSlideTitle(canvas, "Stage-wise summary — table", "Selected projects by present stage · reverse workflow order");
        var points = data.Summary.StageSummary;
        if (points.Count == 0)
        {
            AddEmptyMessage(canvas, "No stage data is available for the selected projects.");
            return;
        }

        var rows = new List<IReadOnlyList<NativeTableCell>>
        {
            new[]
            {
                Cell("PRESENT STAGE", 10.2, "FFFFFF", true, "l", Navy),
                Cell("PROJECTS", 10.2, "FFFFFF", true, "r", Navy),
                Cell("SHARE", 10.2, "FFFFFF", true, "r", Navy)
            }
        };

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var fill = index % 2 == 0 ? "FFFFFF" : "F7F9FC";
            var share = data.Summary.ProjectCount == 0
                ? "0%"
                : $"{point.Count * 100d / data.Summary.ProjectCount:0.#}%";
            rows.Add(new[]
            {
                Cell(point.Label, 10.5, Text, true, "l", fill),
                Cell(point.Count.ToString(CultureInfo.InvariantCulture), 10.5, Text, true, "r", fill),
                Cell(share, 10.2, Muted, false, "r", fill)
            });
        }

        rows.Add(new[]
        {
            Cell("TOTAL SELECTED PROJECTS", 10.5, Navy, true, "l", LightBlue),
            Cell(data.Summary.ProjectCount.ToString(CultureInfo.InvariantCulture), 10.5, Navy, true, "r", LightBlue),
            Cell("100%", 10.5, Navy, true, "r", LightBlue)
        });

        var bodyRows = rows.Count - 1;
        var rowHeight = Math.Min(.415, 5.10 / bodyRows);
        var heights = new List<double> { .46 };
        heights.AddRange(Enumerable.Repeat(rowHeight, bodyRows));
        canvas.AddNativeTable(1.02, 1.36, new[] { 8.05, 1.75, 1.55 }, heights, rows, "Stage-wise project summary table");
    }

    private static void AddExecutiveTableSlides(List<SlidePlan> plans, ProjectBriefingPresentationData data)
    {
        var projects = data.Projects.OrderBy(project => project.SortOrder).ToArray();
        var rowsPerSlide = data.CostMode == ProjectBriefingCostMode.Both ? 5 : 6;
        var chunks = projects.Chunk(rowsPerSlide).Select(chunk => chunk.ToArray()).ToArray();

        for (var index = 0; index < chunks.Length; index++)
        {
            var captured = chunks[index];
            var capturedIndex = index;
            plans.Add(new SlidePlan(false, (canvas, _) => RenderExecutiveTable(
                canvas,
                data,
                captured,
                capturedIndex + 1,
                chunks.Length)));
        }
    }

    private static void RenderExecutiveTable(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        IReadOnlyList<ProjectBriefingPresentationProject> projects,
        int page,
        int pages)
    {
        AddSlideTitle(
            canvas,
            "Project status summary" + (pages > 1 ? $" ({page}/{pages})" : string.Empty),
            "Cost, present stage and latest external status");

        var headers = new List<string> { "PROJECT" };
        var widths = new List<double>();
        switch (data.CostMode)
        {
            case ProjectBriefingCostMode.Both:
                headers.AddRange(new[] { "COST (R&D)", "PROLIFERATION COST", "PRESENT STAGE", "STATUS" });
                widths.AddRange(new[] { 2.30, 1.35, 1.45, 1.65, 5.40 });
                break;
            case ProjectBriefingCostMode.CostRdOnly:
                headers.AddRange(new[] { "COST (R&D)", "PRESENT STAGE", "STATUS" });
                widths.AddRange(new[] { 2.55, 1.55, 1.85, 6.20 });
                break;
            case ProjectBriefingCostMode.ProliferationOnly:
                headers.AddRange(new[] { "PROLIFERATION COST", "PRESENT STAGE", "STATUS" });
                widths.AddRange(new[] { 2.55, 1.70, 1.85, 6.05 });
                break;
            default:
                headers.AddRange(new[] { "PRESENT STAGE", "STATUS" });
                widths.AddRange(new[] { 2.75, 1.85, 7.55 });
                break;
        }

        var rows = new List<IReadOnlyList<NativeTableCell>>
        {
            headers.Select(value => Cell(value, 9.2, "FFFFFF", true, "l", Navy)).ToArray()
        };

        for (var index = 0; index < projects.Count; index++)
        {
            var project = projects[index];
            var rowFill = index % 2 == 0 ? "FFFFFF" : "F7F9FC";
            var costFill = index % 2 == 0 ? "F8FAFC" : "F1F5F9";
            var cells = new List<NativeTableCell>
            {
                Cell(Truncate(project.ProjectName, 52), 10.1, Text, true, "l", rowFill)
            };

            if (data.CostMode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both)
            {
                cells.Add(Cell(
                    CostCell(project.CostRd, "Not recorded"),
                    9.1,
                    project.CostRd.IsAvailable ? Text : Muted,
                    project.CostRd.IsAvailable,
                    "l",
                    costFill));
            }
            if (data.CostMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both)
            {
                cells.Add(Cell(
                    CostCell(project.ProliferationCost, "Not recorded"),
                    9.1,
                    project.ProliferationCost.IsAvailable ? Text : Muted,
                    project.ProliferationCost.IsAvailable,
                    "l",
                    costFill));
            }

            cells.Add(Cell(Truncate(project.PresentStage, 36), 9.3, Text, true, "l", rowFill));
            cells.Add(Cell(
                Truncate(project.ExternalStatus, data.CostMode == ProjectBriefingCostMode.Both ? 205 : 235),
                9.2,
                string.Equals(project.ExternalStatus, "No external status recorded", StringComparison.Ordinal) ? Muted : Text,
                false,
                "l",
                rowFill));
            rows.Add(cells);
        }

        var rowHeight = projects.Count <= 5 ? .96 : .80;
        var heights = new List<double> { .46 };
        heights.AddRange(Enumerable.Repeat(rowHeight, projects.Count));
        canvas.AddNativeTable(.58, 1.32, widths, heights, rows, "Project status summary table");

        canvas.AddText(.65, 6.65, 12.0, .20,
            "Status is the latest external remark recorded in PRISM. Cost (R&D) resolves L1 → AoN → IPA.",
            8.2, Muted, false, "l");
    }

    private static string CostCell(ProjectBriefingCostValue value, string missing)
        => value.IsAvailable
            ? string.IsNullOrWhiteSpace(value.BasisDisplay)
                ? value.DisplayValue
                : $"{value.DisplayValue}\n{value.BasisDisplay}"
            : missing;

    private static void RenderProjectDetail(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
    {
        AddSlideTitle(canvas, Truncate(project.ProjectName, 82), $"{project.LifecycleDisplay} · {CategoryLine(project)}");

        const double leftX = .60;
        const double leftWidth = 4.40;
        const double rightX = 5.25;
        const double rightWidth = 7.48;
        const double contentTop = 1.28;
        const double contentBottom = 6.70;

        var hasPhoto = project.CoverPhoto is { Length: > 0 };
        var photoHeight = hasPhoto ? 2.48 : 1.18;
        canvas.AddRoundedRect(leftX, contentTop, leftWidth, photoHeight, CardBackground, Border, .08);
        if (hasPhoto)
        {
            var imageHeight = photoHeight - .20;
            var imageWidth = imageHeight * 16d / 9d;
            var imageX = leftX + ((leftWidth - imageWidth) / 2d);
            canvas.AddImage(
                project.CoverPhoto!,
                project.CoverPhotoContentType,
                imageX,
                contentTop + .10,
                imageWidth,
                imageHeight,
                $"{project.ProjectName} cover photograph");
        }
        else
        {
            canvas.AddRect(leftX + .10, contentTop + .10, leftWidth - .20, photoHeight - .20, "EEF2F7", "E1E7EF");
            canvas.AddRect(leftX + 1.55, contentTop + .39, 1.25, .06, "B7C2D0");
            canvas.AddRect(leftX + 1.76, contentTop + .60, .83, .06, "CBD3DE");
            canvas.AddText(leftX + .45, contentTop + .72, leftWidth - .90, .25,
                "PHOTOGRAPH NOT AVAILABLE", 9.5, Muted, true, "ctr");
        }

        var positionY = contentTop + photoHeight + .18;
        var costCards = CostCards(data.CostMode, project);
        var costHeight = costCards.Count == 0 ? 0d : 1.03;
        var positionHeight = Math.Max(1.52, contentBottom - positionY - costHeight - (costHeight > 0 ? .18 : 0));
        AddProjectPositionCard(canvas, leftX, positionY, leftWidth, positionHeight, project);

        if (costCards.Count > 0)
        {
            var costY = positionY + positionHeight + .18;
            if (costCards.Count == 2)
            {
                AddInfoCard(canvas, leftX, costY, 2.11, costHeight,
                    costCards[0].Title, costCards[0].Value, costCards[0].Accent, costCards[0].Fill, costCards[0].Note);
                AddInfoCard(canvas, leftX + 2.29, costY, 2.11, costHeight,
                    costCards[1].Title, costCards[1].Value, costCards[1].Accent, costCards[1].Fill, costCards[1].Note);
            }
            else
            {
                AddInfoCard(canvas, leftX, costY, leftWidth, costHeight,
                    costCards[0].Title, costCards[0].Value, costCards[0].Accent, costCards[0].Fill, costCards[0].Note);
            }
        }

        canvas.AddRoundedRect(rightX, contentTop, rightWidth, contentBottom - contentTop, CardBackground, Border, .08);
        canvas.AddRect(rightX, contentTop, .08, contentBottom - contentTop, Teal);
        canvas.AddText(rightX + .32, contentTop + .22, rightWidth - .62, .28,
            "CAPABILITY OVERVIEW", 10.5, Teal, true, "l");

        var overview = FitOverview(project.BriefDescription);
        canvas.AddText(
            rightX + .32,
            contentTop + .66,
            rightWidth - .64,
            contentBottom - contentTop - .92,
            overview.Text,
            overview.FontSize,
            Text,
            false,
            "l",
            "t");
    }

    private static void AddProjectPositionCard(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        double height,
        ProjectBriefingPresentationProject project)
    {
        canvas.AddRoundedRect(x, y, width, height, CardBackground, Border, .08);
        canvas.AddText(x + .25, y + .18, width - .50, .22, "PROJECT POSITION", 9.5, Blue, true, "l");
        canvas.AddText(x + .25, y + .48, width - .50, .18, "PRESENT STAGE", 7.8, Muted, true, "l");
        canvas.AddRoundedRect(x + .25, y + .70, Math.Min(width - .50, 2.65), .38, LightBlue, Blue, .06);
        canvas.AddText(x + .37, y + .74, Math.Min(width - .74, 2.40), .28,
            Truncate(project.PresentStage, 34), 10.5, Navy, true, "l");

        var statusLabel = project.ExternalStatusDate.HasValue
            ? $"STATUS · {project.ExternalStatusDate.Value:dd MMM yyyy}"
            : "STATUS";
        canvas.AddText(x + .25, y + 1.18, width - .50, .20, statusLabel, 8.2, Blue, true, "l");
        var statusHeight = Math.Max(.34, height - 1.48);
        var statusFont = project.ExternalStatus.Length switch
        {
            <= 95 => 10.2,
            <= 165 => 9.5,
            _ => 8.8
        };
        canvas.AddText(x + .25, y + 1.43, width - .50, statusHeight,
            Truncate(project.ExternalStatus, 300), statusFont, Text, false, "l", "t");
    }

    private static (string Text, double FontSize) FitOverview(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value)
            ? "Brief description not recorded."
            : value.Trim();
        var maximum = 1200;
        if (text.Length > maximum)
        {
            text = text[..maximum].TrimEnd() + "…";
        }

        var font = text.Length switch
        {
            <= 420 => 14.5,
            <= 700 => 13.5,
            <= 950 => 12.5,
            _ => 11.5
        };
        return (text, font);
    }

    private static string CategoryLine(ProjectBriefingPresentationProject project)
    {
        var parts = new[] { project.ProjectCategory, project.TechnicalCategory }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? "Project record" : string.Join(" · ", parts);
    }

    private static IReadOnlyList<CostCard> CostCards(
        ProjectBriefingCostMode mode,
        ProjectBriefingPresentationProject project)
    {
        var result = new List<CostCard>();
        if (mode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both)
        {
            result.Add(new CostCard(
                "COST (R&D)",
                project.CostRd.IsAvailable ? project.CostRd.DisplayValue : "Not recorded",
                Blue,
                LightBlue,
                project.CostRd.IsAvailable ? project.CostRd.BasisDisplay : "L1 → AoN → IPA"));
        }
        if (mode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both)
        {
            result.Add(new CostCard(
                "PROLIFERATION COST",
                project.ProliferationCost.IsAvailable ? project.ProliferationCost.DisplayValue : "Not recorded",
                Green,
                LightGreen,
                "Indicative"));
        }
        return result;
    }

    private static void AddInfoCard(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        double height,
        string title,
        string value,
        string accent,
        string fill,
        string? note = null)
    {
        canvas.AddRoundedRect(x, y, width, height, fill, Border, .08);
        canvas.AddRect(x, y, .06, height, accent);
        canvas.AddText(x + .20, y + .14, width - .38, .22, title, width < 2.3 ? 8.0 : 9.2, accent, true, "l");
        canvas.AddText(x + .20, y + .40, width - .38, .30, Truncate(value, 70), width < 2.3 ? 12.0 : 13.5, Text, true, "l");
        if (!string.IsNullOrWhiteSpace(note))
        {
            canvas.AddText(x + .20, y + height - .23, width - .38, .17, note, 7.8, Muted, false, "l");
        }
    }

    private static void AddSlideTitle(SlideCanvas canvas, string title, string subtitle)
    {
        canvas.AddRect(0, 0, SlideWidth, SlideHeight, LightBackground);
        canvas.AddRect(0, 0, SlideWidth, .12, Blue);
        canvas.AddText(.62, .37, 10.90, .48, title, 23, Navy, true, "l");
        canvas.AddText(.64, .91, 10.90, .25, subtitle, 10.5, Muted, false, "l");
    }

    private static void AddFooter(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        int slideNumber,
        int slideCount,
        bool isCover)
    {
        if (isCover)
        {
            return;
        }

        canvas.AddLine(.62, 7.05, 12.72, 7.05, Border, .6);
        canvas.AddText(.65, 7.12, 5.55, .18, "SIMULATOR DEVELOPMENT DIVISION · PRISM ERP", 7.5, Muted, true, "l");
        var centre = string.IsNullOrWhiteSpace(data.HandlingMarking)
            ? "STATUS: LATEST EXTERNAL REMARK ONLY"
            : data.HandlingMarking!;
        canvas.AddText(4.55, 7.12, 4.25, .18, centre, 7.5, string.IsNullOrWhiteSpace(data.HandlingMarking) ? Muted : Red, true, "ctr");
        canvas.AddText(10.35, 7.12, 2.35, .18, $"{slideNumber}/{slideCount}", 7.5, Muted, true, "r");
    }

    private static void AddEmptyMessage(SlideCanvas canvas, string message)
    {
        canvas.AddRoundedRect(1.12, 2.25, 11.05, 2.15, CardBackground, Border, .08);
        canvas.AddText(1.55, 3.03, 10.20, .55, message, 17, Muted, false, "ctr");
    }

    private static NativeTableCell Cell(
        string? value,
        double fontSize = 9,
        string color = Text,
        bool bold = false,
        string align = "l",
        string fill = "FFFFFF")
        => new(value ?? string.Empty, fontSize, color, bold, align, fill);

    private static string Truncate(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximum
            ? normalized
            : normalized[..Math.Max(1, maximum - 1)].TrimEnd() + "…";
    }

    private static double CoverTitleFontSize(string title)
        => title.Length switch
        {
            <= 34 => 34,
            <= 52 => 30,
            <= 75 => 26,
            _ => 22
        };

    private static SlideLayoutPart? FindBlankLayout(PresentationPart presentationPart)
        => presentationPart.SlideMasterParts
               .SelectMany(master => master.SlideLayoutParts)
               .FirstOrDefault(layout =>
                   string.Equals(layout.SlideLayout?.CommonSlideData?.Name?.Value, "Blank", StringComparison.OrdinalIgnoreCase))
           ?? presentationPart.SlideMasterParts.SelectMany(master => master.SlideLayoutParts).FirstOrDefault();

    private static void RemoveTemplateSlides(PresentationPart presentationPart)
    {
        var slideIdList = presentationPart.Presentation.SlideIdList;
        if (slideIdList is null) return;

        foreach (var slideId in slideIdList.Elements<SlideId>().ToList())
        {
            var relationshipId = slideId.RelationshipId?.Value;
            if (!string.IsNullOrWhiteSpace(relationshipId)
                && presentationPart.GetPartById(relationshipId) is SlidePart slidePart)
            {
                presentationPart.DeletePart(slidePart);
            }
            slideId.Remove();
        }
    }

    private sealed record CostCard(string Title, string Value, string Accent, string Fill, string Note);
    private sealed record NativeTableCell(string Value, double FontSize, string Color, bool Bold, string Align, string Fill);
    private sealed record SlidePlan(bool IsCover, Action<SlideCanvas, byte[]?> Render);

    private sealed class SlideCanvas
    {
        private readonly SlidePart _slidePart;
        private readonly List<string> _elements = new();
        private int _nextShapeId = 2;

        public SlideCanvas(SlidePart slidePart) => _slidePart = slidePart;

        public void AddRect(double x, double y, double width, double height, string fill, string? line = null, double lineWidth = .75)
            => AddShape(x, y, width, height, fill, line, lineWidth, "rect", null, 0, Text, false, "l");

        public void AddRoundedRect(double x, double y, double width, double height, string fill, string? line, double radius)
            => AddShape(x, y, width, height, fill, line, .75, "roundRect", null, 0, Text, false, "l");

        public void AddLine(double x1, double y1, double x2, double y2, string color, double width)
        {
            var id = _nextShapeId++;
            _elements.Add($"""
<p:cxnSp>
  <p:nvCxnSpPr><p:cNvPr id="{id}" name="Line {id}"/><p:cNvCxnSpPr/><p:nvPr/></p:nvCxnSpPr>
  <p:spPr><a:xfrm><a:off x="{Emu(x1)}" y="{Emu(y1)}"/><a:ext cx="{Emu(x2 - x1)}" cy="{Emu(y2 - y1)}"/></a:xfrm><a:prstGeom prst="line"><a:avLst/></a:prstGeom><a:ln w="{LineWidth(width)}"><a:solidFill><a:srgbClr val="{CleanColor(color)}"/></a:solidFill></a:ln></p:spPr>
</p:cxnSp>
""");
        }

        public void AddText(
            double x,
            double y,
            double width,
            double height,
            string text,
            double fontSize,
            string color,
            bool bold,
            string align,
            string verticalAnchor = "ctr")
            => AddShape(x, y, width, height, null, null, 0, "rect", text, fontSize, color, bold, align, verticalAnchor);

        public void AddNativeTable(
            double x,
            double y,
            IReadOnlyList<double> widths,
            IReadOnlyList<double> heights,
            IReadOnlyList<IReadOnlyList<NativeTableCell>> rows,
            string name)
        {
            if (widths.Count == 0 || rows.Count == 0) return;
            if (heights.Count != rows.Count)
            {
                throw new ArgumentException("A native PowerPoint table requires one row height per row.", nameof(heights));
            }
            if (rows.Any(row => row.Count != widths.Count))
            {
                throw new ArgumentException("Every native PowerPoint table row must contain one cell per column.", nameof(rows));
            }

            var id = _nextShapeId++;
            var columnXml = string.Join(string.Empty, widths.Select(width => $"<a:gridCol w=\"{Emu(width)}\"/>"));
            var rowXml = new StringBuilder();

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                rowXml.Append($"<a:tr h=\"{Emu(heights[rowIndex])}\">");
                foreach (var cell in rows[rowIndex])
                {
                    rowXml.Append($"""
<a:tc>
  {BuildTableTextBody(cell)}
  <a:tcPr marL="45720" marR="45720" marT="22860" marB="22860" anchor="ctr">
    <a:solidFill><a:srgbClr val="{CleanColor(cell.Fill)}"/></a:solidFill>
    {TableBorders()}
  </a:tcPr>
</a:tc>
""");
                }
                rowXml.Append("</a:tr>");
            }

            _elements.Add($"""
<p:graphicFrame>
  <p:nvGraphicFramePr><p:cNvPr id="{id}" name="{Escape(name)}"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp="1"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>
  <p:xfrm><a:off x="{Emu(x)}" y="{Emu(y)}"/><a:ext cx="{Emu(widths.Sum())}" cy="{Emu(heights.Sum())}"/></p:xfrm>
  <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/table"><a:tbl><a:tblPr firstRow="1" bandRow="1"/><a:tblGrid>{columnXml}</a:tblGrid>{rowXml}</a:tbl></a:graphicData></a:graphic>
</p:graphicFrame>
""");
        }

        public void AddImage(byte[] content, string? contentType, double x, double y, double width, double height, string name)
        {
            var imageType = string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase)
                ? ImagePartType.Jpeg
                : ImagePartType.Png;
            var imagePart = _slidePart.AddImagePart(imageType);
            using (var imageStream = new MemoryStream(content, writable: false))
            {
                imagePart.FeedData(imageStream);
            }
            var relationshipId = _slidePart.GetIdOfPart(imagePart);
            var id = _nextShapeId++;
            _elements.Add($"""
<p:pic>
  <p:nvPicPr><p:cNvPr id="{id}" name="{Escape(name)}"/><p:cNvPicPr><a:picLocks noChangeAspect="1"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>
  <p:blipFill><a:blip r:embed="{relationshipId}"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>
  <p:spPr><a:xfrm><a:off x="{Emu(x)}" y="{Emu(y)}"/><a:ext cx="{Emu(width)}" cy="{Emu(height)}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:ln><a:noFill/></a:ln></p:spPr>
</p:pic>
""");
        }

        public void Commit()
        {
            var xml = $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
  <p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>{string.Join(Environment.NewLine, _elements)}</p:spTree></p:cSld>
  <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
</p:sld>
""";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            _slidePart.FeedData(stream);
        }

        private void AddShape(
            double x,
            double y,
            double width,
            double height,
            string? fill,
            string? line,
            double lineWidth,
            string geometry,
            string? text,
            double fontSize,
            string color,
            bool bold,
            string align,
            string verticalAnchor = "ctr")
        {
            var id = _nextShapeId++;
            var fillXml = string.IsNullOrWhiteSpace(fill)
                ? "<a:noFill/>"
                : $"<a:solidFill><a:srgbClr val=\"{CleanColor(fill)}\"/></a:solidFill>";
            var lineXml = string.IsNullOrWhiteSpace(line)
                ? "<a:ln><a:noFill/></a:ln>"
                : $"<a:ln w=\"{LineWidth(lineWidth)}\"><a:solidFill><a:srgbClr val=\"{CleanColor(line)}\"/></a:solidFill></a:ln>";
            var textXml = text is null ? string.Empty : BuildTextBody(text, fontSize, color, bold, align, verticalAnchor);

            _elements.Add($"""
<p:sp>
  <p:nvSpPr><p:cNvPr id="{id}" name="Shape {id}"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr>
  <p:spPr><a:xfrm><a:off x="{Emu(x)}" y="{Emu(y)}"/><a:ext cx="{Emu(width)}" cy="{Emu(height)}"/></a:xfrm><a:prstGeom prst="{geometry}"><a:avLst/></a:prstGeom>{fillXml}{lineXml}</p:spPr>
  {textXml}
</p:sp>
""");
        }

        private static string BuildTableTextBody(NativeTableCell cell)
        {
            var alignment = Alignment(cell.Align);
            var paragraphs = cell.Value
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Split('\n')
                .Select(line => $"""
<a:p><a:pPr algn="{alignment}"/><a:r><a:rPr lang="en-IN" sz="{FontSize(cell.FontSize)}" b="{(cell.Bold ? 1 : 0)}"><a:solidFill><a:srgbClr val="{CleanColor(cell.Color)}"/></a:solidFill><a:latin typeface="Aptos"/></a:rPr><a:t xml:space="preserve">{Escape(line)}</a:t></a:r><a:endParaRPr lang="en-IN" sz="{FontSize(cell.FontSize)}"/></a:p>
""");
            return $"<a:txBody><a:bodyPr wrap=\"square\" lIns=\"0\" rIns=\"0\" tIns=\"0\" bIns=\"0\" anchor=\"ctr\"/><a:lstStyle/>{string.Join(string.Empty, paragraphs)}</a:txBody>";
        }

        private static string TableBorders()
        {
            var line = $"<a:solidFill><a:srgbClr val=\"{Border}\"/></a:solidFill><a:prstDash val=\"solid\"/>";
            return $"<a:lnL w=\"3175\">{line}</a:lnL><a:lnR w=\"3175\">{line}</a:lnR><a:lnT w=\"3175\">{line}</a:lnT><a:lnB w=\"3175\">{line}</a:lnB>";
        }

        private static string BuildTextBody(
            string text,
            double fontSize,
            string color,
            bool bold,
            string align,
            string verticalAnchor)
        {
            var alignment = Alignment(align);
            var anchor = VerticalAnchor(verticalAnchor);
            var paragraphs = text
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Split('\n')
                .Select(line => $"""
<a:p><a:pPr algn="{alignment}"/><a:r><a:rPr lang="en-IN" sz="{FontSize(fontSize)}" b="{(bold ? 1 : 0)}"><a:solidFill><a:srgbClr val="{CleanColor(color)}"/></a:solidFill><a:latin typeface="Aptos"/></a:rPr><a:t xml:space="preserve">{Escape(line)}</a:t></a:r><a:endParaRPr lang="en-IN" sz="{FontSize(fontSize)}"/></a:p>
""");
            return $"<p:txBody><a:bodyPr wrap=\"square\" lIns=\"45720\" rIns=\"45720\" tIns=\"22860\" bIns=\"22860\" anchor=\"{anchor}\"/><a:lstStyle/>{string.Join(string.Empty, paragraphs)}</p:txBody>";
        }

        private static string Alignment(string value) => value switch { "ctr" => "ctr", "r" => "r", _ => "l" };
        private static string VerticalAnchor(string value) => value switch { "t" => "t", "b" => "b", _ => "ctr" };
        private static long Emu(double inches) => (long)Math.Round(inches * 914400d);
        private static long LineWidth(double points) => (long)Math.Round(points * 12700d);
        private static int FontSize(double points) => (int)Math.Round(points * 100d);
        private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
        private static string CleanColor(string value) => value.Trim().TrimStart('#').ToUpperInvariant();
    }
}

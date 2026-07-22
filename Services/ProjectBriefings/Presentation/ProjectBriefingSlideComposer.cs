using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.AspNetCore.Hosting;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed class ProjectBriefingSlideComposer : IProjectBriefingSlideComposer
{
    private const double SlideWidth = 13.333333;
    private const double SlideHeight = 7.5;

    private readonly string _templatePath;
    private readonly string? _leftLogoPath;
    private readonly string? _rightLogoPath;

    public ProjectBriefingSlideComposer(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _templatePath = Path.Combine(
            environment.ContentRootPath,
            "Resources",
            "ProjectBriefing",
            "ProjectBriefingTemplate.pptx");

        var webRootPath = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath, "wwwroot")
            : environment.WebRootPath;
        var leftLogoCandidate = Path.Combine(webRootPath, "img", "logos", "artrac.png");
        var rightLogoCandidate = Path.Combine(webRootPath, "img", "logos", "sdd.png");
        _leftLogoPath = File.Exists(leftLogoCandidate) ? leftLogoCandidate : null;
        _rightLogoPath = File.Exists(rightLogoCandidate) ? rightLogoCandidate : null;
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
            var theme = ProjectBriefingThemeCatalog.Resolve(data.PresentationTheme);
            var branding = new ProjectBriefingBrandingAssets(
                ReadAsset(_leftLogoPath),
                ReadAsset(_rightLogoPath));

            for (var index = 0; index < plans.Count; index++)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.AddPart(layoutPart);
                var showBranding = ShouldShowBranding(data.BrandingScope, plans[index].Kind);
                var canvas = new SlideCanvas(slidePart, theme, branding, showBranding);
                plans[index].Render(canvas);
                AddFooter(canvas, data, index + 1, plans.Count, plans[index].Kind == SlidePlanKind.Cover);
                canvas.Commit();

                slideIdList.Append(new SlideId
                {
                    Id = nextSlideId++,
                    RelationshipId = presentationPart.GetIdOfPart(slidePart)
                });
            }

            presentationPart.Presentation.Save();
            document.PackageProperties.Title = data.DeckName;
            document.PackageProperties.Subject = "Professional project briefing deck";
            document.PackageProperties.Creator = "Simulator Development Division";
            document.PackageProperties.LastModifiedBy = "Project Briefing Deck Builder";
            document.PackageProperties.Modified = data.GeneratedAtUtc.UtcDateTime;
        }

        return (stream.ToArray(), plans.Count);
    }

    private static List<SlidePlan> BuildPlans(ProjectBriefingPresentationData data)
    {
        var plans = new List<SlidePlan>
        {
            new(SlidePlanKind.Cover, canvas => RenderCover(canvas, data)),
            new(SlidePlanKind.Summary, canvas => RenderPortfolioSummary(canvas, data))
        };

        if (data.IncludeStageSummary)
        {
            AddStageSummarySlides(plans, data);
        }

        if (data.IncludeProjectCategorySummary)
        {
            AddSummaryChartSlides(plans, data, "Project-category summary", data.Summary.ProjectCategorySummary, ThemeAccent.Secondary);
        }

        if (data.IncludeTechnicalCategorySummary)
        {
            AddSummaryChartSlides(plans, data, "Technical-category summary", data.Summary.TechnicalCategorySummary, ThemeAccent.Positive);
        }

        if (data.PresentationMode is ProjectBriefingPresentationMode.ExecutiveTable
            or ProjectBriefingPresentationMode.Combined)
        {
            AddExecutiveTableSlides(plans, data);
        }

        if (data.PresentationMode is ProjectBriefingPresentationMode.DetailedProjects
            or ProjectBriefingPresentationMode.Combined)
        {
            foreach (var project in OrderProjects(data.Projects))
            {
                var capturedProject = project;
                var capability = ProjectBriefingCapabilityPaginator.Paginate(project.BriefDescription);
                var primaryPage = capability.Pages[0];
                plans.Add(new SlidePlan(SlidePlanKind.Project, canvas =>
                    RenderProjectDetail(canvas, data, capturedProject, primaryPage)));

                var continuationPages = capability.Pages.Skip(1).ToArray();
                for (var index = 0; index < continuationPages.Length; index++)
                {
                    var capturedPage = continuationPages[index];
                    var capturedIndex = index;
                    plans.Add(new SlidePlan(SlidePlanKind.Project, canvas =>
                        RenderCapabilityContinuation(
                            canvas,
                            capturedProject,
                            capturedPage,
                            capturedIndex + 1,
                            continuationPages.Length)));
                }
            }
        }

        return plans;
    }

    private static IOrderedEnumerable<ProjectBriefingPresentationProject> OrderProjects(
        IEnumerable<ProjectBriefingPresentationProject> projects)
        => projects
            .OrderBy(project => project.PresentStageOrder)
            .ThenBy(project => project.SortOrder)
            .ThenBy(project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.ProjectId);

    private static void RenderCover(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data)
    {
        var theme = canvas.Theme;
        canvas.AddRect(0, 0, SlideWidth, SlideHeight, theme.CoverCanvas);
        canvas.AddRect(0, 0, SlideWidth, .10, theme.Accent);
        canvas.AddBrandingImages(HeaderVariant.Cover);

        canvas.AddText(.78, .48, 11.77, .34,
            "SIMULATOR DEVELOPMENT DIVISION",
            13.2,
            theme.CoverMuted,
            true,
            "ctr");
        canvas.AddLine(.78, 1.02, 12.55, 1.02, theme.Divider, .7);

        canvas.AddText(.82, 1.68, 7.55, 1.42,
            data.DeckName.ToUpperInvariant(),
            CoverTitleFontSize(data.DeckName),
            theme.CoverText,
            true,
            "l",
            "t");
        canvas.AddText(.84, 3.27, 7.30, .82,
            string.IsNullOrWhiteSpace(data.DeckDescription)
                ? "PROJECT BRIEFING DECK"
                : Truncate(data.DeckDescription, 145),
            17.5,
            theme.CoverMuted,
            false,
            "l",
            "t");

        canvas.AddRoundedRect(8.82, 1.55, 3.72, 4.58, theme.CoverSurface, theme.Divider, .08);
        canvas.AddRect(8.82, 1.55, .07, 4.58, theme.Accent);
        AddCoverMetric(canvas, 9.20, 2.02, data.Summary.ProjectCount, "SELECTED PROJECTS");
        AddCoverMetric(canvas, 9.20, 3.35, data.Summary.OngoingCount, "ONGOING");
        AddCoverMetric(canvas, 9.20, 4.68, data.Summary.CompletedCount, "COMPLETED");

        var generatedAtIst = TimeZoneInfo.ConvertTime(data.GeneratedAtUtc, TimeZoneHelper.GetIst());
        canvas.AddText(.84, 5.68, 7.4, .27,
            "SELECTED PROJECTS · CURRENT PROJECT POSITION",
            10.5,
            theme.CoverMuted,
            true,
            "l");
        canvas.AddText(.84, 6.10, 7.4, .27,
            $"Generated {generatedAtIst:dd MMM yyyy, HH:mm} IST",
            9.8,
            theme.CoverMuted,
            false,
            "l");

        if (!string.IsNullOrWhiteSpace(data.HandlingMarking))
        {
            canvas.AddRoundedRect(9.10, 6.42, 3.20, .43, theme.SurfaceRaised, theme.Divider, .05);
            canvas.AddText(9.23, 6.49, 2.94, .24, data.HandlingMarking!, 9.5, theme.CoverText, true, "ctr");
        }
    }

    private static void AddCoverMetric(SlideCanvas canvas, double x, double y, int value, string label)
    {
        canvas.AddText(x, y, 2.95, .47,
            value.ToString(CultureInfo.InvariantCulture),
            27,
            canvas.Theme.CoverText,
            true,
            "l");
        canvas.AddText(x, y + .51, 2.95, .25,
            label,
            9.8,
            canvas.Theme.CoverMuted,
            true,
            "l");
    }

    private static void RenderPortfolioSummary(SlideCanvas canvas, ProjectBriefingPresentationData data)
    {
        AddSlideTitle(canvas, "Portfolio at a glance");

        var cards = new[]
        {
            ("SELECTED PROJECTS", data.Summary.ProjectCount, canvas.Theme.Accent),
            ("ONGOING", data.Summary.OngoingCount, canvas.Theme.SecondaryAccent),
            ("COMPLETED", data.Summary.CompletedCount, canvas.Theme.Positive)
        };

        const double cardWidth = 3.72;
        const double cardGap = .44;
        const double cardStartX = .65;
        for (var index = 0; index < cards.Length; index++)
        {
            var x = cardStartX + (index * (cardWidth + cardGap));
            canvas.AddRoundedRect(x, 1.35, cardWidth, 1.58, canvas.Theme.Surface, canvas.Theme.Border, .08);
            canvas.AddRect(x, 1.35, .08, 1.58, cards[index].Item3);
            canvas.AddText(x + .28, 1.63, cardWidth - .56, .52,
                cards[index].Item2.ToString(CultureInfo.InvariantCulture),
                29,
                canvas.Theme.TextPrimary,
                true,
                "l");
            canvas.AddText(x + .28, 2.30, cardWidth - .56, .28,
                cards[index].Item1,
                10.8,
                canvas.Theme.TextMuted,
                true,
                "l");
        }

        var showRd = data.CostMode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both;
        var showProliferation = data.CostMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both;

        if (showRd && showProliferation)
        {
            AddCostSummaryCard(
                canvas,
                .65,
                3.42,
                5.86,
                "COST (R&D)",
                data.Summary.TotalCostRdInRupees,
                data.Summary.CostRdRecordedCount,
                data.Summary.ProjectCount,
                canvas.Theme.Accent,
                canvas.Theme.AccentSoft);
            AddCostSummaryCard(
                canvas,
                6.82,
                3.42,
                5.86,
                "PROLIFERATION COST",
                data.Summary.TotalProliferationCostInRupees,
                data.Summary.ProliferationCostRecordedCount,
                data.Summary.ProjectCount,
                canvas.Theme.Positive,
                canvas.Theme.PositiveSoft);
        }
        else if (showRd)
        {
            AddCostSummaryCard(
                canvas,
                2.00,
                3.42,
                9.33,
                "COST (R&D)",
                data.Summary.TotalCostRdInRupees,
                data.Summary.CostRdRecordedCount,
                data.Summary.ProjectCount,
                canvas.Theme.Accent,
                canvas.Theme.AccentSoft);
        }
        else if (showProliferation)
        {
            AddCostSummaryCard(
                canvas,
                2.00,
                3.42,
                9.33,
                "PROLIFERATION COST",
                data.Summary.TotalProliferationCostInRupees,
                data.Summary.ProliferationCostRecordedCount,
                data.Summary.ProjectCount,
                canvas.Theme.Positive,
                canvas.Theme.PositiveSoft);
        }
        else
        {
            canvas.AddRoundedRect(.65, 3.42, 12.03, 1.92, canvas.Theme.Surface, canvas.Theme.Border, .08);
            canvas.AddText(.95, 4.10, 11.43, .38,
                "Cost information is not included in this deck.",
                18,
                canvas.Theme.TextPrimary,
                true,
                "ctr");
        }
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
        string fill)
    {
        canvas.AddRoundedRect(x, y, width, 1.92, fill, accent, .08);
        canvas.AddText(x + .30, y + .28, width - .60, .28, title, 11.2, accent, true, "l");
        var amountDisplay = recorded > 0
            ? ProjectBriefingCurrencyFormatter.FormatRupees(amount)
            : "Not recorded";
        canvas.AddText(x + .30, y + .70, width - .60, .48, amountDisplay, 25, canvas.Theme.TextPrimary, true, "l");
        canvas.AddText(
            x + .30,
            y + 1.36,
            width - .60,
            .26,
            $"Available for {recorded} of {total} selected projects",
            9.8,
            canvas.Theme.TextMuted,
            false,
            "l");
    }

    private static void AddStageSummarySlides(
        List<SlidePlan> plans,
        ProjectBriefingPresentationData data)
    {
        plans.Add(new SlidePlan(SlidePlanKind.Summary, canvas => RenderBarChart(
            canvas,
            "Stage-wise summary",
            null,
            data.Summary.StageSummary,
            ThemeAccent.Primary,
            data.Summary.ProjectCount,
            showShare: true)));

        plans.Add(new SlidePlan(SlidePlanKind.Summary, canvas => RenderStageSummaryTable(canvas, data)));
    }

    private static void AddSummaryChartSlides(
        List<SlidePlan> plans,
        ProjectBriefingPresentationData data,
        string title,
        IReadOnlyList<ProjectBriefingSummaryPoint> points,
        ThemeAccent accent)
    {
        var chunks = points.Count == 0
            ? new[] { Array.Empty<ProjectBriefingSummaryPoint>() }
            : points.Chunk(10).Select(chunk => chunk.ToArray()).ToArray();

        for (var index = 0; index < chunks.Length; index++)
        {
            var captured = chunks[index];
            var capturedIndex = index;
            plans.Add(new SlidePlan(SlidePlanKind.Summary, canvas => RenderBarChart(
                canvas,
                title + (chunks.Length > 1 ? $" ({capturedIndex + 1}/{chunks.Length})" : string.Empty),
                null,
                captured,
                accent)));
        }
    }

    private static void RenderBarChart(
        SlideCanvas canvas,
        string title,
        string? subtitle,
        IReadOnlyList<ProjectBriefingSummaryPoint> points,
        ThemeAccent accentRole,
        int total = 0,
        bool showShare = false)
    {
        AddSlideTitle(canvas, title, subtitle);
        var accent = ResolveAccent(canvas.Theme, accentRole);
        if (points.Count == 0)
        {
            AddEmptyMessage(canvas, "No summary data is available for the selected projects.");
            return;
        }

        var maximum = Math.Max(1, points.Max(point => point.Count));
        const double chartTop = 1.16;
        const double chartHeight = 5.56;
        var rowHeight = Math.Min(.64, chartHeight / points.Count);
        var usedHeight = rowHeight * points.Count;
        var startY = chartTop + Math.Max(0, (chartHeight - usedHeight) / 2d);
        var labelFont = points.Count switch
        {
            <= 6 => 13.0,
            <= 9 => 12.0,
            <= 12 => 11.2,
            _ => 10.4
        };
        var barHeight = Math.Clamp(rowHeight * .58, .22, .34);

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var y = startY + (index * rowHeight);
            var barWidth = 7.05 * point.Count / maximum;
            canvas.AddText(.72, y, 3.15, rowHeight, Truncate(point.Label, 42), labelFont, canvas.Theme.TextPrimary, true, "l");
            canvas.AddRoundedRect(3.90, y + ((rowHeight - barHeight) / 2), 7.25, barHeight, canvas.Theme.SurfaceMuted, canvas.Theme.SurfaceMuted, .04);
            canvas.AddRoundedRect(3.90, y + ((rowHeight - barHeight) / 2), Math.Max(.16, barWidth), barHeight, accent, accent, .04);
            canvas.AddText(11.30, y, .50, rowHeight, point.Count.ToString(CultureInfo.InvariantCulture), 12.2, canvas.Theme.TextPrimary, true, "r");
            if (showShare && total > 0)
            {
                var share = point.Count * 100d / total;
                canvas.AddText(11.86, y, .55, rowHeight, $"{share:0.#}%", 10.2, canvas.Theme.TextMuted, false, "r");
            }
        }
    }

    private static void RenderStageSummaryTable(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data)
    {
        AddSlideTitle(canvas, "Stage-wise project distribution");
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
                Cell("PRESENT STAGE", 11.5, canvas.Theme.TextOnAccent, true, "l", canvas.Theme.TableHeader),
                Cell("PROJECTS", 11.5, canvas.Theme.TextOnAccent, true, "r", canvas.Theme.TableHeader),
                Cell("SHARE", 11.5, canvas.Theme.TextOnAccent, true, "r", canvas.Theme.TableHeader)
            }
        };

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var fill = index % 2 == 0 ? canvas.Theme.TableRow : canvas.Theme.TableAlternateRow;
            var share = data.Summary.ProjectCount == 0
                ? "0%"
                : $"{point.Count * 100d / data.Summary.ProjectCount:0.#}%";
            rows.Add(new[]
            {
                Cell(point.Label, 12.0, canvas.Theme.TextPrimary, true, "l", fill),
                Cell(point.Count.ToString(CultureInfo.InvariantCulture), 12.0, canvas.Theme.TextPrimary, true, "r", fill),
                Cell(share, 11.8, canvas.Theme.TextMuted, false, "r", fill)
            });
        }

        rows.Add(new[]
        {
            Cell("TOTAL SELECTED PROJECTS", 12.0, canvas.Theme.TextPrimary, true, "l", canvas.Theme.AccentSoft),
            Cell(data.Summary.ProjectCount.ToString(CultureInfo.InvariantCulture), 12.0, canvas.Theme.TextPrimary, true, "r", canvas.Theme.AccentSoft),
            Cell("100%", 12.0, canvas.Theme.TextPrimary, true, "r", canvas.Theme.AccentSoft)
        });

        var bodyRows = rows.Count - 1;
        const double availableHeight = 5.70;
        const double headerHeight = .48;
        var rowHeight = Math.Min(.46, (availableHeight - headerHeight) / bodyRows);
        var heights = new List<double> { headerHeight };
        heights.AddRange(Enumerable.Repeat(rowHeight, bodyRows));
        canvas.AddNativeTable(1.02, 1.10, new[] { 8.05, 1.75, 1.55 }, heights, rows, "Stage-wise project distribution table");
    }

    private static void AddExecutiveTableSlides(List<SlidePlan> plans, ProjectBriefingPresentationData data)
    {
        var projects = OrderProjects(data.Projects).ToArray();
        var pages = ProjectBriefingTablePagination.Paginate(
            projects,
            data.CostMode,
            project => ProjectBriefingTablePagination.Measure(
                project.ProjectName,
                project.PresentStage,
                project.ExternalStatus,
                project.CostRd.IsAvailable && !string.IsNullOrWhiteSpace(project.CostRd.BasisDisplay),
                hasProliferationCostBasis: false));

        for (var index = 0; index < pages.Count; index++)
        {
            var captured = pages[index];
            var capturedIndex = index;
            plans.Add(new SlidePlan(SlidePlanKind.Summary, canvas => RenderExecutiveTable(
                canvas,
                data,
                captured.Items,
                captured.RowHeights,
                capturedIndex + 1,
                pages.Count)));
        }
    }

    private static void RenderExecutiveTable(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        IReadOnlyList<ProjectBriefingPresentationProject> projects,
        IReadOnlyList<double> rowHeights,
        int page,
        int pages)
    {
        AddSlideTitle(
            canvas,
            "Project status summary" + (pages > 1 ? $" ({page}/{pages})" : string.Empty));

        var headers = new List<string> { "PROJECT" };
        var widths = new List<double>();
        switch (data.CostMode)
        {
            case ProjectBriefingCostMode.Both:
                headers.AddRange(new[] { "COST (R&D)", "PROLIFERATION COST", "PRESENT STAGE", "STATUS" });
                widths.AddRange(new[] { 2.65, 1.25, 1.35, 1.65, 5.25 });
                break;
            case ProjectBriefingCostMode.CostRdOnly:
                headers.AddRange(new[] { "COST (R&D)", "PRESENT STAGE", "STATUS" });
                widths.AddRange(new[] { 2.95, 1.40, 1.90, 5.90 });
                break;
            case ProjectBriefingCostMode.ProliferationOnly:
                headers.AddRange(new[] { "PROLIFERATION COST", "PRESENT STAGE", "STATUS" });
                widths.AddRange(new[] { 2.95, 1.55, 1.90, 5.75 });
                break;
            default:
                headers.AddRange(new[] { "PRESENT STAGE", "STATUS" });
                widths.AddRange(new[] { 3.10, 1.95, 7.10 });
                break;
        }

        var rows = new List<IReadOnlyList<NativeTableCell>>
        {
            headers.Select(value => Cell(value, 10.2, canvas.Theme.TextOnAccent, true, "l", canvas.Theme.TableHeader)).ToArray()
        };

        for (var index = 0; index < projects.Count; index++)
        {
            var project = projects[index];
            var rowFill = index % 2 == 0 ? canvas.Theme.TableRow : canvas.Theme.TableAlternateRow;
            var costFill = canvas.Theme.SurfaceMuted;
            var cells = new List<NativeTableCell>
            {
                Cell(Truncate(project.ProjectName, 68), 10.7, canvas.Theme.TextPrimary, true, "l", rowFill)
            };

            if (data.CostMode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both)
            {
                cells.Add(Cell(
                    CostCell(project.CostRd, "Not recorded", includeBasis: true),
                    10.0,
                    project.CostRd.IsAvailable ? canvas.Theme.TextPrimary : canvas.Theme.TextMuted,
                    project.CostRd.IsAvailable,
                    "r",
                    costFill));
            }
            if (data.CostMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both)
            {
                cells.Add(Cell(
                    CostCell(project.ProliferationCost, "Not recorded", includeBasis: false),
                    10.0,
                    project.ProliferationCost.IsAvailable ? canvas.Theme.TextPrimary : canvas.Theme.TextMuted,
                    project.ProliferationCost.IsAvailable,
                    "r",
                    costFill));
            }

            cells.Add(Cell(Truncate(project.PresentStage, 42), 10.2, canvas.Theme.TextPrimary, false, "l", rowFill));
            var executiveStatus = ExecutiveStatus(project.ExternalStatus);
            cells.Add(Cell(
                Truncate(executiveStatus, data.CostMode == ProjectBriefingCostMode.Both ? 225 : 265),
                10.1,
                string.Equals(executiveStatus, "Not recorded", StringComparison.Ordinal) ? canvas.Theme.TextMuted : canvas.Theme.TextPrimary,
                false,
                "l",
                rowFill));
            rows.Add(cells);
        }

        var minimumDisplayHeight = projects.Count == 0
            ? .60
            : Math.Min(.78, ProjectBriefingTablePagination.AvailableBodyHeight / projects.Count);
        var displayRowHeights = rowHeights
            .Select(height => Math.Max(height, minimumDisplayHeight))
            .ToArray();
        var totalDisplayHeight = displayRowHeights.Sum();
        if (totalDisplayHeight > ProjectBriefingTablePagination.AvailableBodyHeight)
        {
            var scale = ProjectBriefingTablePagination.AvailableBodyHeight / totalDisplayHeight;
            displayRowHeights = displayRowHeights.Select(height => height * scale).ToArray();
        }

        var heights = new List<double> { .48 };
        heights.AddRange(displayRowHeights);
        canvas.AddNativeTable(.58, 1.06, widths, heights, rows, "Project status summary table");
    }

    private static string CostCell(
        ProjectBriefingCostValue value,
        string missing,
        bool includeBasis)
        => value.IsAvailable
            ? includeBasis && !string.IsNullOrWhiteSpace(value.BasisDisplay)
                ? $"{value.DisplayValue}\n{value.BasisDisplay}"
                : value.DisplayValue
            : missing;

    private static string ExecutiveStatus(string? value)
        => string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "No external status recorded", StringComparison.Ordinal)
                ? "Not recorded"
                : value.Trim();

    private static void RenderProjectDetail(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project,
        ProjectBriefingCapabilityPage capabilityPage)
    {
        AddSlideTitle(canvas, Truncate(project.ProjectName, 82), $"{project.LifecycleDisplay} · {CategoryLine(project)}");

        const double leftX = .60;
        const double leftWidth = 4.40;
        const double rightX = 5.25;
        const double rightWidth = 7.48;
        const double contentTop = 1.28;
        const double contentBottom = 6.70;
        const double sectionGap = .18;

        var hasPhoto = project.CoverPhoto is { Length: > 0 };
        var costCards = CostCards(canvas, data.CostMode, project);
        var costHeight = costCards.Count == 0 ? 0d : 1.03;
        var costY = costCards.Count == 0 ? contentBottom : contentBottom - costHeight;
        var positionBottom = costCards.Count == 0 ? contentBottom : costY - sectionGap;
        var layout = CalculateDetailedLayout(project.ExternalStatus, hasPhoto, contentTop, positionBottom, sectionGap);

        canvas.AddRoundedRect(leftX, contentTop, leftWidth, layout.PhotoHeight, canvas.Theme.Surface, canvas.Theme.Border, .08);
        if (hasPhoto)
        {
            var imageHeight = layout.PhotoHeight - .20;
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
            canvas.AddRect(leftX + .10, contentTop + .10, leftWidth - .20, layout.PhotoHeight - .20, canvas.Theme.Placeholder, canvas.Theme.Border);
            canvas.AddRect(leftX + 1.70, contentTop + .26, 1.00, .05, canvas.Theme.Divider);
            canvas.AddRect(leftX + 1.87, contentTop + .43, .66, .05, canvas.Theme.Border);
            canvas.AddText(leftX + .45, contentTop + .51, leftWidth - .90, .20,
                "PHOTOGRAPH NOT AVAILABLE", 8.7, canvas.Theme.TextMuted, true, "ctr");
        }

        var statusY = contentTop + layout.PhotoHeight + sectionGap;
        AddPresentStatusCard(canvas, leftX, statusY, leftWidth, positionBottom - statusY, project);

        if (costCards.Count > 0)
        {
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

        RenderCapabilityPanel(
            canvas,
            rightX,
            contentTop,
            rightWidth,
            contentBottom - contentTop,
            "CAPABILITY OVERVIEW",
            capabilityPage);
    }

    private static void RenderCapabilityContinuation(
        SlideCanvas canvas,
        ProjectBriefingPresentationProject project,
        ProjectBriefingCapabilityPage capabilityPage,
        int continuationPage,
        int continuationPages)
    {
        AddSlideTitle(
            canvas,
            Truncate(project.ProjectName, 82),
            $"{project.LifecycleDisplay} · {CategoryLine(project)}");

        var heading = continuationPages > 1
            ? $"CAPABILITY OVERVIEW — CONTINUED ({continuationPage}/{continuationPages})"
            : "CAPABILITY OVERVIEW — CONTINUED";

        RenderCapabilityPanel(
            canvas,
            .60,
            1.28,
            12.13,
            5.42,
            heading,
            capabilityPage);
    }

    private static void RenderCapabilityPanel(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        double height,
        string heading,
        ProjectBriefingCapabilityPage page)
    {
        canvas.AddRoundedRect(x, y, width, height, canvas.Theme.Surface, canvas.Theme.Border, .08);
        canvas.AddRect(x, y, .08, height, canvas.Theme.SecondaryAccent);
        canvas.AddText(x + .32, y + .22, width - .62, .28, heading, 10.5, canvas.Theme.SecondaryAccent, true, "l");

        RenderCapabilityBlocks(
            canvas,
            x + .32,
            y + .66,
            width - .64,
            page.Blocks);
    }

    private static void RenderCapabilityBlocks(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        IReadOnlyList<ProjectBriefingCapabilityLayoutBlock> blocks)
    {
        var cursorY = y;
        foreach (var block in blocks)
        {
            var textColor = block.IsMuted ? canvas.Theme.TextMuted : canvas.Theme.TextPrimary;
            var bold = false;
            var textX = x;
            var textWidth = width;
            var text = block.Text;

            switch (block.Type)
            {
                case ProjectBriefingCapabilityBlockType.Heading:
                    textColor = canvas.Theme.SecondaryAccent;
                    bold = true;
                    break;

                case ProjectBriefingCapabilityBlockType.Bullet:
                case ProjectBriefingCapabilityBlockType.NumberedItem:
                case ProjectBriefingCapabilityBlockType.LetteredItem:
                    textX += .30;
                    textWidth -= .30;
                    if (!block.IsContinuation && !string.IsNullOrWhiteSpace(block.Marker))
                    {
                        canvas.AddText(
                            x,
                            cursorY,
                            .26,
                            block.TextHeight,
                            block.Marker!,
                            block.FontSize,
                            textColor,
                            block.Type != ProjectBriefingCapabilityBlockType.Bullet,
                            "l",
                            "t");
                    }
                    break;
            }

            canvas.AddText(
                textX,
                cursorY,
                textWidth,
                block.TextHeight,
                text,
                block.FontSize,
                textColor,
                bold,
                "l",
                "t");

            cursorY += block.TotalHeight;
        }
    }

    private static DetailedSlideLayout CalculateDetailedLayout(
        string? externalStatus,
        bool hasPhoto,
        double contentTop,
        double positionBottom,
        double sectionGap)
    {
        var normalized = NormalizePresentationText(externalStatus);
        var estimatedLines = Math.Clamp(EstimateWrappedLines(normalized, 48), 1, 6);
        var desiredStatusHeight = 1.50 + (estimatedLines * .205);
        var available = Math.Max(2.2, positionBottom - contentTop - sectionGap);
        var minimumPhoto = hasPhoto ? 1.48 : .78;
        var maximumPhoto = hasPhoto ? 2.35 : .92;
        var photoHeight = Math.Clamp(available - desiredStatusHeight, minimumPhoto, maximumPhoto);
        return new DetailedSlideLayout(photoHeight);
    }

    private static void AddPresentStatusCard(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        double height,
        ProjectBriefingPresentationProject project)
    {
        canvas.AddRoundedRect(x, y, width, height, canvas.Theme.Surface, canvas.Theme.Border, .08);
        canvas.AddText(x + .25, y + .18, width - .50, .22, "PRESENT STATUS", 9.5, canvas.Theme.Accent, true, "l");
        canvas.AddText(x + .25, y + .48, width - .50, .18, "PRESENT STAGE", 7.8, canvas.Theme.TextMuted, true, "l");
        canvas.AddRoundedRect(x + .25, y + .70, Math.Min(width - .50, 2.92), .38, canvas.Theme.AccentSoft, canvas.Theme.Accent, .06);
        canvas.AddText(x + .37, y + .74, Math.Min(width - .74, 2.66), .28,
            TruncateAtWord(project.PresentStage, 42), 10.3, canvas.Theme.TextPrimary, true, "l");

        var statusLabel = project.ExternalStatusDate.HasValue
            ? $"STATUS · {project.ExternalStatusDate.Value:dd MMM yyyy}"
            : "STATUS";
        canvas.AddText(x + .25, y + 1.18, width - .50, .20, statusLabel, 8.2, canvas.Theme.Accent, true, "l");
        var statusHeight = Math.Max(.30, height - 1.47);
        var availableLines = Math.Max(1, (int)Math.Floor(statusHeight / .205));
        var normalized = NormalizePresentationText(project.ExternalStatus);
        var maximumCharacters = Math.Max(56, availableLines * 50);
        var fitted = TruncateAtWord(normalized, maximumCharacters);
        var estimatedLines = EstimateWrappedLines(fitted, 48);
        var statusFont = estimatedLines switch
        {
            <= 2 => 10.2,
            <= 4 => 9.5,
            _ => 8.9
        };
        canvas.AddText(x + .25, y + 1.43, width - .50, statusHeight,
            fitted, statusFont, canvas.Theme.TextPrimary, false, "l", "t");
    }

    private static string NormalizePresentationText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\uF0B7', '•')
            .Replace('', '•')
            .Replace('◦', '•')
            .Trim();
    }

    private static int EstimateWrappedLines(string? value, int charactersPerLine)
    {
        if (string.IsNullOrWhiteSpace(value)) return 1;
        return value
            .Split('\n')
            .Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (double)Math.Max(1, charactersPerLine))));
    }

    private static string TruncateAtWord(string? value, int maximum)
    {
        var text = NormalizePresentationText(value);
        if (text.Length <= maximum) return text;
        var candidate = text[..Math.Max(1, maximum - 1)].TrimEnd();
        var lastSpace = candidate.LastIndexOfAny(new[] { ' ', '\n', '\t' });
        if (lastSpace > maximum * .72) candidate = candidate[..lastSpace].TrimEnd();
        return candidate + "…";
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
        SlideCanvas canvas,
        ProjectBriefingCostMode mode,
        ProjectBriefingPresentationProject project)
    {
        var result = new List<CostCard>();
        if (mode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both)
        {
            result.Add(new CostCard(
                "COST (R&D)",
                project.CostRd.IsAvailable ? project.CostRd.DisplayValue : "Not recorded",
                canvas.Theme.Accent,
                canvas.Theme.AccentSoft,
                project.CostRd.IsAvailable ? project.CostRd.BasisDisplay : string.Empty));
        }
        if (mode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both)
        {
            var proliferationAvailable = project.ProliferationCost.IsAvailable;
            result.Add(new CostCard(
                "PROLIFERATION COST",
                proliferationAvailable ? project.ProliferationCost.DisplayValue : "Not recorded",
                proliferationAvailable ? canvas.Theme.Positive : canvas.Theme.TextMuted,
                proliferationAvailable ? canvas.Theme.PositiveSoft : canvas.Theme.SurfaceMuted,
                proliferationAvailable ? "Indicative" : null));
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
        canvas.AddRoundedRect(x, y, width, height, fill, canvas.Theme.Border, .08);
        canvas.AddRect(x, y, .06, height, accent);
        canvas.AddText(x + .20, y + .14, width - .38, .22, title, width < 2.3 ? 8.0 : 9.2, accent, true, "l");
        canvas.AddText(x + .20, y + .40, width - .38, .30, Truncate(value, 70), width < 2.3 ? 12.0 : 13.5, canvas.Theme.TextPrimary, true, "l");
        if (!string.IsNullOrWhiteSpace(note))
        {
            canvas.AddText(x + .20, y + height - .23, width - .38, .17, note, 7.8, canvas.Theme.TextMuted, false, "l");
        }
    }

    private static void AddSlideTitle(SlideCanvas canvas, string title, string? subtitle = null)
    {
        canvas.AddRect(0, 0, SlideWidth, SlideHeight, canvas.Theme.Canvas);
        canvas.AddRect(0, 0, SlideWidth, .10, canvas.Theme.Accent);
        canvas.AddBrandingImages(HeaderVariant.Standard);

        var titleX = canvas.ShowBranding ? 1.30 : .62;
        var titleWidth = canvas.ShowBranding ? 10.73 : 11.40;
        var titleAlign = canvas.ShowBranding ? "ctr" : "l";
        canvas.AddText(
            titleX,
            .27,
            titleWidth,
            .44,
            title,
            SlideTitleFontSize(title),
            canvas.Theme.TextPrimary,
            true,
            titleAlign);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            canvas.AddText(titleX, .72, titleWidth, .22, subtitle, 10.0, canvas.Theme.TextMuted, false, titleAlign);
        }
        canvas.AddLine(.62, 1.00, 12.72, 1.00, canvas.Theme.Divider, .55);
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

        canvas.AddLine(.62, 7.05, 12.72, 7.05, canvas.Theme.Divider, .55);
        canvas.AddText(.65, 7.12, 5.55, .18, "SIMULATOR DEVELOPMENT DIVISION", 7.5, canvas.Theme.TextMuted, true, "l");
        if (!string.IsNullOrWhiteSpace(data.HandlingMarking))
        {
            canvas.AddText(4.55, 7.12, 4.25, .18, data.HandlingMarking!, 7.5, canvas.Theme.Critical, true, "ctr");
        }
        canvas.AddText(10.35, 7.12, 2.35, .18, $"{slideNumber}/{slideCount}", 7.5, canvas.Theme.TextMuted, true, "r");
    }

    private static void AddEmptyMessage(SlideCanvas canvas, string message)
    {
        canvas.AddRoundedRect(1.12, 2.25, 11.05, 2.15, canvas.Theme.Surface, canvas.Theme.Border, .08);
        canvas.AddText(1.55, 3.03, 10.20, .55, message, 17, canvas.Theme.TextMuted, false, "ctr");
    }

    private static NativeTableCell Cell(
        string? value,
        double fontSize,
        string color,
        bool bold,
        string align,
        string fill)
        => new(value ?? string.Empty, fontSize, color, bold, align, fill);

    private static string Truncate(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximum
            ? normalized
            : normalized[..Math.Max(1, maximum - 1)].TrimEnd() + "…";
    }

    private static double SlideTitleFontSize(string title)
        => title.Length switch
        {
            <= 48 => 22.5,
            <= 66 => 20.0,
            <= 82 => 17.5,
            _ => 16.0
        };

    private static double CoverTitleFontSize(string title)
        => title.Length switch
        {
            <= 34 => 34,
            <= 52 => 30,
            <= 75 => 26,
            _ => 22
        };

    private static byte[]? ReadAsset(string? path)
        => string.IsNullOrWhiteSpace(path) || !File.Exists(path)
            ? null
            : File.ReadAllBytes(path);

    private static bool ShouldShowBranding(
        ProjectBriefingBrandingScope scope,
        SlidePlanKind kind)
        => scope switch
        {
            ProjectBriefingBrandingScope.None => false,
            ProjectBriefingBrandingScope.CoverAndSummary => kind is SlidePlanKind.Cover or SlidePlanKind.Summary,
            ProjectBriefingBrandingScope.AllSlides => true,
            _ => false
        };

    private static string ResolveAccent(ProjectBriefingThemeDefinition theme, ThemeAccent accent)
        => accent switch
        {
            ThemeAccent.Secondary => theme.SecondaryAccent,
            ThemeAccent.Positive => theme.Positive,
            _ => theme.Accent
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

    private enum ThemeAccent
    {
        Primary,
        Secondary,
        Positive
    }

    private enum SlidePlanKind
    {
        Cover,
        Summary,
        Project
    }

    private enum HeaderVariant
    {
        Cover,
        Standard
    }

    private sealed record DetailedSlideLayout(double PhotoHeight);
    private sealed record CostCard(string Title, string Value, string Accent, string Fill, string? Note);
    private sealed record NativeTableCell(string Value, double FontSize, string Color, bool Bold, string Align, string Fill);
    private sealed record ProjectBriefingBrandingAssets(byte[]? LeftLogo, byte[]? RightLogo);
    private sealed record SlidePlan(SlidePlanKind Kind, Action<SlideCanvas> Render);

    private sealed class SlideCanvas
    {
        private const double NativeTableHorizontalMargin = .11;
        private const double NativeTableVerticalMargin = .04;

        private readonly SlidePart _slidePart;
        private readonly ProjectBriefingBrandingAssets _branding;
        private readonly List<string> _elements = new();
        private int _nextShapeId = 2;

        public SlideCanvas(
            SlidePart slidePart,
            ProjectBriefingThemeDefinition theme,
            ProjectBriefingBrandingAssets branding,
            bool showBranding)
        {
            _slidePart = slidePart;
            Theme = theme;
            _branding = branding;
            ShowBranding = showBranding;
        }

        public ProjectBriefingThemeDefinition Theme { get; }
        public bool ShowBranding { get; }

        public void AddRect(double x, double y, double width, double height, string fill, string? line = null, double lineWidth = .75)
            => AddShape(x, y, width, height, fill, line, lineWidth, "rect", null, 0, Theme.TextPrimary, false, "l");

        public void AddRoundedRect(double x, double y, double width, double height, string fill, string? line, double radius)
            => AddShape(x, y, width, height, fill, line, .75, "roundRect", null, 0, Theme.TextPrimary, false, "l");

        public void AddBrandingImages(HeaderVariant variant)
        {
            if (!ShowBranding) return;

            if (variant == HeaderVariant.Cover)
            {
                if (_branding.LeftLogo is { Length: > 0 })
                {
                    AddRoundedRect(.24, .13, .78, .78, Theme.BrandingPlate, Theme.BrandingPlateBorder, .06);
                    AddImageContained(_branding.LeftLogo, .32, .20, .62, .62, "Left formation insignia");
                }
                if (_branding.RightLogo is { Length: > 0 })
                {
                    AddRoundedRect(12.28, .12, .70, .78, Theme.BrandingPlate, Theme.BrandingPlateBorder, .06);
                    AddImageContained(_branding.RightLogo, 12.35, .17, .56, .68, "Right division insignia");
                }
                return;
            }

            if (_branding.LeftLogo is { Length: > 0 })
            {
                if (Theme.IsDark)
                {
                    AddRoundedRect(.24, .13, .64, .64, Theme.BrandingPlate, Theme.BrandingPlateBorder, .05);
                }
                AddImageContained(_branding.LeftLogo, .32, .20, .48, .48, "Left formation insignia");
            }

            if (_branding.RightLogo is { Length: > 0 })
            {
                if (Theme.IsDark)
                {
                    AddRoundedRect(12.40, .11, .54, .66, Theme.BrandingPlate, Theme.BrandingPlateBorder, .05);
                }
                AddImageContained(_branding.RightLogo, 12.49, .16, .36, .56, "Right division insignia");
            }
        }

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
  <a:tcPr marL="{Emu(NativeTableHorizontalMargin)}" marR="{Emu(NativeTableHorizontalMargin)}" marT="{Emu(NativeTableVerticalMargin)}" marB="{Emu(NativeTableVerticalMargin)}" anchor="ctr">
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

        public void AddImageContained(
            byte[]? content,
            double x,
            double y,
            double maximumWidth,
            double maximumHeight,
            string name)
        {
            if (content is null || content.Length == 0) return;

            var (pixelWidth, pixelHeight) = GetImageDimensions(content);
            var aspectRatio = pixelWidth > 0 && pixelHeight > 0
                ? pixelWidth / (double)pixelHeight
                : 1d;
            var width = maximumWidth;
            var height = width / aspectRatio;
            if (height > maximumHeight)
            {
                height = maximumHeight;
                width = height * aspectRatio;
            }

            AddImage(
                content,
                "image/png",
                x + ((maximumWidth - width) / 2d),
                y + ((maximumHeight - height) / 2d),
                width,
                height,
                name);
        }

        private static (int Width, int Height) GetImageDimensions(byte[] content)
        {
            if (content.Length >= 24
                && content[0] == 0x89
                && content[1] == 0x50
                && content[2] == 0x4E
                && content[3] == 0x47)
            {
                var width = (content[16] << 24)
                    | (content[17] << 16)
                    | (content[18] << 8)
                    | content[19];
                var height = (content[20] << 24)
                    | (content[21] << 16)
                    | (content[22] << 8)
                    | content[23];
                return (Math.Max(1, width), Math.Max(1, height));
            }

            return (1, 1);
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
  <p:nvPicPr><p:cNvPr id="{id}" name="{Escape(name)}" descr="{Escape(name)}"/><p:cNvPicPr><a:picLocks noChangeAspect="1"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>
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

        private string TableBorders()
        {
            var line = $"<a:solidFill><a:srgbClr val=\"{CleanColor(Theme.Border)}\"/></a:solidFill><a:prstDash val=\"solid\"/>";
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
            return $"<p:txBody><a:bodyPr wrap=\"square\" vertOverflow=\"clip\" horzOverflow=\"clip\" lIns=\"45720\" rIns=\"45720\" tIns=\"22860\" bIns=\"22860\" anchor=\"{anchor}\"><a:normAutofit fontScale=\"92000\" lnSpcReduction=\"10000\"/></a:bodyPr><a:lstStyle/>{string.Join(string.Empty, paragraphs)}</p:txBody>";
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

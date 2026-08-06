using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.AspNetCore.Hosting;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer : IProjectBriefingSlideComposer
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
            var slideIdList = EnsureSlideIdList(presentationPart.Presentation);

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
                AddFooter(canvas, data, index + 1, plans.Count, plans[index].Kind);
                canvas.Commit();

                slideIdList.Append(new SlideId
                {
                    Id = nextSlideId++,
                    RelationshipId = presentationPart.GetIdOfPart(slidePart)
                });
            }

            presentationPart.Presentation.Save();
            document.PackageProperties.Title = SanitizeOpenXmlText(data.DeckName);
            document.PackageProperties.Subject = data.Layout == ProjectBriefingLayout.ProjectUpdateSheet
                ? "Formal project update sheets"
                : "Professional project briefing deck";
            document.PackageProperties.Creator = "Simulator Development Division";
            document.PackageProperties.LastModifiedBy = "Project Briefing Deck Builder";
            document.PackageProperties.Modified = data.GeneratedAtUtc.UtcDateTime;
        }

        var content = stream.ToArray();
        ProjectBriefingPresentationIntegrityValidator.Validate(content, plans.Count);
        return (content, plans.Count);
    }

    private static List<SlidePlan> BuildPlans(ProjectBriefingPresentationData data)
    {
        var plans = data.Layout == ProjectBriefingLayout.ProjectUpdateSheet
            ? BuildProjectUpdateSheetPlans(data)
            : BuildStandardPlans(data);

        AddConcludingPlans(plans, data);

        // Every exported presentation ends with one deliberate ceremonial closing slide.
        plans.Add(new SlidePlan(
            SlidePlanKind.Closing,
            canvas => RenderClosingSlide(canvas, data)));
        return plans;
    }

    private static void AddIntroductoryPlans(
        ICollection<SlidePlan> plans,
        ProjectBriefingPresentationData data)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(data);

        if (data.IncludeCoverSlide)
        {
            plans.Add(new SlidePlan(SlidePlanKind.Cover, canvas => RenderCover(canvas, data)));
        }

        foreach (var slideType in data.AdditionalSlideOrder.Where(type =>
                     ProjectBriefingAdditionalSlideCatalog.Get(type).Placement
                     == ProjectBriefingAdditionalSlidePlacement.AfterCover))
        {
            switch (slideType)
            {
                case ProjectBriefingAdditionalSlideType.InstitutionalProfile
                    when data.InstitutionalProfile is not null:
                {
                    var institutionalProfile = data.InstitutionalProfile;
                    plans.Add(new SlidePlan(
                        SlidePlanKind.InstitutionalProfile,
                        canvas => RenderInstitutionalProfile(canvas, institutionalProfile)));
                    break;
                }
                case ProjectBriefingAdditionalSlideType.RoleAndCharter
                    when data.RoleCharter is not null:
                {
                    var roleCharter = data.RoleCharter;
                    foreach (var page in PaginateRoleCharter(roleCharter))
                    {
                        var capturedPage = page;
                        plans.Add(new SlidePlan(
                            SlidePlanKind.RoleCharter,
                            canvas => RenderRoleCharter(canvas, roleCharter, capturedPage)));
                    }
                    break;
                }
            }
        }

        if (data.IncludePortfolioSummarySlide)
        {
            plans.Add(new SlidePlan(SlidePlanKind.Summary, canvas => RenderPortfolioSummary(canvas, data)));
        }
    }

    private static void AddConcludingPlans(
        ICollection<SlidePlan> plans,
        ProjectBriefingPresentationData data)
    {
        foreach (var slideType in data.AdditionalSlideOrder.Where(type =>
                     ProjectBriefingAdditionalSlideCatalog.Get(type).Placement
                     == ProjectBriefingAdditionalSlidePlacement.BeforeClosing))
        {
            if (slideType == ProjectBriefingAdditionalSlideType.FfcGlobalFootprint
                && data.FfcGlobalFootprint is not null)
            {
                var footprint = data.FfcGlobalFootprint;
                plans.Add(new SlidePlan(
                    SlidePlanKind.FfcGlobalFootprint,
                    canvas => RenderFfcGlobalFootprint(canvas, footprint)));

                if (footprint.IncludeCountryWiseBreakdown && footprint.Countries.Count > 0)
                {
                    var pages = footprint.Countries
                        .Chunk(ProjectBriefingFfcGlobalFootprintOptions.CountriesPerBreakdownSlide)
                        .Select(chunk => chunk.ToArray())
                        .ToArray();
                    for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
                    {
                        var capturedCountries = pages[pageIndex];
                        var capturedPageIndex = pageIndex;
                        plans.Add(new SlidePlan(
                            SlidePlanKind.FfcGlobalFootprint,
                            canvas => RenderFfcCountryWiseBreakdownSlide(
                                canvas,
                                footprint,
                                capturedCountries,
                                capturedPageIndex + 1,
                                pages.Length)));
                    }
                }
            }
        }
    }

    private static List<SlidePlan> BuildStandardPlans(ProjectBriefingPresentationData data)
    {
        var orderedProjects = OrderProjects(data.Projects);
        var plans = new List<SlidePlan>();
        AddIntroductoryPlans(plans, data);

        if (data.IncludeStageSummary)
        {
            AddStageSummarySlides(plans, data);
        }

        if (data.IncludeProjectCategorySummary
            && ProjectBriefingSummaryPlanning.ShouldRenderCategorySummary(
                data.Summary.ProjectCategorySummary.Count(point => point.Count > 0)))
        {
            AddSummaryChartSlides(
                plans,
                data,
                "Project-category summary",
                data.Summary.ProjectCategorySummary,
                ThemeAccent.Secondary);
        }

        if (data.IncludeTechnicalCategorySummary
            && ProjectBriefingSummaryPlanning.ShouldRenderCategorySummary(
                data.Summary.TechnicalCategorySummary.Count(point => point.Count > 0)))
        {
            AddSummaryChartSlides(
                plans,
                data,
                "Technical-category summary",
                data.Summary.TechnicalCategorySummary,
                ThemeAccent.Primary);
        }

        if (data.PresentationMode is ProjectBriefingPresentationMode.ExecutiveTable
            or ProjectBriefingPresentationMode.Combined)
        {
            AddExecutiveTableSlides(plans, data, orderedProjects);
        }

        if (data.PresentationMode is ProjectBriefingPresentationMode.DetailedProjects
            or ProjectBriefingPresentationMode.Combined)
        {
            var includeCapabilities = data.NarrativeMode is ProjectBriefingNarrativeMode.CapabilityOverview
                or ProjectBriefingNarrativeMode.Both;
            var includeProjectBrief = data.NarrativeMode is ProjectBriefingNarrativeMode.ProjectBrief
                or ProjectBriefingNarrativeMode.Both;

            foreach (var project in orderedProjects)
            {
                var capturedProject = project;
                if (includeCapabilities)
                {
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

                if (includeProjectBrief)
                {
                    plans.Add(new SlidePlan(SlidePlanKind.Project, canvas =>
                        RenderProjectBrief(canvas, data, capturedProject)));
                }
            }
        }

        return plans;
    }

    private static IReadOnlyList<ProjectBriefingPresentationProject> OrderProjects(
        IEnumerable<ProjectBriefingPresentationProject> projects)
        => ProjectBriefingProjectOrdering.OrderProjects(projects);

    private static void RenderClosingSlide(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data)
    {
        var theme = canvas.Theme;
        var closingText = data.ClosingSlideType == ProjectBriefingClosingSlideType.ThankYou
            ? "THANK YOU"
            : "JAI HIND";

        canvas.AddRect(0, 0, SlideWidth, SlideHeight, theme.CoverCanvas, name: "Closing slide canvas");
        canvas.AddRect(0, 0, SlideWidth, .10, theme.HeaderAccent, name: "Closing slide top accent");
        canvas.AddRect(0, 7.40, SlideWidth, .10, theme.HeaderAccent, name: "Closing slide bottom accent");
        canvas.AddBrandingImages(HeaderVariant.Closing);

        // The closing slide intentionally avoids the normal deck dividers and dashboard-like
        // card treatment. A wider, near-rectangular ceremonial field gives the final message
        // visual authority while remaining consistent with the institutional maroon palette.
        canvas.AddSubtleRoundedRect(
            .78,
            1.48,
            11.77,
            4.55,
            theme.HeaderAccent,
            theme.HeaderAccent,
            "Closing ceremonial panel");

        canvas.AddText(
            1.48,
            2.52,
            10.37,
            1.08,
            closingText,
            closingText.Length > 8 ? 39.0 : 44.0,
            "FFFFFF",
            true,
            "ctr",
            name: "Closing message");

        // A short, fine tricolour rule provides ceremonial emphasis without resembling
        // a progress indicator or introducing non-editable artwork.
        const double ruleY = 4.08;
        const double segmentWidth = 1.10;
        const double ruleHeight = .040;
        canvas.AddRect(5.02, ruleY, segmentWidth, ruleHeight, "FF9933", name: "Closing saffron accent");
        canvas.AddRect(6.12, ruleY, segmentWidth, ruleHeight, "F7F7F5", name: "Closing white accent");
        canvas.AddRect(7.22, ruleY, segmentWidth, ruleHeight, "138808", name: "Closing green accent");

        canvas.AddText(
            2.02,
            4.48,
            9.29,
            .30,
            "SIMULATOR DEVELOPMENT DIVISION",
            11.5,
            "F5ECEF",
            true,
            "ctr",
            name: "Closing organisation");
    }

    private static void RenderCover(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data)
    {
        var theme = canvas.Theme;
        canvas.AddRect(0, 0, SlideWidth, SlideHeight, theme.CoverCanvas);
        canvas.AddRect(0, 0, SlideWidth, .10, theme.HeaderAccent);
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
        => RenderExecutivePortfolioSummary(canvas, data);

    private static void AddStageSummarySlides(
        List<SlidePlan> plans,
        ProjectBriefingPresentationData data)
    {
        plans.Add(new SlidePlan(
            SlidePlanKind.Summary,
            canvas => RenderStageWiseExecutiveSummary(canvas, data)));

        if (data.StandardSlideOptions.IncludeStageDistributionTable)
        {
            plans.Add(new SlidePlan(SlidePlanKind.Summary, canvas => RenderStageSummaryTable(canvas, data)));
        }
    }

    private static void AddSummaryChartSlides(
        List<SlidePlan> plans,
        ProjectBriefingPresentationData data,
        string title,
        IReadOnlyList<ProjectBriefingSummaryPoint> points,
        ThemeAccent accent)
    {
        var pages = ProjectBriefingSummaryPlanning.PaginateCategories(points);
        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var capturedPage = pages[pageIndex];
            var capturedPageIndex = pageIndex;
            plans.Add(new SlidePlan(
                SlidePlanKind.Summary,
                canvas => RenderAdaptiveCategorySummary(
                    canvas,
                    title,
                    capturedPage,
                    data.Summary.ProjectCount,
                    points.Count(point => point.Count > 0),
                    accent,
                    capturedPageIndex + 1,
                    pages.Count)));
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

        var bodyFontSize = points.Count switch
        {
            >= 16 => 9.2,
            >= 13 => 9.8,
            >= 10 => 10.5,
            _ => 12.0
        };

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var fill = index % 2 == 0 ? canvas.Theme.TableRow : canvas.Theme.TableAlternateRow;
            var isZero = point.Count == 0;
            var textColour = isZero ? canvas.Theme.TextMuted : canvas.Theme.TextPrimary;
            var share = data.Summary.ProjectCount == 0
                ? "0%"
                : $"{point.Count * 100d / data.Summary.ProjectCount:0.#}%";
            rows.Add(new[]
            {
                Cell(point.Label, bodyFontSize, textColour, !isZero, "l", fill),
                Cell(point.Count.ToString(CultureInfo.InvariantCulture), bodyFontSize, textColour, !isZero, "r", fill),
                Cell(share, Math.Max(8.8, bodyFontSize - .2), canvas.Theme.TextMuted, false, "r", fill)
            });
        }

        var totalFontSize = Math.Max(9.4, bodyFontSize);
        rows.Add(new[]
        {
            Cell("TOTAL SELECTED PROJECTS", totalFontSize, canvas.Theme.TextPrimary, true, "l", canvas.Theme.AccentSoft),
            Cell(data.Summary.ProjectCount.ToString(CultureInfo.InvariantCulture), totalFontSize, canvas.Theme.TextPrimary, true, "r", canvas.Theme.AccentSoft),
            Cell(data.Summary.ProjectCount == 0 ? "0%" : "100%", totalFontSize, canvas.Theme.TextPrimary, true, "r", canvas.Theme.AccentSoft)
        });

        var bodyRows = rows.Count - 1;
        const double availableHeight = 5.70;
        const double headerHeight = .42;
        var rowHeight = Math.Min(.46, (availableHeight - headerHeight) / bodyRows);
        var heights = new List<double> { headerHeight };
        heights.AddRange(Enumerable.Repeat(rowHeight, bodyRows));
        canvas.AddNativeTable(1.02, 1.10, new[] { 8.05, 1.75, 1.55 }, heights, rows, "Stage-wise project distribution table");
    }

    private static void AddExecutiveTableSlides(
        List<SlidePlan> plans,
        ProjectBriefingPresentationData data,
        IReadOnlyList<ProjectBriefingPresentationProject> projects)
    {
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
                widths.AddRange(new[] { 2.50, 1.30, 1.40, 1.65, 5.30 });
                break;
            case ProjectBriefingCostMode.CostRdOnly:
                headers.AddRange(new[] { "COST (R&D)", "PRESENT STAGE", "STATUS" });
                widths.AddRange(new[] { 2.80, 1.45, 1.95, 5.95 });
                break;
            case ProjectBriefingCostMode.ProliferationOnly:
                headers.AddRange(new[] { "PROLIFERATION COST", "PRESENT STAGE", "STATUS" });
                widths.AddRange(new[] { 2.80, 1.60, 1.95, 5.80 });
                break;
            default:
                headers.AddRange(new[] { "PRESENT STAGE", "STATUS" });
                widths.AddRange(new[] { 2.95, 2.00, 7.20 });
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
                    "l",
                    costFill));
            }
            if (data.CostMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both)
            {
                cells.Add(Cell(
                    CostCell(project.ProliferationCost, "Not recorded", includeBasis: false),
                    10.0,
                    project.ProliferationCost.IsAvailable ? canvas.Theme.TextPrimary : canvas.Theme.TextMuted,
                    project.ProliferationCost.IsAvailable,
                    "l",
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

        var heights = new List<double> { .43 };
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
        var layout = CalculateDetailedLayout(
            project,
            hasPhoto,
            contentTop,
            positionBottom,
            sectionGap,
            data.StandardSlideOptions.ShowPresentStage,
            data.StandardSlideOptions.ShowPresentStatus,
            statusCharactersPerLine: 48,
            minimumPhotoHeight: 1.48,
            minimumPlaceholderHeight: 1.20,
            maximumContextHeight: 2.78);

        AddProjectPhoto(
            canvas,
            project,
            leftX,
            contentTop,
            leftWidth,
            layout.PhotoHeight,
            "Project photograph frame");

        if (layout.ContextHeight > 0)
        {
            var contextY = contentTop + layout.PhotoHeight + sectionGap;
            AddProjectContextCard(
                canvas,
                leftX,
                contextY,
                leftWidth,
                layout.ContextHeight,
                project,
                data.StandardSlideOptions.ShowPresentStage,
                data.StandardSlideOptions.ShowPresentStatus);
        }

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

    private static void RenderProjectBrief(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
    {
        AddSlideTitle(
            canvas,
            Truncate(project.ProjectName, 82));

        var layout = ResolveProjectBriefLayout(data, project);
        if (layout == ProjectBriefingProjectBriefLayout.PhotoEmphasis)
        {
            RenderPhotoEmphasisProjectBrief(canvas, data, project);
            return;
        }

        RenderStandardProjectBrief(canvas, data, project);
    }

    private static ProjectBriefingProjectBriefLayout ResolveProjectBriefLayout(
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
    {
        var configured = data.StandardSlideOptions.ProjectBriefLayout;
        if (configured is ProjectBriefingProjectBriefLayout.Standard
            or ProjectBriefingProjectBriefLayout.PhotoEmphasis)
        {
            return configured;
        }

        if (project.CoverPhoto is not { Length: > 0 })
        {
            return ProjectBriefingProjectBriefLayout.Standard;
        }

        var brief = NormalizePresentationText(project.ProjectBrief);
        if (string.IsNullOrWhiteSpace(brief)
            || string.Equals(brief, "Project brief not recorded.", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectBriefingProjectBriefLayout.Standard;
        }

        var threshold = 1_100;
        if (!data.StandardSlideOptions.ShowPresentStatus) threshold += 220;
        if (!data.StandardSlideOptions.ShowPresentStage) threshold += 60;
        if (data.CostMode == ProjectBriefingCostMode.None) threshold += 140;
        if (data.CostMode == ProjectBriefingCostMode.Both) threshold -= 100;

        var statusLength = data.StandardSlideOptions.ShowPresentStatus
            ? NormalizeExternalStatus(project.ExternalStatus).Length
            : 0;
        if (statusLength > 260) threshold -= Math.Min(260, (statusLength - 260) / 2);

        var paragraphCount = brief
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;

        return brief.Length <= Math.Max(700, threshold) && paragraphCount <= 6
            ? ProjectBriefingProjectBriefLayout.PhotoEmphasis
            : ProjectBriefingProjectBriefLayout.Standard;
    }

    private static void RenderStandardProjectBrief(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
    {
        const double leftX = .60;
        const double leftWidth = 3.65;
        const double rightX = 4.48;
        const double rightWidth = 8.25;
        const double top = 1.28;
        const double bottom = 6.70;
        const double gap = .16;
        const double fullWidth = 12.13;

        var costCards = CostCards(canvas, data.CostMode, project);
        var presentStatus = ResolvePresentStatusValue(
            project,
            data.StandardSlideOptions.ShowPresentStage,
            data.StandardSlideOptions.ShowPresentStatus);
        var stripHeight = ResolveProjectBriefInformationStripHeight(presentStatus, costCards.Count);
        var stripY = stripHeight > 0 ? bottom - stripHeight : bottom;
        var contentBottom = stripHeight > 0 ? stripY - gap : bottom;

        AddProjectPhoto(
            canvas,
            project,
            leftX,
            top,
            leftWidth,
            contentBottom - top,
            "Project brief photograph frame");

        AddProjectBriefPanel(
            canvas,
            rightX,
            top,
            rightWidth,
            contentBottom - top,
            project.ProjectBrief);

        AddProjectBriefInformationStrip(
            canvas,
            leftX,
            stripY,
            fullWidth,
            stripHeight,
            presentStatus,
            costCards);
    }

    private static void RenderPhotoEmphasisProjectBrief(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
    {
        const double leftX = .60;
        const double leftWidth = 5.48;
        const double columnGap = .22;
        const double rightX = leftX + leftWidth + columnGap;
        const double rightWidth = 12.73 - rightX;
        const double top = 1.28;
        const double bottom = 6.70;
        const double sectionGap = .16;
        const double fullWidth = 12.13;

        var costCards = CostCards(canvas, data.CostMode, project);
        var presentStatus = ResolvePresentStatusValue(
            project,
            data.StandardSlideOptions.ShowPresentStage,
            data.StandardSlideOptions.ShowPresentStatus);
        var stripHeight = ResolveProjectBriefInformationStripHeight(presentStatus, costCards.Count);
        var stripY = stripHeight > 0 ? bottom - stripHeight : bottom;
        var contentBottom = stripHeight > 0 ? stripY - sectionGap : bottom;

        AddProjectPhoto(
            canvas,
            project,
            leftX,
            top,
            leftWidth,
            contentBottom - top,
            "Photo-emphasis project photograph");

        AddProjectBriefPanel(
            canvas,
            rightX,
            top,
            rightWidth,
            contentBottom - top,
            project.ProjectBrief);

        AddProjectBriefInformationStrip(
            canvas,
            leftX,
            stripY,
            fullWidth,
            stripHeight,
            presentStatus,
            costCards);
    }

    private static string ResolvePresentStatusValue(
        ProjectBriefingPresentationProject project,
        bool showPresentStage,
        bool showPresentStatus)
    {
        var stage = showPresentStage
            ? NormalizePresentationText(project.PresentStage)
            : string.Empty;
        var status = showPresentStatus
            ? NormalizeExternalStatus(project.ExternalStatus)
            : string.Empty;

        if (string.IsNullOrWhiteSpace(stage)) return status;
        if (string.IsNullOrWhiteSpace(status)) return stage;
        if (string.Equals(stage, status, StringComparison.OrdinalIgnoreCase)) return stage;

        // Keep short values on one line. For a substantive external status, preserve the
        // exact authoritative values but place the stage and remark on separate lines so
        // the bottom strip remains readable without reducing the type excessively.
        var inlineValue = $"{stage} · {status}";
        return inlineValue.Length > 58
            ? $"{stage}\n{status}"
            : inlineValue;
    }

    private static double ResolveProjectBriefInformationStripHeight(
        string presentStatus,
        int costCardCount)
    {
        if (string.IsNullOrWhiteSpace(presentStatus) && costCardCount == 0) return 0d;
        if (presentStatus.Contains('\n')) return 1.02;
        if (presentStatus.Length > 150) return 1.02;
        if (presentStatus.Length > 82) return .94;
        return .86;
    }

    private static void AddProjectBriefInformationStrip(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        double height,
        string presentStatus,
        IReadOnlyList<CostCard> costCards)
    {
        if (height <= 0) return;

        const double gap = .02;
        var hasStatus = !string.IsNullOrWhiteSpace(presentStatus);
        var costWidth = costCards.Count switch
        {
            2 => 2.62,
            1 => 3.05,
            _ => 0d
        };
        var costsTotalWidth = costCards.Count == 0
            ? 0d
            : (costWidth * costCards.Count) + (gap * Math.Max(0, costCards.Count - 1));
        var stripX = hasStatus ? x : x + width - costsTotalWidth;
        var stripWidth = hasStatus ? width : costsTotalWidth;
        canvas.AddRoundedRect(
            stripX,
            y,
            stripWidth,
            height,
            canvas.Theme.Surface,
            canvas.Theme.Border,
            .06,
            "Project brief information strip");

        var statusWidth = hasStatus
            ? width - costsTotalWidth - (costCards.Count > 0 ? gap : 0d)
            : 0d;
        var currentX = stripX;

        if (hasStatus)
        {
            AddProjectBriefStripCell(
                canvas,
                currentX,
                y,
                statusWidth,
                height,
                "PRESENT STATUS",
                presentStatus,
                canvas.Theme.OperationalAccent,
                null,
                "Project brief present status");
            currentX += statusWidth + (costCards.Count > 0 ? gap : 0d);
        }

        for (var index = 0; index < costCards.Count; index++)
        {
            var card = costCards[index];
            AddProjectBriefStripCell(
                canvas,
                currentX,
                y,
                costWidth,
                height,
                card.Title,
                card.Value,
                card.Accent,
                card.Note,
                $"Project brief cost cell {index + 1}");
            currentX += costWidth + gap;
        }
    }

    private static void AddProjectBriefStripCell(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        double height,
        string title,
        string value,
        string accent,
        string? note,
        string name)
    {
        if (width <= 0) return;

        canvas.AddRect(
            x,
            y + .08,
            .045,
            Math.Max(.20, height - .16),
            accent,
            name: $"{name} accent");

        var titleWidth = Math.Max(.60, width - .30);
        canvas.AddText(
            x + .17,
            y + .12,
            titleWidth,
            .18,
            title,
            width < 2.8 ? 7.2 : 7.7,
            accent,
            true,
            "l",
            "t",
            $"{name} title");

        var valueWidth = Math.Max(.60, width - .30);
        var valueLines = value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n');
        var longestValueLine = valueLines.Length == 0
            ? value.Length
            : valueLines.Max(line => line.Length);
        var valueFont = title == "PRESENT STATUS"
            ? valueLines.Length > 1
                ? longestValueLine switch
                {
                    <= 72 => 10.4,
                    <= 118 => 9.6,
                    _ => 8.9
                }
                : value.Length switch
                {
                    <= 56 => 10.6,
                    <= 105 => 9.7,
                    _ => 8.8
                }
            : width < 2.8
                ? 11.2
                : 12.0;
        var valueHeight = string.IsNullOrWhiteSpace(note)
            ? height - .38
            : Math.Max(.24, height - .53);

        canvas.AddText(
            x + .17,
            y + .34,
            valueWidth,
            valueHeight,
            TruncateAtWord(value, title == "PRESENT STATUS" ? 260 : 72),
            valueFont,
            canvas.Theme.TextPrimary,
            true,
            "l",
            "t",
            $"{name} value");

        if (!string.IsNullOrWhiteSpace(note))
        {
            canvas.AddText(
                x + .17,
                y + height - .21,
                valueWidth,
                .13,
                note!,
                6.8,
                canvas.Theme.TextMuted,
                false,
                "l",
                "t",
                $"{name} note");
        }
    }

    private static void AddProjectPhoto(
        SlideCanvas canvas,
        ProjectBriefingPresentationProject project,
        double x,
        double y,
        double width,
        double height,
        string frameName)
    {
        if (project.CoverPhoto is { Length: > 0 })
        {
            canvas.AddRoundedRect(
                x,
                y,
                width,
                height,
                canvas.Theme.Surface,
                canvas.Theme.Border,
                .08,
                frameName);
            var availableWidth = width - .20;
            var availableHeight = height - .20;
            var imageWidth = availableWidth;
            var imageHeight = imageWidth * 9d / 16d;
            if (imageHeight > availableHeight)
            {
                imageHeight = availableHeight;
                imageWidth = imageHeight * 16d / 9d;
            }
            canvas.AddImage(
                project.CoverPhoto,
                project.CoverPhotoContentType,
                x + .10 + ((availableWidth - imageWidth) / 2d),
                y + .10 + ((availableHeight - imageHeight) / 2d),
                imageWidth,
                imageHeight,
                $"{project.ProjectName} cover photograph");
            return;
        }

        canvas.AddTextShape(
            x,
            y,
            width,
            height,
            canvas.Theme.Placeholder,
            canvas.Theme.Border,
            .75,
            "roundRect",
            new[]
            {
                new RichTextParagraph(
                    new[]
                    {
                        new RichTextRun(
                            "PHOTOGRAPH NOT AVAILABLE",
                            8.7,
                            canvas.Theme.TextMuted,
                            Bold: true)
                    },
                    Align: "ctr")
            },
            "Project photograph placeholder",
            verticalAnchor: "ctr",
            allowAutoFit: false,
            leftInset: .12,
            rightInset: .12,
            topInset: .06,
            bottomInset: .06);
    }

    private static void AddProjectBriefPanel(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        double height,
        string? projectBrief)
    {
        canvas.AddRoundedRect(
            x,
            y,
            width,
            height,
            canvas.Theme.Surface,
            canvas.Theme.Border,
            .08,
            "Project brief panel");
        canvas.AddRect(
            x,
            y,
            .08,
            height,
            canvas.Theme.NarrativeAccent,
            name: "Project brief accent");

        canvas.AddRichTextBox(
            x + .28,
            y + .20,
            width - .56,
            height - .40,
            BuildProjectBriefParagraphs(canvas, projectBrief),
            "Project brief",
            verticalAnchor: "t",
            allowAutoFit: true,
            leftInset: .05,
            rightInset: .05,
            topInset: .02,
            bottomInset: .02);
    }

    private static void AddCostCards(
        SlideCanvas canvas,
        IReadOnlyList<CostCard> costCards,
        double x,
        double y,
        double width,
        double height,
        double gap)
    {
        if (costCards.Count == 0 || height <= 0) return;

        if (costCards.Count == 1)
        {
            AddInfoCard(canvas, x, y, width, height,
                costCards[0].Title, costCards[0].Value, costCards[0].Accent, costCards[0].Fill, costCards[0].Note);
            return;
        }

        var cardWidth = (width - gap) / 2d;
        AddInfoCard(canvas, x, y, cardWidth, height,
            costCards[0].Title, costCards[0].Value, costCards[0].Accent, costCards[0].Fill, costCards[0].Note);
        AddInfoCard(canvas, x + cardWidth + gap, y, cardWidth, height,
            costCards[1].Title, costCards[1].Value, costCards[1].Accent, costCards[1].Fill, costCards[1].Note);
    }

    private static IReadOnlyList<RichTextParagraph> BuildProjectBriefParagraphs(
        SlideCanvas canvas,
        string? projectBrief)
    {
        var paragraphs = new List<RichTextParagraph>
        {
            new(
                new[]
                {
                    new RichTextRun(
                        "PROJECT BRIEF",
                        10.5,
                        canvas.Theme.NarrativeAccent,
                        Bold: true)
                },
                SpaceAfterPoints: 12.0,
                LineSpacingPoints: 12.6)
        };

        var isMissing = string.IsNullOrWhiteSpace(projectBrief)
            || string.Equals(projectBrief.Trim(), "Project brief not recorded.", StringComparison.OrdinalIgnoreCase);
        if (isMissing)
        {
            paragraphs.Add(new RichTextParagraph(
                new[]
                {
                    new RichTextRun(
                        "Project brief not recorded.",
                        13.0,
                        canvas.Theme.TextMuted,
                        Italic: true)
                },
                LineSpacingPoints: 16.0));
            return paragraphs;
        }

        var typography = ProjectBriefingNarrativeTypography.ResolveProjectBrief(projectBrief);
        foreach (var paragraph in projectBrief!
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace("\r", "\n", StringComparison.Ordinal)
                     .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            paragraphs.Add(new RichTextParagraph(
                new[]
                {
                    new RichTextRun(
                        paragraph.Replace("\n", " ", StringComparison.Ordinal),
                        typography.BodyFontSize,
                        canvas.Theme.TextPrimary)
                },
                SpaceAfterPoints: typography.SpaceAfterPoints,
                LineSpacingPoints: typography.LineSpacingPoints));
        }

        return paragraphs;
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
        canvas.AddRoundedRect(
            x,
            y,
            width,
            height,
            canvas.Theme.Surface,
            canvas.Theme.Border,
            .08,
            "Capability panel");
        canvas.AddRect(
            x,
            y,
            .08,
            height,
            canvas.Theme.NarrativeAccent,
            name: "Capability accent");

        canvas.AddRichTextBox(
            x + .25,
            y + .17,
            width - .50,
            height - .34,
            BuildCapabilityParagraphs(canvas, heading, page.Blocks),
            "Capability overview",
            verticalAnchor: "t",
            allowAutoFit: false,
            leftInset: .05,
            rightInset: .05,
            topInset: .02,
            bottomInset: .02);
    }

    private static IReadOnlyList<RichTextParagraph> BuildCapabilityParagraphs(
        SlideCanvas canvas,
        string heading,
        IReadOnlyList<ProjectBriefingCapabilityLayoutBlock> blocks)
    {
        var paragraphs = new List<RichTextParagraph>(blocks.Count + 1)
        {
            new(
                new[]
                {
                    new RichTextRun(
                        heading,
                        10.5,
                        canvas.Theme.NarrativeAccent,
                        Bold: true)
                },
                SpaceAfterPoints: 10.0,
                LineSpacingPoints: 12.6)
        };

        foreach (var block in blocks)
        {
            var textColor = block.IsMuted
                ? canvas.Theme.TextMuted
                : canvas.Theme.TextPrimary;
            var spaceAfter = Math.Max(0, block.SpaceAfter * 72d);
            var lineSpacing = block.Type == ProjectBriefingCapabilityBlockType.Heading
                ? 16.2
                : block.FontSize * 1.20;

            if (block.Type == ProjectBriefingCapabilityBlockType.Heading)
            {
                paragraphs.Add(new RichTextParagraph(
                    new[]
                    {
                        new RichTextRun(
                            block.Text,
                            block.FontSize,
                            canvas.Theme.NarrativeAccent,
                            Bold: true)
                    },
                    SpaceAfterPoints: spaceAfter,
                    LineSpacingPoints: lineSpacing));
                continue;
            }

            if (block.Type is ProjectBriefingCapabilityBlockType.Bullet
                or ProjectBriefingCapabilityBlockType.NumberedItem
                or ProjectBriefingCapabilityBlockType.LetteredItem)
            {
                var bodyIndent = .36 + (Math.Max(0, block.IndentLevel) * .19);

                if (block.IsContinuation || string.IsNullOrWhiteSpace(block.Marker))
                {
                    paragraphs.Add(new RichTextParagraph(
                        new[]
                        {
                            new RichTextRun(
                                block.Text,
                                block.FontSize,
                                textColor)
                        },
                        LeftMarginInches: bodyIndent,
                        SpaceAfterPoints: spaceAfter,
                        LineSpacingPoints: lineSpacing));
                }
                else
                {
                    paragraphs.Add(new RichTextParagraph(
                        new[]
                        {
                            new RichTextRun(
                                block.Marker!,
                                block.FontSize,
                                textColor,
                                Bold: block.Type != ProjectBriefingCapabilityBlockType.Bullet),
                            new RichTextRun(
                                block.Text,
                                block.FontSize,
                                textColor)
                        },
                        LeftMarginInches: bodyIndent,
                        FirstLineIndentInches: -bodyIndent,
                        TabStopInches: bodyIndent,
                        TabAfterFirstRun: true,
                        SpaceAfterPoints: spaceAfter,
                        LineSpacingPoints: lineSpacing));
                }

                continue;
            }

            paragraphs.Add(new RichTextParagraph(
                new[]
                {
                    new RichTextRun(
                        block.Text,
                        block.FontSize,
                        textColor)
                },
                SpaceAfterPoints: spaceAfter,
                LineSpacingPoints: lineSpacing));
        }

        return paragraphs;
    }

    private static DetailedSlideLayout CalculateDetailedLayout(
        ProjectBriefingPresentationProject project,
        bool hasPhoto,
        double contentTop,
        double positionBottom,
        double sectionGap,
        bool showPresentStage,
        bool showPresentStatus,
        int statusCharactersPerLine = 48,
        double minimumPhotoHeight = 1.48,
        double minimumPlaceholderHeight = 1.20,
        double maximumContextHeight = 2.78)
    {
        var contextHeight = CalculateProjectContextHeight(
            project,
            showPresentStage,
            showPresentStatus,
            statusCharactersPerLine,
            maximumContextHeight);
        var available = Math.Max(1.6, positionBottom - contentTop);
        var effectiveGap = contextHeight > 0 ? sectionGap : 0d;
        var minimumPhoto = hasPhoto ? minimumPhotoHeight : minimumPlaceholderHeight;
        var photoHeight = available - contextHeight - effectiveGap;

        if (photoHeight < minimumPhoto && contextHeight > 0)
        {
            const double minimumContext = .92;
            var transferable = Math.Max(0d, contextHeight - minimumContext);
            var transfer = Math.Min(minimumPhoto - photoHeight, transferable);
            contextHeight -= transfer;
            photoHeight += transfer;
        }

        photoHeight = Math.Max(Math.Min(minimumPhoto, available), photoHeight);
        return new DetailedSlideLayout(photoHeight, contextHeight);
    }

    private static double CalculateProjectContextHeight(
        ProjectBriefingPresentationProject project,
        bool showPresentStage,
        bool showPresentStatus,
        int statusCharactersPerLine,
        double maximumHeight)
    {
        var value = ResolvePresentStatusValue(project, showPresentStage, showPresentStatus);
        if (string.IsNullOrWhiteSpace(value)) return 0d;

        var estimatedLines = Math.Clamp(
            EstimateWrappedLines(value, statusCharactersPerLine),
            1,
            8);
        return Math.Clamp(
            .62 + (estimatedLines * .21),
            .92,
            maximumHeight);
    }

    private static void AddProjectContextCard(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        double height,
        ProjectBriefingPresentationProject project,
        bool showPresentStage,
        bool showPresentStatus,
        bool compact = false)
    {
        var value = ResolvePresentStatusValue(project, showPresentStage, showPresentStatus);
        if (string.IsNullOrWhiteSpace(value) || height <= 0) return;

        canvas.AddRoundedRect(
            x,
            y,
            width,
            height,
            canvas.Theme.Surface,
            canvas.Theme.Border,
            .08,
            "Present status panel");

        var estimatedLines = EstimateWrappedLines(value, compact ? 58 : 48);
        var valueFont = estimatedLines switch
        {
            <= 1 => compact ? 10.0 : 10.5,
            <= 3 => compact ? 9.3 : 9.8,
            _ => compact ? 8.6 : 9.0
        };

        canvas.AddRichTextBox(
            x + .21,
            y + .14,
            width - .42,
            Math.Max(.40, height - .28),
            new[]
            {
                new RichTextParagraph(
                    new[]
                    {
                        new RichTextRun(
                            "PRESENT STATUS",
                            compact ? 8.4 : 9.0,
                            canvas.Theme.OperationalAccent,
                            Bold: true)
                    },
                    SpaceAfterPoints: compact ? 5.0 : 6.0,
                    LineSpacingPoints: compact ? 9.8 : 10.5),
                new RichTextParagraph(
                    new[]
                    {
                        new RichTextRun(
                            TruncateAtWord(value, compact ? 320 : 260),
                            valueFont,
                            canvas.Theme.TextPrimary,
                            Bold: true)
                    },
                    LineSpacingPoints: valueFont * 1.18)
            },
            "Present status labels",
            verticalAnchor: "t",
            allowAutoFit: true,
            leftInset: .02,
            rightInset: .02,
            topInset: 0,
            bottomInset: 0);
    }

    private static string NormalizeExternalStatus(string? value)
    {
        var normalized = NormalizePresentationText(value);
        return string.Equals(
            normalized,
            "No external status recorded",
            StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : normalized;
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
        var titleFont = width < 2.3 ? 8.0 : 9.2;
        var valueFont = width < 2.3 ? 12.0 : 13.5;
        var paragraphs = new List<RichTextParagraph>
        {
            new(
                new[]
                {
                    new RichTextRun(
                        title,
                        titleFont,
                        accent,
                        Bold: true)
                },
                SpaceAfterPoints: 6.0,
                LineSpacingPoints: titleFont * 1.12),
            new(
                new[]
                {
                    new RichTextRun(
                        Truncate(value, 70),
                        valueFont,
                        canvas.Theme.TextPrimary,
                        Bold: true)
                },
                SpaceAfterPoints: string.IsNullOrWhiteSpace(note) ? 0 : 6.0,
                LineSpacingPoints: valueFont * 1.10)
        };

        if (!string.IsNullOrWhiteSpace(note))
        {
            paragraphs.Add(new RichTextParagraph(
                new[]
                {
                    new RichTextRun(
                        note!,
                        7.8,
                        canvas.Theme.TextMuted)
                },
                LineSpacingPoints: 9.0));
        }

        canvas.AddTextShape(
            x,
            y,
            width,
            height,
            fill,
            canvas.Theme.Border,
            .75,
            "roundRect",
            paragraphs,
            $"{title} card",
            verticalAnchor: "t",
            allowAutoFit: true,
            leftInset: .20,
            rightInset: .16,
            topInset: .13,
            bottomInset: .08);
        canvas.AddRect(x, y, .06, height, accent, name: $"{title} accent");
    }

    private static void AddSlideTitle(SlideCanvas canvas, string title, string? subtitle = null)
        => AddProjectSlideHeader(
            canvas,
            title,
            subtitle,
            ProjectSlideHeaderVariant.Standard);

    private static void AddProjectSlideHeader(
        SlideCanvas canvas,
        string title,
        string? subtitle,
        ProjectSlideHeaderVariant variant)
    {
        var style = ResolveProjectSlideHeaderStyle(canvas, title, variant);

        canvas.AddRect(0, 0, SlideWidth, SlideHeight, canvas.Theme.Canvas, name: style.CanvasShapeName);
        canvas.AddRect(
            0,
            0,
            SlideWidth,
            style.TopRuleHeight,
            style.TopRuleColor,
            name: style.TopRuleShapeName);
        canvas.AddBrandingImages(style.BrandingVariant);

        if (style.UseRichTextTitle)
        {
            canvas.AddRichTextBox(
                style.TitleX,
                style.TitleY,
                style.TitleWidth,
                style.TitleHeight,
                new[]
                {
                    new RichTextParagraph(
                        new[]
                        {
                            new RichTextRun(
                                title,
                                style.TitleFontSize,
                                style.TitleColor,
                                Bold: true)
                        },
                        Align: style.TitleAlign,
                        LineSpacingPoints: style.TitleFontSize * 1.05)
                },
                style.TitleShapeName,
                verticalAnchor: "ctr",
                allowAutoFit: true,
                leftInset: .03,
                rightInset: .03,
                topInset: 0,
                bottomInset: 0);
        }
        else
        {
            canvas.AddText(
                style.TitleX,
                style.TitleY,
                style.TitleWidth,
                style.TitleHeight,
                title,
                style.TitleFontSize,
                style.TitleColor,
                true,
                style.TitleAlign,
                name: style.TitleShapeName);
        }

        if (!string.IsNullOrWhiteSpace(subtitle) && style.SubtitleHeight > 0)
        {
            canvas.AddText(
                style.TitleX,
                style.SubtitleY,
                style.TitleWidth,
                style.SubtitleHeight,
                subtitle,
                style.SubtitleFontSize,
                style.SubtitleColor,
                false,
                style.TitleAlign,
                name: style.SubtitleShapeName);
        }

        canvas.AddLine(
            style.DividerX1,
            style.DividerY,
            style.DividerX2,
            style.DividerY,
            canvas.Theme.Divider,
            style.DividerWidth);
    }

    private static ProjectSlideHeaderStyle ResolveProjectSlideHeaderStyle(
        SlideCanvas canvas,
        string title,
        ProjectSlideHeaderVariant variant)
    {
        if (variant == ProjectSlideHeaderVariant.ProjectUpdateSheet)
        {
            return new ProjectSlideHeaderStyle(
                HeaderVariant.ProjectUpdateSheet,
                canvas.Theme.ProjectUpdateAccent,
                canvas.Theme.TextPrimary,
                canvas.Theme.TextMuted,
                TopRuleHeight: .065,
                TitleX: canvas.ShowBranding ? 1.16 : .62,
                TitleY: .15,
                TitleWidth: canvas.ShowBranding ? 11.01 : 12.09,
                TitleHeight: .64,
                TitleFontSize: UpdateSheetTitleFontSize(title),
                TitleAlign: "ctr",
                UseRichTextTitle: true,
                SubtitleY: 0,
                SubtitleHeight: 0,
                SubtitleFontSize: 0,
                DividerX1: .55,
                DividerX2: 12.78,
                DividerY: .92,
                DividerWidth: .65,
                CanvasShapeName: "Project sheet canvas",
                TopRuleShapeName: "Project sheet top accent",
                TitleShapeName: "Project sheet title",
                SubtitleShapeName: "Project sheet subtitle");
        }

        var titleAlign = canvas.ShowBranding ? "ctr" : "l";
        return new ProjectSlideHeaderStyle(
            variant == ProjectSlideHeaderVariant.FfcGlobalFootprint
                ? HeaderVariant.FfcGlobalFootprint
                : HeaderVariant.Standard,
            canvas.Theme.HeaderAccent,
            canvas.Theme.TextPrimary,
            canvas.Theme.TextMuted,
            TopRuleHeight: .10,
            TitleX: canvas.ShowBranding ? 1.30 : .62,
            TitleY: .27,
            TitleWidth: canvas.ShowBranding ? 10.73 : 11.40,
            TitleHeight: .44,
            TitleFontSize: SlideTitleFontSize(title),
            TitleAlign: titleAlign,
            UseRichTextTitle: false,
            SubtitleY: .72,
            SubtitleHeight: .22,
            SubtitleFontSize: 10.0,
            DividerX1: .62,
            DividerX2: 12.72,
            DividerY: 1.00,
            DividerWidth: .55,
            CanvasShapeName: "Slide canvas",
            TopRuleShapeName: "Slide top accent",
            TitleShapeName: "Slide title",
            SubtitleShapeName: "Slide subtitle");
    }

    private static void AddFooter(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data,
        int slideNumber,
        int slideCount,
        SlidePlanKind kind)
    {
        if (kind is SlidePlanKind.Cover or SlidePlanKind.Closing)
        {
            return;
        }

        canvas.AddLine(.62, 7.05, 12.72, 7.05, canvas.Theme.Divider, .55);
        canvas.AddText(.65, 7.12, 5.55, .18, "SIMULATOR DEVELOPMENT DIVISION", 7.5, canvas.Theme.TextMuted, true, "l");
        if (kind == SlidePlanKind.InstitutionalProfile && data.InstitutionalProfile is not null)
        {
            var dataAsOn = data.InstitutionalProfile.DataAsOnUtc
                .ToUniversalTime()
                .ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            canvas.AddText(
                4.15,
                7.12,
                5.05,
                .18,
                $"Data as on {dataAsOn} · Source: PRISM ERP",
                7.8,
                canvas.Theme.TextMuted,
                false,
                "ctr");
        }
        else if (kind == SlidePlanKind.FfcGlobalFootprint && data.FfcGlobalFootprint is not null)
        {
            var dataAsOn = TimeZoneInfo
                .ConvertTime(data.GeneratedAtUtc, TimeZoneHelper.GetIst())
                .ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            canvas.AddText(
                4.15,
                7.12,
                5.05,
                .18,
                $"Data as on {dataAsOn} · Source: PRISM ERP",
                7.8,
                canvas.Theme.TextMuted,
                false,
                "ctr");
        }
        else if (!string.IsNullOrWhiteSpace(data.HandlingMarking))
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
            ProjectBriefingBrandingScope.CoverAndSummary => kind is SlidePlanKind.Cover or SlidePlanKind.Summary or SlidePlanKind.InstitutionalProfile or SlidePlanKind.RoleCharter or SlidePlanKind.FfcGlobalFootprint or SlidePlanKind.Closing,
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

    private static SlideIdList EnsureSlideIdList(DocumentFormat.OpenXml.Presentation.Presentation presentation)
    {
        var existing = presentation.SlideIdList;
        if (existing is not null)
        {
            return existing;
        }

        var slideIdList = new SlideIdList();

        // Let the Open XML SDK place p:sldIdLst in the schema-defined position.
        // Manual null-coalescing across different child-element types is both
        // type-unsafe and vulnerable to invalid PresentationML element ordering.
        presentation.AddChild(slideIdList, throwOnError: true);

        return slideIdList;
    }

    private static string SanitizeOpenXmlText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            var scalar = rune.Value;
            if (scalar is 0x9 or 0xA or 0xD
                || scalar is >= 0x20 and <= 0xD7FF
                || scalar is >= 0xE000 and <= 0xFFFD
                || scalar is >= 0x10000 and <= 0x10FFFF)
            {
                builder.Append(rune.ToString());
            }
            else
            {
                builder.Append(' ');
            }
        }

        return builder.ToString();
    }

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

        // A generated deck must not retain custom-show references to template slides
        // that no longer exist. Stale references can make PowerPoint repair the file.
        presentationPart.Presentation.CustomShowList?.Remove();
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
        InstitutionalProfile,
        RoleCharter,
        FfcGlobalFootprint,
        Project,
        Closing
    }

    private enum HeaderVariant
    {
        Cover,
        Closing,
        Standard,
        FfcGlobalFootprint,
        ProjectUpdateSheet
    }

    private enum ProjectSlideHeaderVariant
    {
        Standard,
        FfcGlobalFootprint,
        ProjectUpdateSheet
    }

    private sealed record ProjectSlideHeaderStyle(
        HeaderVariant BrandingVariant,
        string TopRuleColor,
        string TitleColor,
        string SubtitleColor,
        double TopRuleHeight,
        double TitleX,
        double TitleY,
        double TitleWidth,
        double TitleHeight,
        double TitleFontSize,
        string TitleAlign,
        bool UseRichTextTitle,
        double SubtitleY,
        double SubtitleHeight,
        double SubtitleFontSize,
        double DividerX1,
        double DividerX2,
        double DividerY,
        double DividerWidth,
        string CanvasShapeName,
        string TopRuleShapeName,
        string TitleShapeName,
        string SubtitleShapeName);

    private sealed record DetailedSlideLayout(double PhotoHeight, double ContextHeight);
    private sealed record CostCard(string Title, string Value, string Accent, string Fill, string? Note);
    private sealed record RichTextRun(
        string Text,
        double FontSize,
        string Color,
        bool Bold = false,
        bool Italic = false);
    private sealed record RichTextParagraph(
        IReadOnlyList<RichTextRun> Runs,
        string Align = "l",
        double LeftMarginInches = 0,
        double FirstLineIndentInches = 0,
        double? TabStopInches = null,
        bool TabAfterFirstRun = false,
        double SpaceAfterPoints = 0,
        double? LineSpacingPoints = null);
    private sealed record NativeTableBorders(
        string? LeftColor = null,
        double LeftWidth = .25,
        string? RightColor = null,
        double RightWidth = .25,
        string? TopColor = null,
        double TopWidth = .25,
        string? BottomColor = null,
        double BottomWidth = .25)
    {
        public static NativeTableBorders None { get; } = new(
            LeftWidth: 0,
            RightWidth: 0,
            TopWidth: 0,
            BottomWidth: 0);
    }

    private sealed record NativeTableCell(
        string Value,
        double FontSize,
        string Color,
        bool Bold,
        string Align,
        string Fill,
        NativeTableBorders? Borders = null,
        string VerticalAnchor = "ctr",
        double LeftMargin = .05,
        double RightMargin = .05,
        double TopMargin = .025,
        double BottomMargin = .025,
        int GridSpan = 1,
        bool HorizontalMerge = false);
    private sealed record ProjectBriefingBrandingAssets(byte[]? LeftLogo, byte[]? RightLogo);
    private sealed record SlidePlan(SlidePlanKind Kind, Action<SlideCanvas> Render);

    private sealed class SlideCanvas
    {
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

        public void AddRect(
            double x,
            double y,
            double width,
            double height,
            string fill,
            string? line = null,
            double lineWidth = .75,
            string? name = null)
            => AddShape(
                x,
                y,
                width,
                height,
                fill,
                line,
                lineWidth,
                "rect",
                null,
                0,
                Theme.TextPrimary,
                false,
                "l",
                "ctr",
                name ?? "Rectangle",
                isTextBox: false);

        public void AddRoundedRect(
            double x,
            double y,
            double width,
            double height,
            string fill,
            string? line,
            double radius,
            string? name = null)
            => AddShape(
                x,
                y,
                width,
                height,
                fill,
                line,
                .75,
                "roundRect",
                null,
                0,
                Theme.TextPrimary,
                false,
                "l",
                "ctr",
                name ?? "Rounded rectangle",
                isTextBox: false);

        public void AddEllipse(
            double x,
            double y,
            double width,
            double height,
            string fill,
            string? line = null,
            double lineWidth = .75,
            string? name = null)
            => AddShape(
                x,
                y,
                width,
                height,
                fill,
                line,
                lineWidth,
                "ellipse",
                null,
                0,
                Theme.TextPrimary,
                false,
                "c",
                "ctr",
                name ?? "Ellipse",
                isTextBox: false);

        public void AddSubtleRoundedRect(
            double x,
            double y,
            double width,
            double height,
            string fill,
            string? line,
            string? name = null)
            => AddShapeXml(
                x,
                y,
                width,
                height,
                fill,
                line,
                .75,
                "roundRect",
                string.Empty,
                name ?? "Subtly rounded rectangle",
                isTextBox: false,
                geometryAdjustmentsXml: "<a:gd name=\"adj\" fmla=\"val 6000\"/>");

        public void AddBrandingImages(HeaderVariant variant)
        {
            if (!ShowBranding) return;

            if (variant == HeaderVariant.Closing)
            {
                if (_branding.LeftLogo is { Length: > 0 })
                {
                    if (Theme.IsDark)
                    {
                        AddRoundedRect(.29, .15, .64, .64, Theme.BrandingPlate, null, .045, "Left branding plate");
                        AddImageContained(_branding.LeftLogo, .36, .22, .50, .50, "Left formation insignia");
                    }
                    else
                    {
                        AddImageContained(_branding.LeftLogo, .32, .18, .56, .56, "Left formation insignia");
                    }
                }

                if (_branding.RightLogo is { Length: > 0 })
                {
                    if (Theme.IsDark)
                    {
                        AddRoundedRect(12.39, .15, .58, .66, Theme.BrandingPlate, null, .045, "Right branding plate");
                        AddImageContained(_branding.RightLogo, 12.46, .20, .44, .56, "Right division insignia");
                    }
                    else
                    {
                        AddImageContained(_branding.RightLogo, 12.43, .16, .44, .58, "Right division insignia");
                    }
                }

                return;
            }

            if (variant == HeaderVariant.Cover)
            {
                if (_branding.LeftLogo is { Length: > 0 })
                {
                    if (Theme.IsDark)
                    {
                        AddRoundedRect(.28, .17, .68, .68, Theme.BrandingPlate, Theme.BrandingPlateBorder, .05, "Left branding plate");
                        AddImageContained(_branding.LeftLogo, .36, .25, .52, .52, "Left formation insignia");
                    }
                    else
                    {
                        AddRoundedRect(.24, .13, .78, .78, Theme.BrandingPlate, Theme.BrandingPlateBorder, .06, "Left branding plate");
                        AddImageContained(_branding.LeftLogo, .32, .20, .62, .62, "Left formation insignia");
                    }
                }
                if (_branding.RightLogo is { Length: > 0 })
                {
                    if (Theme.IsDark)
                    {
                        AddRoundedRect(12.33, .16, .60, .70, Theme.BrandingPlate, Theme.BrandingPlateBorder, .05, "Right branding plate");
                        AddImageContained(_branding.RightLogo, 12.40, .21, .46, .60, "Right division insignia");
                    }
                    else
                    {
                        AddRoundedRect(12.28, .12, .70, .78, Theme.BrandingPlate, Theme.BrandingPlateBorder, .06, "Right branding plate");
                        AddImageContained(_branding.RightLogo, 12.35, .17, .56, .68, "Right division insignia");
                    }
                }
                return;
            }

            if (variant == HeaderVariant.ProjectUpdateSheet)
            {
                if (_branding.LeftLogo is { Length: > 0 })
                {
                    AddImageContained(_branding.LeftLogo, .32, .20, .48, .48, "Left formation insignia");
                }

                if (_branding.RightLogo is { Length: > 0 })
                {
                    // The SDD insignia is visually slender. A larger optical box balances it
                    // against the denser formation crest without changing the title safe area.
                    AddImageContained(_branding.RightLogo, 12.39, .10, .46, .68, "Right division insignia");
                }

                return;
            }

            if (variant == HeaderVariant.FfcGlobalFootprint)
            {
                if (_branding.LeftLogo is { Length: > 0 })
                {
                    if (Theme.IsDark)
                    {
                        AddRoundedRect(.27, .16, .58, .58, Theme.BrandingPlate, Theme.BrandingPlateBorder, .045, "Left branding plate");
                        AddImageContained(_branding.LeftLogo, .34, .23, .44, .44, "Left formation insignia");
                    }
                    else
                    {
                        AddImageContained(_branding.LeftLogo, .32, .20, .48, .48, "Left formation insignia");
                    }
                }

                if (_branding.RightLogo is { Length: > 0 })
                {
                    // The division insignia is optically slender. The FFC footprint slide uses
                    // a slightly larger box so both institutional marks carry equal visual weight.
                    if (Theme.IsDark)
                    {
                        AddRoundedRect(12.40, .12, .53, .64, Theme.BrandingPlate, Theme.BrandingPlateBorder, .045, "Right branding plate");
                        AddImageContained(_branding.RightLogo, 12.46, .16, .41, .57, "Right division insignia");
                    }
                    else
                    {
                        AddImageContained(_branding.RightLogo, 12.43, .13, .42, .62, "Right division insignia");
                    }
                }

                return;
            }

            if (_branding.LeftLogo is { Length: > 0 })
            {
                if (Theme.IsDark)
                {
                    AddRoundedRect(.27, .16, .58, .58, Theme.BrandingPlate, Theme.BrandingPlateBorder, .045, "Left branding plate");
                    AddImageContained(_branding.LeftLogo, .34, .23, .44, .44, "Left formation insignia");
                }
                else
                {
                    AddImageContained(_branding.LeftLogo, .32, .20, .48, .48, "Left formation insignia");
                }
            }

            if (_branding.RightLogo is { Length: > 0 })
            {
                if (Theme.IsDark)
                {
                    AddRoundedRect(12.43, .14, .48, .60, Theme.BrandingPlate, Theme.BrandingPlateBorder, .045, "Right branding plate");
                    AddImageContained(_branding.RightLogo, 12.50, .18, .34, .52, "Right division insignia");
                }
                else
                {
                    AddImageContained(_branding.RightLogo, 12.49, .16, .36, .56, "Right division insignia");
                }
            }
        }

        public void AddGroup(
            double x,
            double y,
            double width,
            double height,
            string name,
            Action renderChildren)
        {
            ArgumentNullException.ThrowIfNull(renderChildren);
            EnsureFrame(x, y, width, height, name);

            var startIndex = _elements.Count;
            renderChildren();
            if (_elements.Count == startIndex) return;

            var childElements = _elements.Skip(startIndex).ToArray();
            _elements.RemoveRange(startIndex, _elements.Count - startIndex);

            var id = _nextShapeId++;
            _elements.Add($"""
<p:grpSp>
  <p:nvGrpSpPr><p:cNvPr id="{id}" name="{Escape(name)}"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>
  <p:grpSpPr><a:xfrm><a:off x="{Emu(x)}" y="{Emu(y)}"/><a:ext cx="{Emu(width)}" cy="{Emu(height)}"/><a:chOff x="{Emu(x)}" y="{Emu(y)}"/><a:chExt cx="{Emu(width)}" cy="{Emu(height)}"/></a:xfrm></p:grpSpPr>
  {string.Join(Environment.NewLine, childElements)}
</p:grpSp>
""");
        }

        public void AddLine(double x1, double y1, double x2, double y2, string color, double width)
        {
            EnsureFinite(x1, nameof(x1));
            EnsureFinite(y1, nameof(y1));
            EnsureFinite(x2, nameof(x2));
            EnsureFinite(y2, nameof(y2));
            if (width <= 0 || !double.IsFinite(width))
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Line width must be a finite positive value.");
            }

            var extentX = Math.Abs(x2 - x1);
            var extentY = Math.Abs(y2 - y1);
            if (extentX == 0 && extentY == 0)
            {
                return;
            }

            var originX = Math.Min(x1, x2);
            var originY = Math.Min(y1, y2);
            var flipAttributes = string.Concat(
                x2 < x1 ? " flipH=\"1\"" : string.Empty,
                y2 < y1 ? " flipV=\"1\"" : string.Empty);
            var id = _nextShapeId++;
            _elements.Add($"""
<p:cxnSp>
  <p:nvCxnSpPr><p:cNvPr id="{id}" name="Line {id}"/><p:cNvCxnSpPr/><p:nvPr/></p:nvCxnSpPr>
  <p:spPr><a:xfrm{flipAttributes}><a:off x="{Emu(originX)}" y="{Emu(originY)}"/><a:ext cx="{Emu(extentX)}" cy="{Emu(extentY)}"/></a:xfrm><a:prstGeom prst="line"><a:avLst/></a:prstGeom><a:ln w="{LineWidth(width)}"><a:solidFill><a:srgbClr val="{CleanColor(color)}"/></a:solidFill></a:ln></p:spPr>
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
            string verticalAnchor = "ctr",
            string? name = null)
            => AddShape(
                x,
                y,
                width,
                height,
                null,
                null,
                0,
                "rect",
                text,
                fontSize,
                color,
                bold,
                align,
                verticalAnchor,
                name ?? "Text",
                isTextBox: true);

        public void AddRichTextBox(
            double x,
            double y,
            double width,
            double height,
            IReadOnlyList<RichTextParagraph> paragraphs,
            string name,
            string verticalAnchor = "t",
            bool allowAutoFit = false,
            double leftInset = .05,
            double rightInset = .05,
            double topInset = .03,
            double bottomInset = .03)
            => AddRichTextShape(
                x,
                y,
                width,
                height,
                null,
                null,
                0,
                "rect",
                paragraphs,
                name,
                isTextBox: true,
                verticalAnchor: verticalAnchor,
                allowAutoFit: allowAutoFit,
                leftInset: leftInset,
                rightInset: rightInset,
                topInset: topInset,
                bottomInset: bottomInset);

        public void AddTextShape(
            double x,
            double y,
            double width,
            double height,
            string fill,
            string? line,
            double lineWidth,
            string geometry,
            IReadOnlyList<RichTextParagraph> paragraphs,
            string name,
            string verticalAnchor = "t",
            bool allowAutoFit = false,
            double leftInset = .05,
            double rightInset = .05,
            double topInset = .03,
            double bottomInset = .03)
            => AddRichTextShape(
                x,
                y,
                width,
                height,
                fill,
                line,
                lineWidth,
                geometry,
                paragraphs,
                name,
                isTextBox: false,
                verticalAnchor: verticalAnchor,
                allowAutoFit: allowAutoFit,
                leftInset: leftInset,
                rightInset: rightInset,
                topInset: topInset,
                bottomInset: bottomInset);

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
            if (widths.Any(width => width <= 0 || !double.IsFinite(width)))
            {
                throw new ArgumentException("Every native PowerPoint table column width must be finite and positive.", nameof(widths));
            }
            if (heights.Any(height => height <= 0 || !double.IsFinite(height)))
            {
                throw new ArgumentException("Every native PowerPoint table row height must be finite and positive.", nameof(heights));
            }
            EnsureFinite(x, nameof(x));
            EnsureFinite(y, nameof(y));

            var id = _nextShapeId++;
            var columnXml = string.Join(string.Empty, widths.Select(width => $"<a:gridCol w=\"{Emu(width)}\"/>"));
            var rowXml = new StringBuilder();

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                rowXml.Append($"<a:tr h=\"{Emu(heights[rowIndex])}\">");
                foreach (var cell in rows[rowIndex])
                {
                    var mergeAttributes = cell.HorizontalMerge
                        ? " hMerge=\"1\""
                        : cell.GridSpan > 1
                            ? $" gridSpan=\"{cell.GridSpan}\""
                            : string.Empty;
                    rowXml.Append($"""
<a:tc{mergeAttributes}>
  {BuildTableTextBody(cell)}
  <a:tcPr marL="{Emu(cell.LeftMargin)}" marR="{Emu(cell.RightMargin)}" marT="{Emu(cell.TopMargin)}" marB="{Emu(cell.BottomMargin)}" anchor="{VerticalAnchor(cell.VerticalAnchor)}">
    {TableBorders(cell.Borders)}
    <a:solidFill><a:srgbClr val="{CleanColor(cell.Fill)}"/></a:solidFill>
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
            ArgumentNullException.ThrowIfNull(content);
            EnsureFrame(x, y, width, height, name);

            var imageType = DetectImagePartType(content, contentType, name);
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
            string verticalAnchor,
            string name,
            bool isTextBox)
        {
            var textXml = text is null
                ? string.Empty
                : BuildTextBody(text, fontSize, color, bold, align, verticalAnchor);
            AddShapeXml(
                x,
                y,
                width,
                height,
                fill,
                line,
                lineWidth,
                geometry,
                textXml,
                name,
                isTextBox);
        }

        private void AddRichTextShape(
            double x,
            double y,
            double width,
            double height,
            string? fill,
            string? line,
            double lineWidth,
            string geometry,
            IReadOnlyList<RichTextParagraph> paragraphs,
            string name,
            bool isTextBox,
            string verticalAnchor,
            bool allowAutoFit,
            double leftInset,
            double rightInset,
            double topInset,
            double bottomInset)
        {
            ArgumentNullException.ThrowIfNull(paragraphs);
            var textXml = BuildRichTextBody(
                paragraphs,
                verticalAnchor,
                allowAutoFit,
                leftInset,
                rightInset,
                topInset,
                bottomInset);
            AddShapeXml(
                x,
                y,
                width,
                height,
                fill,
                line,
                lineWidth,
                geometry,
                textXml,
                name,
                isTextBox);
        }

        private void AddShapeXml(
            double x,
            double y,
            double width,
            double height,
            string? fill,
            string? line,
            double lineWidth,
            string geometry,
            string textXml,
            string name,
            bool isTextBox,
            string? geometryAdjustmentsXml = null)
        {
            EnsureFrame(x, y, width, height, name);
            var id = _nextShapeId++;
            var fillXml = string.IsNullOrWhiteSpace(fill)
                ? "<a:noFill/>"
                : $"<a:solidFill><a:srgbClr val=\"{CleanColor(fill)}\"/></a:solidFill>";
            var lineXml = string.IsNullOrWhiteSpace(line)
                ? "<a:ln><a:noFill/></a:ln>"
                : $"<a:ln w=\"{LineWidth(lineWidth)}\"><a:solidFill><a:srgbClr val=\"{CleanColor(line)}\"/></a:solidFill></a:ln>";
            var nonVisualShapeProperties = isTextBox
                ? "<p:cNvSpPr txBox=\"1\"/>"
                : "<p:cNvSpPr/>";
            var geometryXml = string.IsNullOrWhiteSpace(geometryAdjustmentsXml)
                ? $"<a:prstGeom prst=\"{geometry}\"><a:avLst/></a:prstGeom>"
                : $"<a:prstGeom prst=\"{geometry}\"><a:avLst>{geometryAdjustmentsXml}</a:avLst></a:prstGeom>";

            _elements.Add($"""
<p:sp>
  <p:nvSpPr><p:cNvPr id="{id}" name="{Escape(name)}"/>{nonVisualShapeProperties}<p:nvPr/></p:nvSpPr>
  <p:spPr><a:xfrm><a:off x="{Emu(x)}" y="{Emu(y)}"/><a:ext cx="{Emu(width)}" cy="{Emu(height)}"/></a:xfrm>{geometryXml}{fillXml}{lineXml}</p:spPr>
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
            return $"<a:txBody><a:bodyPr wrap=\"square\" lIns=\"0\" rIns=\"0\" tIns=\"0\" bIns=\"0\" anchor=\"{VerticalAnchor(cell.VerticalAnchor)}\"/><a:lstStyle/>{string.Join(string.Empty, paragraphs)}</a:txBody>";
        }

        private string TableBorders(NativeTableBorders? borders)
        {
            if (borders is null)
            {
                borders = new NativeTableBorders(
                    Theme.Border,
                    .25,
                    Theme.Border,
                    .25,
                    Theme.Border,
                    .25,
                    Theme.Border,
                    .25);
            }

            return string.Concat(
                TableBorder("L", borders.LeftColor, borders.LeftWidth),
                TableBorder("R", borders.RightColor, borders.RightWidth),
                TableBorder("T", borders.TopColor, borders.TopWidth),
                TableBorder("B", borders.BottomColor, borders.BottomWidth));
        }

        private static string TableBorder(string side, string? color, double width)
        {
            if (width <= 0 || string.IsNullOrWhiteSpace(color))
            {
                return $"<a:ln{side}><a:noFill/></a:ln{side}>";
            }

            var line = $"<a:solidFill><a:srgbClr val=\"{CleanColor(color)}\"/></a:solidFill><a:prstDash val=\"solid\"/>";
            return $"<a:ln{side} w=\"{LineWidth(width)}\">{line}</a:ln{side}>";
        }

        private static string BuildRichTextBody(
            IReadOnlyList<RichTextParagraph> paragraphs,
            string verticalAnchor,
            bool allowAutoFit,
            double leftInset,
            double rightInset,
            double topInset,
            double bottomInset)
        {
            var anchor = VerticalAnchor(verticalAnchor);
            var autoFit = allowAutoFit
                ? "<a:normAutofit fontScale=\"94000\" lnSpcReduction=\"6000\"/>"
                : "<a:noAutofit/>";
            var paragraphXml = new StringBuilder();

            foreach (var paragraph in paragraphs)
            {
                if (paragraph.Runs.Count == 0)
                {
                    continue;
                }

                var alignment = Alignment(paragraph.Align);
                var lineSpacingXml = paragraph.LineSpacingPoints.HasValue
                    ? $"<a:lnSpc><a:spcPts val=\"{FontSize(paragraph.LineSpacingPoints.Value)}\"/></a:lnSpc>"
                    : string.Empty;
                var spaceAfterXml = paragraph.SpaceAfterPoints > 0
                    ? $"<a:spcAft><a:spcPts val=\"{FontSize(paragraph.SpaceAfterPoints)}\"/></a:spcAft>"
                    : string.Empty;
                var tabXml = paragraph.TabStopInches.HasValue
                    ? $"<a:tabLst><a:tab pos=\"{Emu(paragraph.TabStopInches.Value)}\"/></a:tabLst>"
                    : string.Empty;
                var marginAttributes = $" marL=\"{Emu(paragraph.LeftMarginInches)}\" indent=\"{Emu(paragraph.FirstLineIndentInches)}\"";

                paragraphXml.Append($"<a:p><a:pPr algn=\"{alignment}\"{marginAttributes}>{lineSpacingXml}{spaceAfterXml}{tabXml}</a:pPr>");
                for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
                {
                    var run = paragraph.Runs[runIndex];
                    var runText = paragraph.TabAfterFirstRun
                        && runIndex == 0
                        && paragraph.Runs.Count > 1
                            ? Escape(run.Text) + "&#x9;"
                            : Escape(run.Text);
                    paragraphXml.Append($"""
<a:r><a:rPr lang="en-IN" sz="{FontSize(run.FontSize)}" b="{(run.Bold ? 1 : 0)}" i="{(run.Italic ? 1 : 0)}"><a:solidFill><a:srgbClr val="{CleanColor(run.Color)}"/></a:solidFill><a:latin typeface="Aptos"/></a:rPr><a:t xml:space="preserve">{runText}</a:t></a:r>
""");
                }

                var finalRun = paragraph.Runs[^1];
                paragraphXml.Append($"<a:endParaRPr lang=\"en-IN\" sz=\"{FontSize(finalRun.FontSize)}\"/></a:p>");
            }

            return $"<p:txBody><a:bodyPr wrap=\"square\" vertOverflow=\"clip\" horzOverflow=\"clip\" lIns=\"{Emu(leftInset)}\" rIns=\"{Emu(rightInset)}\" tIns=\"{Emu(topInset)}\" bIns=\"{Emu(bottomInset)}\" anchor=\"{anchor}\">{autoFit}</a:bodyPr><a:lstStyle/>{paragraphXml}</p:txBody>";
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

        private static PartTypeInfo DetectImagePartType(
            ReadOnlySpan<byte> content,
            string? declaredContentType,
            string name)
        {
            if (content.Length >= 8
                && content[0] == 0x89
                && content[1] == 0x50
                && content[2] == 0x4E
                && content[3] == 0x47
                && content[4] == 0x0D
                && content[5] == 0x0A
                && content[6] == 0x1A
                && content[7] == 0x0A)
            {
                return ImagePartType.Png;
            }

            if (content.Length >= 3
                && content[0] == 0xFF
                && content[1] == 0xD8
                && content[2] == 0xFF)
            {
                return ImagePartType.Jpeg;
            }

            throw new InvalidOperationException(
                $"The image '{SanitizeOpenXmlText(name)}' is not a supported PNG or JPEG payload. " +
                $"Declared content type: {declaredContentType ?? "not supplied"}.");
        }

        private static void EnsureFrame(double x, double y, double width, double height, string name)
        {
            EnsureFinite(x, nameof(x));
            EnsureFinite(y, nameof(y));
            if (width <= 0 || !double.IsFinite(width))
            {
                throw new ArgumentOutOfRangeException(nameof(width), $"The width for '{SanitizeOpenXmlText(name)}' must be finite and positive.");
            }
            if (height <= 0 || !double.IsFinite(height))
            {
                throw new ArgumentOutOfRangeException(nameof(height), $"The height for '{SanitizeOpenXmlText(name)}' must be finite and positive.");
            }
        }

        private static void EnsureFinite(double value, string parameterName)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, "PowerPoint coordinates must be finite values.");
            }
        }

        private static string Alignment(string value) => value switch { "ctr" => "ctr", "r" => "r", _ => "l" };
        private static string VerticalAnchor(string value) => value switch { "t" => "t", "b" => "b", _ => "ctr" };
        private static long Emu(double inches) => (long)Math.Round(inches * 914400d);
        private static long LineWidth(double points) => (long)Math.Round(points * 12700d);
        private static int FontSize(double points) => (int)Math.Round(points * 100d);
        private static string Escape(string? value)
            => SecurityElement.Escape(SanitizeOpenXmlText(value)) ?? string.Empty;
        private static string CleanColor(string value) => value.Trim().TrimStart('#').ToUpperInvariant();
    }
}

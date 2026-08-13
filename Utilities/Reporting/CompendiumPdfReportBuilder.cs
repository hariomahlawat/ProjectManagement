using System.Globalization;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services.Compendiums;
using ProjectManagement.Utilities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProjectManagement.Utilities.Reporting;

public interface ICompendiumPdfReportBuilder
{
    byte[] Build(CompendiumPdfReportContext context);
}

public sealed record CompendiumPdfReportContext(
    string Title,
    string Subtitle,
    string UnitDisplayName,
    string IssuerDisplayName,
    string? HandlingMarking,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<CompendiumPdfCategorySection> Categories,
    bool ShowMissingPhotoPlaceholder)
{
    public string Edition { get; init; } = string.Empty;
    public byte[]? CoverHero { get; init; }
    public CompendiumPagePlan? Plan { get; init; }
}

public sealed record CompendiumPdfCategorySection(
    string CategoryName,
    IReadOnlyList<CompendiumPdfProjectSection> Projects);

public sealed record CompendiumPdfProjectSection(
    int ProjectId,
    string ProjectName,
    string? CaseFileNumber,
    string CategoryName,
    string CompletionYearDisplay,
    string ArmServiceDisplay,
    string ProliferationCostDisplay,
    string? ProliferationCostRemarks,
    string DescriptionMarkdown,
    byte[]? CoverPhoto,
    bool PhotoWasSelected)
{
    public string LifecycleDisplay { get; init; } = "Completed";
    public string? ProjectCategoryDisplay { get; init; }
    public bool IsAvailableForProliferation { get; init; }
    public bool? ProliferationAvailability { get; init; }
}

/// <summary>
/// Phase 24 A4 portrait compositor. Page membership is decided by CompendiumPagePlanner before
/// drawing; this class renders that plan without changing project membership or pagination policy.
/// </summary>
public sealed class CompendiumPdfReportBuilder : ICompendiumPdfReportBuilder
{
    private const string Forest950 = "#102A23";
    private const string Forest900 = "#17382F";
    private const string Forest800 = "#205244";
    private const string Forest100 = "#EAF3EF";
    private const string Forest50 = "#F5F9F7";
    private const string Gold = "#C9A646";
    private const string GoldSoft = "#E9D9A7";
    private const string Ink = "#14221D";
    private const string Slate700 = "#334155";
    private const string Slate600 = "#475569";
    private const string Slate500 = "#64748B";
    private const string Slate300 = "#CBD5E1";
    private const string Slate200 = "#E2E8F0";
    private const string Slate100 = "#F1F5F9";
    private const string Slate50 = "#F8FAFC";
    private const string White = "#FFFFFF";

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<CompendiumPdfReportBuilder> _logger;

    static CompendiumPdfReportBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public CompendiumPdfReportBuilder(
        IWebHostEnvironment environment,
        ILogger<CompendiumPdfReportBuilder> logger)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public byte[] Build(CompendiumPdfReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var title = Normalize(context.Title, "Simulators Compendium");
        var subtitle = Normalize(context.Subtitle, "Detailed Project Reference");
        var issuer = Normalize(context.IssuerDisplayName, "Simulator Development Division");
        var marking = NormalizeOptional(context.HandlingMarking)?.ToUpperInvariant();
        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());
        var edition = Normalize(context.Edition, $"Capability Edition · {generatedAtIst.Year}");
        var crest = TryLoadAsset("img/logos/artrac.png");
        var sddMark = TryLoadAsset("img/logos/sdd.png");
        var footerLogo = sddMark;
        var plan = context.Plan ?? new CompendiumPagePlanner().Plan(context);

        var document = Document
            .Create(container =>
            {
                foreach (var planned in plan.Pages)
                {
                    switch (planned.Kind)
                    {
                        case CompendiumPageKind.Cover:
                            ComposeCover(container, title, subtitle, edition, marking, context.CoverHero, crest, sddMark);
                            break;
                        case CompendiumPageKind.Index:
                            ComposeIndexPage(container, planned, title, edition, issuer, marking, footerLogo, plan.IndexPageCount);
                            break;
                        case CompendiumPageKind.Project:
                            ComposeProjectPage(container, planned, issuer, marking, footerLogo);
                            break;
                        case CompendiumPageKind.ProjectContinuation:
                            ComposeProjectContinuationPage(container, planned, issuer, marking, footerLogo);
                            break;
                        case CompendiumPageKind.BackCover:
                            ComposeBackCover(container, title, subtitle, edition, marking, crest, sddMark);
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported Compendium page kind: {planned.Kind}.");
                    }
                }
            })
            .WithMetadata(new DocumentMetadata
            {
                Title = $"{title} — {subtitle}",
                Author = issuer,
                Subject = "Detailed project reference generated from selected PRISM project records.",
                Keywords = "projects, simulators, capabilities, SDD, PRISM ERP",
                Creator = "PRISM ERP",
                Producer = "PRISM ERP / QuestPDF",
                CreationDate = context.GeneratedAtUtc,
                ModifiedDate = context.GeneratedAtUtc
            });

        return document.GeneratePdf();
    }

    private static void ComposeCover(
        IDocumentContainer container,
        string title,
        string subtitle,
        string edition,
        string? marking,
        byte[]? coverHero,
        byte[]? crest,
        byte[]? sddMark)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => BaseStyle(style).FontColor(White));

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Background(Forest950).PaddingHorizontal(52).PaddingVertical(48).Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Row(row =>
                    {
                        if (crest is { Length: > 0 })
                        {
                            row.ConstantItem(48).Height(48).Image(crest).FitArea();
                            row.ConstantItem(12);
                        }

                        row.RelativeItem();

                        if (sddMark is { Length: > 0 })
                        {
                            row.ConstantItem(42).Height(42).AlignMiddle().Image(sddMark).FitArea();
                        }
                    });

                    if (!string.IsNullOrWhiteSpace(marking))
                    {
                        column.Item().PaddingTop(2).AlignCenter().Text(marking)
                            .FontSize(8.5f)
                            .SemiBold()
                            .LetterSpacing(.8f)
                            .FontColor(GoldSoft);
                    }

                    column.Item().PaddingTop(50).Text(title)
                        .FontSize(34)
                        .SemiBold()
                        .LineHeight(1.04f)
                        .FontColor(White);
                    column.Item().Width(128).Height(3).Background(Gold);
                    column.Item().Text(subtitle)
                        .FontSize(15)
                        .LineHeight(1.18f)
                        .FontColor("#D7E3DE");
                    column.Item().Text(edition)
                        .FontSize(10.5f)
                        .SemiBold()
                        .FontColor(GoldSoft);

                    column.Item().PaddingTop(28).Element(frame => ComposeCoverHero(frame, coverHero));
                });

                layers.Layer().AlignTop().Height(6).Background(Gold);
                layers.Layer().AlignBottom().Height(16).Background(Forest900);
            });
        });
    }

    private static void ComposeCoverHero(IContainer container, byte[]? hero)
    {
        if (hero is { Length: > 0 })
        {
            container.Height((float)CompendiumCoverImagePolicy.FrameHeightPoints)
                .Background(Forest900)
                .Image(hero)
                .FitArea();
            return;
        }

        container.Height((float)CompendiumCoverImagePolicy.FrameHeightPoints).Layers(layers =>
        {
            layers.PrimaryLayer().Background(Forest900);
            layers.Layer().AlignTop().Height(3).Background(Gold);
            layers.Layer().AlignBottom().Height(86).Background(Forest800);
        });
    }

    private static void ComposeIndexPage(
        IDocumentContainer container,
        CompendiumPagePlanItem planned,
        string title,
        string edition,
        string issuer,
        string? marking,
        byte[]? footerLogo,
        int totalIndexPages)
    {
        container.Page(page =>
        {
            ConfigureStandardPage(page);
            page.Header().Element(header => ComposeRunningHeader(header, "COMPENDIUM INDEX", edition, marking));
            page.Content().PaddingTop(12).Column(content =>
            {
                content.Spacing(12);
                content.Item().Row(row =>
                {
                    row.RelativeItem().Column(copy =>
                    {
                        copy.Item().Text("Compendium Index")
                            .FontSize(22)
                            .SemiBold()
                            .FontColor(Ink);
                        copy.Item().Text(title)
                            .FontSize(9.5f)
                            .FontColor(Slate500);
                    });
                    if (totalIndexPages > 1)
                    {
                        row.AutoItem().AlignBottom().Text($"Index page {planned.PhysicalPageNumber - 1} of {totalIndexPages}")
                            .FontSize(8)
                            .FontColor(Slate500);
                    }
                });

                content.Item().Height(2).Background(Gold);

                foreach (var group in planned.IndexGroups)
                {
                    content.Item().Element(element => ComposeIndexGroup(element, group));
                }
            });
            page.Footer().Element(footer => ComposeFooter(footer, issuer, marking, footerLogo));
        });
    }

    private static void ComposeIndexGroup(IContainer container, CompendiumIndexGroupPlan group)
    {
        container.Column(column =>
        {
            column.Spacing(0);
            column.Item().Background(Forest100).BorderLeft(4).BorderColor(Forest800).PaddingHorizontal(10).PaddingVertical(7)
                .Row(row =>
                {
                    row.RelativeItem().Text(group.CategoryName)
                        .FontSize(11.5f)
                        .SemiBold()
                        .FontColor(Forest900);
                    row.AutoItem().Text($"{group.Projects.Count} project{(group.Projects.Count == 1 ? string.Empty : "s")}")
                        .FontSize(8)
                        .FontColor(Slate500);
                });

            foreach (var project in group.Projects)
            {
                column.Item().BorderBottom(1).BorderColor(Slate200).PaddingHorizontal(10).PaddingVertical(6).Row(row =>
                {
                    row.RelativeItem().SectionLink(ProjectAnchorId(project.ProjectId)).Text(project.ProjectName)
                        .FontSize(9.3f)
                        .FontColor(Ink);
                    row.ConstantItem(76).AlignRight().Text(
                            string.Equals(project.LifecycleDisplay, "Completed", StringComparison.OrdinalIgnoreCase)
                                ? project.CompletionDisplay
                                : project.LifecycleDisplay)
                        .FontSize(8.5f)
                        .FontColor(Slate500);
                    row.ConstantItem(34).AlignRight().Text(project.ProjectPageNumber.ToString(CultureInfo.InvariantCulture))
                        .FontSize(9)
                        .SemiBold()
                        .FontColor(Forest800);
                });
            }
        });
    }

    private static void ComposeProjectPage(
        IDocumentContainer container,
        CompendiumPagePlanItem planned,
        string issuer,
        string? marking,
        byte[]? footerLogo)
    {
        var project = planned.Project ?? throw new InvalidOperationException("Project page is missing its project payload.");
        container.Page(page =>
        {
            ConfigureStandardPage(page);
            page.Header().Element(header => ComposeRunningHeader(header, project.CategoryName.ToUpperInvariant(), project.LifecycleDisplay, marking));
            page.Content().PaddingTop(10).Section(ProjectAnchorId(project.ProjectId)).Column(column =>
            {
                column.Spacing(10);
                if (planned.IsFirstProjectInCategory)
                {
                    column.Item().Background(Forest900).PaddingHorizontal(12).PaddingVertical(8).Text(project.CategoryName)
                        .FontSize(12)
                        .SemiBold()
                        .FontColor(White);
                }

                column.Item().Text(project.ProjectName)
                    .FontSize(CompendiumLayoutMetrics.ProjectTitleFontSize)
                    .SemiBold()
                    .LineHeight(1.06f)
                    .FontColor(Ink);

                column.Item().Element(meta => ComposeProjectMetadata(meta, project));

                if (planned.ProjectLayout != CompendiumProjectLayoutVariant.NoPhoto
                    && project.CoverPhoto is { Length: > 0 })
                {
                    column.Item().Height(CompendiumLayoutMetrics.ProjectImageHeightPoints(planned.ProjectLayout))
                        .Background(Slate100)
                        .Image(project.CoverPhoto)
                        .FitArea();
                }
                else
                {
                    column.Item().Element(element => ComposeNoPhotoTreatment(element, project));
                }

                column.Item().Element(description => ComposeDescription(
                    description,
                    planned.DescriptionMarkdown,
                    continuation: false));
            });
            page.Footer().Element(footer => ComposeFooter(footer, issuer, marking, footerLogo));
        });
    }

    private static void ComposeProjectContinuationPage(
        IDocumentContainer container,
        CompendiumPagePlanItem planned,
        string issuer,
        string? marking,
        byte[]? footerLogo)
    {
        var project = planned.Project ?? throw new InvalidOperationException("Continuation page is missing its project payload.");
        container.Page(page =>
        {
            ConfigureStandardPage(page);
            page.Header().Element(header => ComposeRunningHeader(header, project.CategoryName.ToUpperInvariant(), project.LifecycleDisplay, marking));
            page.Content().PaddingTop(14).Column(column =>
            {
                column.Spacing(12);
                column.Item().Text(project.ProjectName)
                    .FontSize(16)
                    .SemiBold()
                    .LineHeight(1.08f)
                    .FontColor(Ink);
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("Project description · continued")
                        .FontSize(10)
                        .SemiBold()
                        .FontColor(Forest800);
                    row.AutoItem().Text($"CONTINUED · {planned.ContinuationPart + 1}")
                        .FontSize(7.5f)
                        .SemiBold()
                        .LetterSpacing(.6f)
                        .FontColor(Slate500);
                });
                column.Item().Height(2).Background(Gold);
                column.Item().Element(description => ComposeDescription(
                    description,
                    planned.DescriptionMarkdown,
                    continuation: true));
            });
            page.Footer().Element(footer => ComposeFooter(footer, issuer, marking, footerLogo));
        });
    }

    private static void ComposeProjectMetadata(IContainer container, CompendiumPdfProjectSection project)
    {
        var items = new List<(string Key, string Value)>();
        if (!string.IsNullOrWhiteSpace(project.ProjectCategoryDisplay))
        {
            items.Add(("Project category", project.ProjectCategoryDisplay!));
        }
        items.Add(("Technical category", project.CategoryName));
        items.Add(("Status", project.LifecycleDisplay));
        if (string.Equals(project.LifecycleDisplay, "Completed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(project.CompletionYearDisplay)
            && !string.Equals(project.CompletionYearDisplay, "Not recorded", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(("Completed", project.CompletionYearDisplay));
        }
        if (!string.Equals(project.ArmServiceDisplay, "Not recorded", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(("Arm / Service", project.ArmServiceDisplay));
        }
        if (project.ProliferationAvailability.HasValue)
        {
            items.Add(("Proliferation", project.ProliferationAvailability.Value
                ? "Available"
                : "Not available"));
        }
        if (project.ProliferationAvailability == true
            && !string.IsNullOrWhiteSpace(project.ProliferationCostDisplay)
            && !string.Equals(project.ProliferationCostDisplay, "Not recorded", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(("Indicative cost", project.ProliferationCostDisplay));
        }
        if (!string.IsNullOrWhiteSpace(project.CaseFileNumber))
        {
            items.Add(("Project reference", project.CaseFileNumber!));
        }

        container.Background(Forest50).Border(1).BorderColor("#DCE7E2").Padding(9).Column(column =>
        {
            column.Spacing(7);
            for (var index = 0; index < items.Count; index += 2)
            {
                var first = items[index];
                var second = index + 1 < items.Count ? items[index + 1] : default;
                column.Item().Row(row =>
                {
                    row.RelativeItem().Element(cell => ComposeMetadataCell(cell, first.Key, first.Value));
                    if (!string.IsNullOrWhiteSpace(second.Key))
                    {
                        row.ConstantItem(18);
                        row.RelativeItem().Element(cell => ComposeMetadataCell(cell, second.Key, second.Value));
                    }
                    else
                    {
                        row.ConstantItem(18);
                        row.RelativeItem().Text(string.Empty);
                    }
                });
            }

            if (!string.IsNullOrWhiteSpace(project.ProliferationCostRemarks))
            {
                column.Item().PaddingTop(2).BorderTop(1).BorderColor("#DCE7E2").PaddingTop(6)
                    .Text(project.ProliferationCostRemarks!)
                    .FontSize(8.4f)
                    .FontColor(Slate600)
                    .LineHeight(1.18f);
            }
        });
    }

    private static void ComposeMetadataCell(IContainer container, string key, string value)
    {
        container.Column(column =>
        {
            column.Item().Text(key.ToUpperInvariant())
                .FontSize(7)
                .SemiBold()
                .LetterSpacing(.45f)
                .FontColor(Slate500);
            column.Item().Text(value)
                .FontSize(9.2f)
                .SemiBold()
                .FontColor(Ink);
        });
    }

    private static void ComposeNoPhotoTreatment(IContainer container, CompendiumPdfProjectSection project)
    {
        container.Height(96).Background(Forest100).BorderLeft(4).BorderColor(Gold).PaddingHorizontal(16).PaddingVertical(14).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(project.CategoryName.ToUpperInvariant())
                    .FontSize(8)
                    .SemiBold()
                    .LetterSpacing(.8f)
                    .FontColor(Forest800);
                column.Item().PaddingTop(5).Text(project.ProjectName)
                    .FontSize(12.5f)
                    .SemiBold()
                    .FontColor(Forest950);
            });
            row.AutoItem().AlignMiddle().Text("PROJECT REFERENCE")
                .FontSize(7)
                .SemiBold()
                .LetterSpacing(.8f)
                .FontColor(Slate500);
        });
    }

    private static void ComposeDescription(IContainer container, string markdown, bool continuation)
    {
        container.Column(column =>
        {
            column.Spacing(7);
            if (!continuation)
            {
                column.Item().Text("Project description")
                    .FontSize(10.5f)
                    .SemiBold()
                    .FontColor(Forest900);
            }

            if (string.IsNullOrWhiteSpace(markdown))
            {
                column.Item().Text("Project description not recorded.")
                    .FontSize(CompendiumLayoutMetrics.ProjectBodyFontSize)
                    .Italic()
                    .FontColor(Slate500);
                return;
            }

            column.Item()
                .DefaultTextStyle(style => BaseStyle(style)
                    .FontSize(continuation
                        ? CompendiumLayoutMetrics.ContinuationBodyFontSize
                        : CompendiumLayoutMetrics.ProjectBodyFontSize)
                    .FontColor(Slate700)
                    .LineHeight(1.25f))
                .Element(element => MarkdownPdfRenderer.Render(element, markdown));
        });
    }

    private static void ComposeBackCover(
        IDocumentContainer container,
        string title,
        string subtitle,
        string edition,
        string? marking,
        byte[]? crest,
        byte[]? sddMark)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => BaseStyle(style).FontColor(White));
            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Background(Forest950).PaddingHorizontal(58).PaddingVertical(54).Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        if (crest is { Length: > 0 })
                        {
                            row.ConstantItem(46).Height(46).Image(crest).FitArea();
                        }
                        row.RelativeItem();
                        if (sddMark is { Length: > 0 })
                        {
                            row.ConstantItem(42).Height(42).Image(sddMark).FitArea();
                        }
                    });

                    column.Item().PaddingTop(205).Width(110).Height(3).Background(Gold);
                    column.Item().PaddingTop(16).Text(title)
                        .FontSize(24)
                        .SemiBold()
                        .FontColor(White);
                    column.Item().PaddingTop(8).Text(subtitle)
                        .FontSize(12)
                        .FontColor("#D7E3DE");
                    column.Item().PaddingTop(16).Text(edition)
                        .FontSize(10)
                        .SemiBold()
                        .FontColor(GoldSoft);

                    if (!string.IsNullOrWhiteSpace(marking))
                    {
                        column.Item().PaddingTop(245).Text(marking)
                            .FontSize(8.5f)
                            .SemiBold()
                            .LetterSpacing(.8f)
                            .FontColor(GoldSoft);
                    }
                });
                layers.Layer().AlignTop().Height(6).Background(Gold);
                layers.Layer().AlignBottom().Height(16).Background(Forest900);
            });
        });
    }

    private static void ConfigureStandardPage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginTop(CompendiumLayoutMetrics.TopMarginPoints);
        page.MarginLeft(CompendiumLayoutMetrics.HorizontalMarginPoints);
        page.MarginRight(CompendiumLayoutMetrics.HorizontalMarginPoints);
        page.MarginBottom(0);
        page.PageColor(White);
        page.DefaultTextStyle(style => BaseStyle(style).FontSize(10f).FontColor(Ink));
    }

    private static void ComposeRunningHeader(IContainer container, string left, string right, string? marking)
    {
        container.PaddingBottom(7).BorderBottom(1).BorderColor(Slate200).Row(row =>
        {
            row.RelativeItem().Text(left)
                .FontSize(7.6f)
                .SemiBold()
                .LetterSpacing(.65f)
                .FontColor(Forest800);
            row.AutoItem().Text(right)
                .FontSize(7.5f)
                .FontColor(Slate500);
            if (!string.IsNullOrWhiteSpace(marking))
            {
                row.ConstantItem(12);
                row.AutoItem().Text(marking)
                    .FontSize(7.3f)
                    .SemiBold()
                    .FontColor("#8B6B18");
            }
        });
    }

    private static void ComposeFooter(IContainer container, string issuer, string? marking, byte[]? logo)
    {
        container.Height(CompendiumLayoutMetrics.FooterHeightPoints).PaddingTop(5).BorderTop(1).BorderColor(Slate200).Row(row =>
        {
            row.RelativeItem().Row(left =>
            {
                if (logo is { Length: > 0 })
                {
                    left.ConstantItem(18).Height(18).AlignMiddle().Image(logo).FitArea();
                    left.ConstantItem(6);
                }
                left.RelativeItem().AlignMiddle().Text(issuer)
                    .FontSize(7.8f)
                    .SemiBold()
                    .FontColor(Slate600);
            });

            if (!string.IsNullOrWhiteSpace(marking))
            {
                row.AutoItem().PaddingHorizontal(10).AlignMiddle().Text(marking)
                    .FontSize(7)
                    .SemiBold()
                    .FontColor("#8B6B18");
            }

            row.RelativeItem().AlignRight().AlignMiddle().Text(text =>
            {
                text.DefaultTextStyle(BaseStyle(TextStyle.Default).FontSize(7.8f).FontColor(Slate500));
                text.Span("Page ");
                text.CurrentPageNumber().SemiBold();
                text.Span(" / ");
                text.TotalPages().SemiBold();
            });
        });
    }

    private static TextStyle BaseStyle(TextStyle style)
        => style.DisableFontFeature(FontFeatures.StandardLigatures);

    private static string ProjectAnchorId(int projectId)
        => $"compendium-project-{projectId.ToString(CultureInfo.InvariantCulture)}";

    private byte[]? TryLoadAsset(string relativeUnderWwwRoot)
    {
        try
        {
            var relative = relativeUnderWwwRoot.Trim().Replace('\\', '/');
            var path = Path.Combine(
                _environment.WebRootPath,
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                _logger.LogWarning("Compendium PDF asset was not found at {AssetPath}.", path);
                return null;
            }
            return File.ReadAllBytes(path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to load compendium PDF asset {RelativeAssetPath}.", relativeUnderWwwRoot);
            return null;
        }
    }

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

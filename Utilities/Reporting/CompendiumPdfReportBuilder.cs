using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ProjectManagement.Utilities;

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
    bool ShowMissingPhotoPlaceholder);

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
    bool PhotoWasSelected);

// SECTION: Publication-quality QuestPDF composition for the SDD Simulators Compendium.
public sealed class CompendiumPdfReportBuilder : ICompendiumPdfReportBuilder
{
    private const string Navy = "#0B1220";
    private const string NavySoft = "#111C33";
    private const string Blue = "#1E3A8A";
    private const string Gold = "#D4AF37";
    private const string Slate900 = "#0F172A";
    private const string Slate700 = "#334155";
    private const string Slate600 = "#475569";
    private const string Slate500 = "#64748B";
    private const string Slate300 = "#CBD5E1";
    private const string Slate200 = "#E2E8F0";
    private const string Slate100 = "#F1F5F9";
    private const string Slate50 = "#F8FAFC";

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
        var subtitle = Normalize(context.Subtitle, "Available for Proliferation");
        var unit = Normalize(context.UnitDisplayName, "Simulator Development Division");
        var issuer = Normalize(context.IssuerDisplayName, "Simulator Development Division");
        var marking = NormalizeOptional(context.HandlingMarking)?.ToUpperInvariant();
        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());
        var generatedDate = generatedAtIst.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);
        var projectCount = context.Categories.Sum(category => category.Projects.Count);
        var footerLogo = TryLoadAsset("img/logos/sdd.png");
        var crest = TryLoadAsset("img/logos/artrac.png");
        var sddMark = TryLoadAsset("img/logos/sdd.png");

        var document = Document
            .Create(container =>
            {
                ComposeCover(
                    container,
                    title,
                    subtitle,
                    unit,
                    issuer,
                    marking,
                    generatedDate,
                    projectCount,
                    context.Categories.Count,
                    crest,
                    sddMark);

                ComposeIndex(
                    container,
                    title,
                    subtitle,
                    issuer,
                    marking,
                    context.Categories,
                    footerLogo);

                foreach (var category in context.Categories)
                {
                    foreach (var project in category.Projects)
                    {
                        ComposeProjectPage(
                            container,
                            project,
                            issuer,
                            marking,
                            context.ShowMissingPhotoPlaceholder,
                            footerLogo);
                    }
                }
            })
            .WithMetadata(new DocumentMetadata
            {
                Title = $"{title} — {subtitle}",
                Author = issuer,
                Subject = $"Completed simulators recorded as {subtitle.ToLowerInvariant()}.",
                Keywords = "simulators, proliferation, SDD, PRISM ERP, training systems",
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
        string unit,
        string issuer,
        string? marking,
        string generatedDate,
        int projectCount,
        int categoryCount,
        byte[]? crest,
        byte[]? sddMark)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor("#FFFFFF");
            page.DefaultTextStyle(style => BaseStyle(style).FontColor("#FFFFFF"));

            page.Content().Layers(layers =>
            {
                layers.Layer().Background(Navy);

                layers.Layer()
                    .AlignLeft()
                    .AlignTop()
                    .TranslateX(-120)
                    .TranslateY(160)
                    .Rotate(-10)
                    .Element(band => band
                        .Width(895)
                        .Height(120)
                        .Background(NavySoft));

                layers.Layer()
                    .AlignLeft()
                    .AlignTop()
                    .TranslateX(-120)
                    .TranslateY(150)
                    .Rotate(-10)
                    .Element(band => band
                        .Width(895)
                        .Height(6)
                        .Background(Gold));

                layers.PrimaryLayer().PaddingHorizontal(68).PaddingVertical(58).Column(column =>
                {
                    column.Spacing(12);

                    column.Item().Row(row =>
                    {
                        if (crest is { Length: > 0 })
                        {
                            row.ConstantItem(48).Height(48).Image(crest).FitArea();
                            row.ConstantItem(12);
                        }

                        row.RelativeItem().AlignMiddle().Column(lockup =>
                        {
                            lockup.Item().Text(unit)
                                .FontSize(12)
                                .SemiBold()
                                .FontColor("#E2E8F0");
                            lockup.Item().Text("PRISM ERP")
                                .FontSize(9)
                                .FontColor("#94A3B8");
                        });

                        if (sddMark is { Length: > 0 })
                        {
                            row.ConstantItem(38).Height(38).AlignMiddle().Image(sddMark).FitArea();
                        }
                    });

                    if (!string.IsNullOrWhiteSpace(marking))
                    {
                        column.Item().PaddingTop(8).AlignCenter().Text(marking)
                            .FontSize(9)
                            .SemiBold()
                            .LetterSpacing(.8f)
                            .FontColor("#F8D568");
                    }

                    column.Item().PaddingTop(82).Text(title)
                        .FontSize(37)
                        .SemiBold()
                        .LineHeight(1.05f)
                        .FontColor("#FFFFFF");

                    column.Item().Width(145).Height(2).Background(Gold);

                    column.Item().Text(subtitle)
                        .FontSize(16)
                        .FontColor("#CBD5E1");

                    column.Item().PaddingTop(8).Text($"As on {generatedDate}")
                        .FontSize(11)
                        .FontColor("#94A3B8");

                    column.Item().PaddingTop(28).Row(summary =>
                    {
                        summary.ConstantItem(138).Element(box => ComposeCoverMetric(box, projectCount.ToString(CultureInfo.InvariantCulture), "SIMULATORS"));
                        summary.ConstantItem(12);
                        summary.ConstantItem(138).Element(box => ComposeCoverMetric(box, categoryCount.ToString(CultureInfo.InvariantCulture), "TECHNICAL CATEGORIES"));
                    });

                    column.Item().PaddingTop(185).Row(bottom =>
                    {
                        bottom.RelativeItem().Column(org =>
                        {
                            org.Item().Text(issuer)
                                .FontSize(11)
                                .SemiBold()
                                .FontColor("#E2E8F0");
                            org.Item().Text("Capability catalogue generated from the authoritative PRISM project record.")
                                .FontSize(8.5f)
                                .FontColor("#94A3B8");
                        });

                        if (!string.IsNullOrWhiteSpace(marking))
                        {
                            bottom.AutoItem().AlignBottom().Text(marking)
                                .FontSize(8)
                                .SemiBold()
                                .FontColor("#F8D568");
                        }
                    });
                });
            });
        });
    }

    private static void ComposeCoverMetric(IContainer container, string value, string label)
    {
        container.Border(1)
            .BorderColor("#263552")
            .Background("#101A2D")
            .PaddingHorizontal(14)
            .PaddingVertical(11)
            .Column(column =>
            {
                column.Item().Text(value).FontSize(21).SemiBold().FontColor("#FFFFFF");
                column.Item().Text(label).FontSize(7.5f).LetterSpacing(.7f).FontColor("#94A3B8");
            });
    }

    private static void ComposeIndex(
        IDocumentContainer container,
        string title,
        string subtitle,
        string issuer,
        string? marking,
        IReadOnlyList<CompendiumPdfCategorySection> categories,
        byte[]? footerLogo)
    {
        container.Page(page =>
        {
            ConfigureStandardPage(page);

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(header =>
                {
                    header.Item().Text("Index")
                        .FontSize(20)
                        .SemiBold()
                        .FontColor(Slate900);
                    header.Item().Text($"{title} · {subtitle}")
                        .FontSize(9.5f)
                        .FontColor(Slate500);
                });

                if (!string.IsNullOrWhiteSpace(marking))
                {
                    row.AutoItem().AlignTop().Text(marking)
                        .FontSize(8)
                        .SemiBold()
                        .FontColor("#9A6B00");
                }
            });

            page.Content().PaddingTop(14).Column(content =>
            {
                content.Spacing(14);

                if (categories.Count == 0)
                {
                    content.Item().PaddingTop(50).AlignCenter().Text("No eligible projects found.")
                        .FontSize(13)
                        .FontColor(Slate500);
                    return;
                }

                foreach (var category in categories)
                {
                    var categoryCopy = category;
                    content.Item().Element(element => ComposeIndexCategory(element, categoryCopy));
                }
            });

            page.Footer().Element(footer => ComposeFooter(
                footer.PaddingLeft(-35).PaddingRight(-35),
                issuer,
                marking,
                includePageNumber: true,
                footerLogo));
        });
    }

    private static void ComposeProjectPage(
        IDocumentContainer container,
        CompendiumPdfProjectSection project,
        string issuer,
        string? marking,
        bool showMissingPhotoPlaceholder,
        byte[]? footerLogo)
    {
        container.Page(page =>
        {
            ConfigureStandardPage(page);

            page.Header().Row(row =>
            {
                row.RelativeItem().Text(project.CategoryName.ToUpperInvariant())
                    .FontSize(7.5f)
                    .SemiBold()
                    .LetterSpacing(.7f)
                    .FontColor(Blue);

                if (!string.IsNullOrWhiteSpace(marking))
                {
                    row.AutoItem().Text(marking)
                        .FontSize(8)
                        .SemiBold()
                        .FontColor("#9A6B00");
                }
            });

            page.Content().PaddingTop(10).Section(ProjectAnchorId(project.ProjectId)).Column(column =>
            {
                column.Spacing(12);

                column.Item().Text(project.ProjectName)
                    .FontSize(20)
                    .SemiBold()
                    .LineHeight(1.08f)
                    .FontColor(Slate900);

                column.Item().Height(2).Background("#DCE5F2");

                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(metadata =>
                    {
                        metadata.Spacing(6);
                        metadata.Item().Element(item => ComposeKeyValue(item, "Technical category", project.CategoryName));
                        metadata.Item().Element(item => ComposeKeyValue(item, "Year of completion", project.CompletionYearDisplay));
                        metadata.Item().Element(item => ComposeKeyValue(item, "Arm/Service", project.ArmServiceDisplay));
                        metadata.Item().Element(item => ComposeKeyValue(item, "Indicative proliferation cost (₹ lakh)", project.ProliferationCostDisplay));

                        if (!string.IsNullOrWhiteSpace(project.CaseFileNumber))
                        {
                            metadata.Item().Element(item => ComposeKeyValue(item, "Project reference", project.CaseFileNumber!));
                        }

                        if (!string.IsNullOrWhiteSpace(project.ProliferationCostRemarks))
                        {
                            metadata.Item().PaddingTop(4).BorderLeft(2).BorderColor(Slate300).PaddingLeft(8)
                                .Text(project.ProliferationCostRemarks!)
                                .FontSize(8.5f)
                                .FontColor(Slate600)
                                .LineHeight(1.2f);
                        }
                    });

                    if (project.CoverPhoto is { Length: > 0 })
                    {
                        row.ConstantItem(228).Height(168).PaddingLeft(14).Element(image =>
                        {
                            image.Border(1)
                                .BorderColor(Slate200)
                                .Background(Slate50)
                                .Padding(7)
                                .AlignCenter()
                                .AlignMiddle()
                                .Image(project.CoverPhoto)
                                .FitArea();
                        });
                    }
                    else if (showMissingPhotoPlaceholder)
                    {
                        row.ConstantItem(228).Height(168).PaddingLeft(14).Element(ComposeMissingPhoto);
                    }
                });

                column.Item().Element(box =>
                {
                    box.Border(1)
                        .BorderColor(Slate200)
                        .Background("#FFFFFF")
                        .Padding(11)
                        .Column(description =>
                        {
                            description.Spacing(7);
                            description.Item().Text("Project description")
                                .FontSize(10.5f)
                                .SemiBold()
                                .FontColor(Slate700);
                            description.Item().Element(markdown => MarkdownPdfRenderer.Render(markdown, project.DescriptionMarkdown));
                        });
                });
            });

            page.Footer().Element(footer => ComposeFooter(
                footer.PaddingLeft(-35).PaddingRight(-35),
                issuer,
                marking,
                includePageNumber: true,
                footerLogo));
        });
    }

    private static void ConfigureStandardPage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginTop(30);
        page.MarginLeft(35);
        page.MarginRight(35);
        page.MarginBottom(0);
        page.PageColor("#FFFFFF");
        page.DefaultTextStyle(style => BaseStyle(style).FontSize(10.5f).FontColor("#1F2933"));
    }

    private static void ComposeIndexCategory(IContainer container, CompendiumPdfCategorySection category)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1);
                columns.ConstantColumn(68);
                columns.ConstantColumn(38);
            });

            // The header repeats automatically when a category table spans pages.
            // Keeping the category label inside the table header prevents orphaned continuation rows.
            table.Header(header =>
            {
                header.Cell().Element(CategoryHeaderCell).Text(category.CategoryName);
                header.Cell().Element(CategoryHeaderCell).Text(string.Empty);
                header.Cell().Element(CategoryHeaderCell).Text(string.Empty);

                header.Cell().Element(IndexColumnHeader).Text("Simulator");
                header.Cell().Element(IndexColumnHeader).AlignRight().Text("Completed");
                header.Cell().Element(IndexColumnHeader).AlignRight().Text("Page");
            });

            foreach (var project in category.Projects)
            {
                var anchor = ProjectAnchorId(project.ProjectId);

                table.Cell().Element(IndexBodyCell)
                    .SectionLink(anchor)
                    .Text(project.ProjectName);

                table.Cell().Element(IndexBodyCell)
                    .AlignRight()
                    .Text(project.CompletionYearDisplay);

                table.Cell().Element(IndexBodyCell)
                    .AlignRight()
                    .Text(text => text.BeginPageNumberOfSection(anchor));
            }
        });
    }

    private static void ComposeMissingPhoto(IContainer container)
    {
        container.Border(1)
            .BorderColor(Slate200)
            .Background(Slate50)
            .AlignCenter()
            .AlignMiddle()
            .Column(column =>
            {
                column.Spacing(5);
                column.Item().AlignCenter().Text("PHOTO")
                    .FontSize(9)
                    .SemiBold()
                    .LetterSpacing(1.2f)
                    .FontColor("#94A3B8");
                column.Item().AlignCenter().Text("Photograph not available")
                    .FontSize(9)
                    .FontColor(Slate500);
                column.Item().AlignCenter().Text("Add or mark a project cover photograph in PRISM")
                    .FontSize(7.5f)
                    .FontColor("#94A3B8");
            });
    }

    private static void ComposeKeyValue(IContainer container, string key, string value)
    {
        container.Row(row =>
        {
            row.ConstantItem(148).Text(key).FontSize(9.5f).FontColor(Slate500);
            row.RelativeItem().Text(value).FontSize(9.5f).FontColor(Slate900).SemiBold();
        });
    }

    private static void ComposeFooter(
        IContainer container,
        string issuer,
        string? marking,
        bool includePageNumber,
        byte[]? logo)
    {
        container.Background(Slate50)
            .PaddingVertical(7)
            .PaddingHorizontal(35)
            .Row(row =>
            {
                row.RelativeItem().Row(left =>
                {
                    if (logo is { Length: > 0 })
                    {
                        left.ConstantItem(21).Height(21).AlignMiddle().Image(logo).FitArea();
                        left.ConstantItem(7);
                    }

                    left.RelativeItem().AlignMiddle().Text(issuer)
                        .FontSize(8.5f)
                        .SemiBold()
                        .FontColor(Slate600);
                });

                if (!string.IsNullOrWhiteSpace(marking))
                {
                    row.AutoItem().PaddingHorizontal(10).AlignMiddle().Text(marking)
                        .FontSize(7.5f)
                        .SemiBold()
                        .FontColor("#9A6B00");
                }

                row.RelativeItem().AlignRight().AlignMiddle().Text(text =>
                {
                    text.DefaultTextStyle(BaseStyle(TextStyle.Default).FontSize(8.5f).FontColor(Slate500));
                    text.Span("PRISM ERP");
                    if (includePageNumber)
                    {
                        text.Span(" · Page ");
                        text.CurrentPageNumber().SemiBold();
                        text.Span(" / ");
                        text.TotalPages().SemiBold();
                    }
                });
            });
    }

    private static IContainer CategoryHeaderCell(IContainer container)
        => container.Background("#EDF3FF")
            .BorderBottom(1)
            .BorderColor("#B9CBEF")
            .PaddingHorizontal(8)
            .PaddingVertical(7)
            .DefaultTextStyle(style => BaseStyle(style).FontSize(11.5f).SemiBold().FontColor(Blue));

    private static IContainer IndexColumnHeader(IContainer container)
        => container.Background(Slate50)
            .BorderBottom(1)
            .BorderColor(Slate300)
            .PaddingHorizontal(8)
            .PaddingVertical(5)
            .DefaultTextStyle(style => BaseStyle(style).FontSize(8.5f).SemiBold().FontColor(Slate600));

    private static IContainer IndexBodyCell(IContainer container)
        => container.BorderBottom(1)
            .BorderColor(Slate200)
            .PaddingHorizontal(8)
            .PaddingVertical(5)
            .DefaultTextStyle(style => BaseStyle(style).FontSize(9.25f).FontColor(Slate900));

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
            _logger.LogWarning(
                exception,
                "Unable to load compendium PDF asset {RelativeAssetPath}.",
                relativeUnderWwwRoot);
            return null;
        }
    }

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

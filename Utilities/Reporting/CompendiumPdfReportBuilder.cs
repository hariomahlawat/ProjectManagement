using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using QuestPDF.Drawing;
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
    string UnitDisplayName,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<CompendiumPdfCategorySection> Categories);

public sealed record CompendiumPdfCategorySection(
    string CategoryName,
    IReadOnlyList<CompendiumPdfProjectSection> Projects);

public sealed record CompendiumPdfProjectSection(
    int ProjectId,
    string ProjectName,
    string CategoryName,
    string CompletionYearDisplay,
    string ArmServiceDisplay,
    string ProliferationCostDisplay,
    string DescriptionMarkdown,
    byte[]? CoverPhoto);

public sealed class CompendiumPdfReportBuilder : ICompendiumPdfReportBuilder
{
    static CompendiumPdfReportBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly IWebHostEnvironment _env;

    public CompendiumPdfReportBuilder(IWebHostEnvironment env)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
    }

    public byte[] Build(CompendiumPdfReportContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());
        var generatedAtText = generatedAtIst.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);
        var unitText = string.IsNullOrWhiteSpace(context.UnitDisplayName) ? string.Empty : context.UnitDisplayName.Trim();
        var titleText = string.IsNullOrWhiteSpace(context.Title) ? "Proliferation Compendium" : context.Title.Trim();

        // SECTION: Resolve footer logo bytes using the same asset path as Project Pulse.
        var footerLogoBytes = TryLoadFooterLogoBytes(_env, "img/logos/sdd.png");
        // SECTION: Cover page logos (institutional): crest (top nav) + SDD mark (same as Project Pulse).
        var coverCrestBytes = TryLoadFooterLogoBytes(_env, "img/logos/artrac.png");
        var coverSddBytes = TryLoadFooterLogoBytes(_env, "img/logos/sdd.png");

        var document = Document.Create(container =>
        {
            // SECTION: Cover page
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                // SECTION: Use margin=0 so the background can be full-bleed.
                page.Margin(0);
                page.PageColor("#FFFFFF");

                page.Content().Layers(layers =>
                {
                    // SECTION: Full-bleed institutional cover background.
                    layers.Layer().Element(bg =>
                    {
                        bg.Background("#0B1220");

                        // SECTION: Subtle diagonal band.
                        bg.AlignTopLeft().TranslateX(-120).TranslateY(160).Rotate(-10).Element(band =>
                        {
                            band.Width(page.Size().Width + 300)
                                .Height(120)
                                .Background("#111C33")
                                .Opacity(0.9f);
                        });

                        // SECTION: Watermark (very subtle): use SDD mark.
                        if (coverSddBytes is not null && coverSddBytes.Length > 0)
                        {
                            bg.AlignCenter().Opacity(0.08f).Element(wm =>
                            {
                                wm.Width(420).Height(420).AlignCenter().AlignMiddle()
                                  .Image(coverSddBytes).FitArea();
                            });
                        }
                    });

                    // SECTION: Cover foreground content.
                    layers.PrimaryLayer().PaddingHorizontal(70).PaddingVertical(70).Column(col =>
                    {
                        col.Spacing(16);

                        // SECTION: Top institutional lockup.
                        col.Item().Row(r =>
                        {
                            // SECTION: Crest (top nav image).
                            if (coverCrestBytes is not null && coverCrestBytes.Length > 0)
                            {
                                r.ConstantItem(48).Height(48).AlignMiddle()
                                 .Image(coverCrestBytes).FitArea();
                                r.ConstantItem(12);
                            }

                            r.RelativeItem().AlignMiddle().Text(t =>
                            {
                                t.DefaultTextStyle(s => s.FontSize(12).FontColor("#CBD5E1").SemiBold());
                                t.Span(string.IsNullOrWhiteSpace(unitText) ? "PRISM ERP" : unitText);
                            });

                            // SECTION: Secondary SDD mark (right aligned).
                            if (coverSddBytes is not null && coverSddBytes.Length > 0)
                            {
                                r.ConstantItem(12);
                                r.ConstantItem(34).Height(34).AlignMiddle().AlignRight()
                                 .Image(coverSddBytes).FitArea();
                            }
                        });

                        // SECTION: Cover title block.
                        col.Item().PaddingTop(90).Text(titleText)
                            .FontSize(40)
                            .SemiBold()
                            .FontColor("#FFFFFF");

                        col.Item().Text($"Generated on {generatedAtText}")
                            .FontSize(12)
                            .FontColor("#94A3B8");

                        col.Item().PaddingTop(18).Element(e => e.Height(2).Background("#1F2A44"));

                        // SECTION: Bottom classification tag.
                        col.Item().PaddingTop(240).Text("OFFICIAL USE")
                            .FontSize(10)
                            .LetterSpacing(2)
                            .FontColor("#94A3B8");
                    });
                });
            });

            // SECTION: Index pages
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                // SECTION: Keep content grid margins while allowing footer to reach the true page bottom.
                page.MarginTop(35);
                page.MarginLeft(35);
                page.MarginRight(35);
                page.MarginBottom(0);
                page.PageColor("#FFFFFF");
                page.DefaultTextStyle(x => x.FontSize(11).FontColor("#1F2933"));

                page.Header().Column(header =>
                {
                    header.Item().Text("Index")
                        .FontSize(20)
                        .SemiBold()
                        .FontColor("#0F172A");

                    header.Item().Text($"{titleText}{(string.IsNullOrWhiteSpace(unitText) ? string.Empty : " · " + unitText)}")
                        .FontSize(10)
                        .FontColor("#64748B");
                });

                page.Content().PaddingTop(15).Column(content =>
                {
                    if (context.Categories.Count == 0)
                    {
                        content.Item().PaddingTop(50).AlignCenter().Text("No eligible projects found.")
                            .FontSize(14)
                            .FontColor("#64748B");
                        return;
                    }

                    content.Spacing(18);

                    foreach (var cat in context.Categories)
                    {
                        var catCopy = cat;
                        content.Item().Element(c => ComposeIndexCategory(c, catCopy));
                    }
                });

                // SECTION: Index footer (full-bleed, no page numbers).
                page.Footer().Element(f =>
                {
                    ComposeIndexFooter(
                        f.PaddingLeft(-35).PaddingRight(-35),
                        footerLogoBytes);
                });
            });

            // SECTION: Detail pages
            foreach (var cat in context.Categories)
            {
                foreach (var proj in cat.Projects)
                {
                    var projCopy = proj;
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        // SECTION: Keep content grid margins while allowing footer to reach the true page bottom.
                        page.MarginTop(35);
                        page.MarginLeft(35);
                        page.MarginRight(35);
                        page.MarginBottom(0);
                        page.PageColor("#FFFFFF");
                        page.DefaultTextStyle(x => x.FontSize(11).FontColor("#1F2933"));

                        // SECTION: No header on detail pages (URD refinement).

                        page.Content().PaddingTop(14).Element(c => ComposeProjectDetail(c, projCopy));

                        page.Footer().Element(f =>
                        {
                            // SECTION: Full-bleed footer by negating detail page horizontal margin.
                            ComposeProjectFooter(
                                f
                                    .PaddingLeft(-35)
                                    .PaddingRight(-35),
                                footerLogoBytes);
                        });
                    });
                }
            }
        });

        return document.GeneratePdf();
    }

    // SECTION: Stable named destinations used for in-document navigation.
    private static string ProjectAnchorId(int projectId) => $"proj-{projectId.ToString(CultureInfo.InvariantCulture)}";

    private static byte[]? TryLoadFooterLogoBytes(IWebHostEnvironment env, string relativeUnderWwwroot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(relativeUnderWwwroot))
            {
                return null;
            }

            var relativePath = relativeUnderWwwroot.Trim().Replace('\\', '/');
            var fullPath = Path.Combine(env.WebRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
        }
        catch
        {
            // SECTION: Never block PDF generation when asset lookup fails.
            return null;
        }
    }

    private static void ComposeProjectFooter(
        IContainer container,
        byte[]? logoBytes)
    {
        container
            .Background("#F8FAFC")
            .PaddingVertical(8)
            .PaddingHorizontal(35)
            .Row(row =>
            {
                // SECTION: Left footer content (logo and organization label).
                row.RelativeItem().Row(left =>
                {
                    if (logoBytes is not null && logoBytes.Length > 0)
                    {
                        left.ConstantItem(22).Height(22).AlignMiddle().Element(logo => logo.Image(logoBytes).FitArea());
                        left.ConstantItem(8);
                    }

                    left.RelativeItem().AlignMiddle().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(9).FontColor("#475569").SemiBold());
                        t.Span("Simulator Development Division");
                    });
                });

                // SECTION: Right footer content (product label and page number).
                row.RelativeItem().AlignRight().AlignMiddle().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(9).FontColor("#64748B"));
                    t.Span("PRISM ERP · Page ");
                    t.CurrentPageNumber().SemiBold();
                    t.Span(" / ");
                    t.TotalPages().SemiBold();
                });
            });
    }

    private static void ComposeIndexFooter(
        IContainer container,
        byte[]? logoBytes)
    {
        container
            .Background("#F8FAFC")
            .PaddingVertical(8)
            .PaddingHorizontal(35)
            .Row(row =>
            {
                // SECTION: Left footer content (logo and organization label).
                row.RelativeItem().Row(left =>
                {
                    if (logoBytes is not null && logoBytes.Length > 0)
                    {
                        left.ConstantItem(22).Height(22).AlignMiddle().Element(logo => logo.Image(logoBytes).FitArea());
                        left.ConstantItem(8);
                    }

                    left.RelativeItem().AlignMiddle().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(9).FontColor("#475569").SemiBold());
                        t.Span("Simulator Development Division");
                    });
                });

                // SECTION: Right footer content (product label only).
                row.RelativeItem().AlignRight().AlignMiddle().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(9).FontColor("#64748B"));
                    t.Span("PRISM ERP");
                });
            });
    }

    private static void ComposeIndexCategory(IContainer container, CompendiumPdfCategorySection category)
    {
        container.Column(col =>
        {
            col.Spacing(10);

            col.Item().Text(category.CategoryName)
                .FontSize(14)
                .SemiBold()
                .FontColor("#1E3A8A");

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(4);
                    columns.ConstantColumn(90);
                });

                table.Header(header =>
                {
                    header.Cell().Element(CellHeader).Text("Project");
                    header.Cell().Element(CellHeader).AlignRight().Text("Completed");
                });

                foreach (var p in category.Projects)
                {
                    table.Cell().Element(CellBody)
                        .SectionLink(ProjectAnchorId(p.ProjectId))
                        .Text(p.ProjectName);
                    table.Cell().Element(CellBody).AlignRight().Text(p.CompletionYearDisplay);
                }
            });
        });
    }

    private static void ComposeProjectDetail(IContainer container, CompendiumPdfProjectSection project)
    {
        container.Column(col =>
        {
            col.Spacing(12);

            col.Item().Section(ProjectAnchorId(project.ProjectId))
                .AlignCenter().Text(project.ProjectName)
                .FontSize(20)
                .SemiBold()
                .FontColor("#0F172A");

            // SECTION: Header two-column layout (left metadata, right cover photo).
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(meta =>
                {
                    meta.Spacing(6);
                    meta.Item().Element(c => ComposeKeyValue(c, "Technical category", project.CategoryName));
                    meta.Item().Element(c => ComposeKeyValue(c, "Year of completion", project.CompletionYearDisplay));
                    meta.Item().Element(c => ComposeKeyValue(c, "Arm/Service", project.ArmServiceDisplay));
                    meta.Item().Element(c => ComposeKeyValue(c, "Proliferation cost (lakhs)", project.ProliferationCostDisplay));
                });

                if (project.CoverPhoto is not null && project.CoverPhoto.Length > 0)
                {
                    row.ConstantItem(230).Height(170).PaddingLeft(14).Element(img =>
                    {
                        img.Border(1)
                            .BorderColor("#E2E8F0")
                            .Background("#F8FAFC")
                            .Padding(8)
                            .AlignCenter()
                            .AlignMiddle()
                            .Image(project.CoverPhoto)
                            .FitArea();
                    });
                }
            });

            // SECTION: Description (full width with markdown formatting)
            col.Item().PaddingTop(4).Element(box =>
            {
                box.Border(1)
                    .BorderColor("#E2E8F0")
                    .Background("#FFFFFF")
                    .Padding(10)
                    .Column(desc =>
                    {
                        desc.Spacing(6);
                        desc.Item().Text("Project description")
                            .FontSize(11)
                            .SemiBold()
                            .FontColor("#334155");

                        desc.Item().Element(md => MarkdownPdfRenderer.Render(md, project.DescriptionMarkdown));
                    });
            });
        });
    }

    private static void ComposeKeyValue(IContainer container, string key, string value)
    {
        container.Row(row =>
        {
            row.ConstantItem(160).Text(key).FontSize(10).FontColor("#64748B");
            row.RelativeItem().Text(value).FontSize(10).FontColor("#0F172A").SemiBold();
        });
    }

    private static IContainer CellHeader(IContainer container)
        => container.DefaultTextStyle(x => x.SemiBold().FontColor("#334155").FontSize(10))
            .PaddingVertical(6)
            .PaddingHorizontal(8)
            .BorderBottom(1)
            .BorderColor("#CBD5E1");

    private static IContainer CellBody(IContainer container)
        => container.DefaultTextStyle(x => x.FontSize(10).FontColor("#0F172A"))
            .PaddingVertical(5)
            .PaddingHorizontal(8)
            .BorderBottom(1)
            .BorderColor("#E2E8F0");
}

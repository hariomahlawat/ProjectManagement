using System;
using System.Collections.Generic;
using System.Globalization;
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
    string SponsoringLineDirectorateDisplay,
    string ArmServiceDisplay,
    string ProliferationCostDisplay,
    byte[]? CoverPhoto);

public sealed class CompendiumPdfReportBuilder : ICompendiumPdfReportBuilder
{
    static CompendiumPdfReportBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
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

        var document = Document.Create(container =>
        {
            // SECTION: Cover page
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.PageColor("#FFFFFF");

                page.Content().AlignCenter().Column(col =>
                {
                    col.Spacing(18);

                    col.Item().PaddingTop(120).AlignCenter().Text(titleText)
                        .FontSize(34)
                        .SemiBold()
                        .FontColor("#0F172A");

                    if (!string.IsNullOrWhiteSpace(unitText))
                    {
                        col.Item().AlignCenter().Text(unitText)
                            .FontSize(14)
                            .FontColor("#334155");
                    }

                    col.Item().AlignCenter().Text($"Generated on {generatedAtText}")
                        .FontSize(12)
                        .FontColor("#64748B");

                    col.Item().PaddingTop(40).AlignCenter().Element(e =>
                    {
                        e.Height(2).Background("#E2E8F0");
                    });

                    col.Item().PaddingTop(16).AlignCenter().Text("Official Use")
                        .FontSize(10)
                        .LetterSpacing(1)
                        .FontColor("#94A3B8");
                });
            });

            // SECTION: Index pages
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(35);
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

                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(9).FontColor("#94A3B8"));
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
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
                        page.Margin(35);
                        page.PageColor("#FFFFFF");
                        page.DefaultTextStyle(x => x.FontSize(11).FontColor("#1F2933"));

                        page.Header().Column(header =>
                        {
                            header.Item().Text(titleText)
                                .FontSize(14)
                                .SemiBold()
                                .FontColor("#0F172A");
                            header.Item().Text(projCopy.CategoryName)
                                .FontSize(10)
                                .FontColor("#64748B");
                        });

                        page.Content().PaddingTop(14).Element(c => ComposeProjectDetail(c, projCopy));

                        page.Footer().AlignCenter().Text(text =>
                        {
                            text.DefaultTextStyle(style => style.FontSize(9).FontColor("#94A3B8"));
                            text.CurrentPageNumber();
                            text.Span(" / ");
                            text.TotalPages();
                        });
                    });
                }
            }
        });

        return document.GeneratePdf();
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
                    table.Cell().Element(CellBody).Text(p.ProjectName);
                    table.Cell().Element(CellBody).AlignRight().Text(p.CompletionYearDisplay);
                }
            });
        });
    }

    private static void ComposeProjectDetail(IContainer container, CompendiumPdfProjectSection project)
    {
        container.Column(col =>
        {
            col.Spacing(14);

            col.Item().Text(project.ProjectName)
                .FontSize(20)
                .SemiBold()
                .FontColor("#0F172A");

            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Spacing(8);
                    left.Item().Element(c => ComposeKeyValue(c, "Technical category", project.CategoryName));
                    left.Item().Element(c => ComposeKeyValue(c, "Year of completion", project.CompletionYearDisplay));
                    left.Item().Element(c => ComposeKeyValue(c, "Sponsoring line directorate", project.SponsoringLineDirectorateDisplay));
                    left.Item().Element(c => ComposeKeyValue(c, "Arm/Service", project.ArmServiceDisplay));
                    left.Item().Element(c => ComposeKeyValue(c, "Proliferation cost (lakhs)", project.ProliferationCostDisplay));
                });

                if (project.CoverPhoto is not null && project.CoverPhoto.Length > 0)
                {
                    row.ConstantItem(210).Height(160).PaddingLeft(14).Element(img =>
                    {
                        // SECTION: Project cover image
                        img.Border(1)
                            .BorderColor("#CBD5F5")
                            .Background("#F8FAFC")
                            .Padding(6)
                            .Image(project.CoverPhoto)
                            .FitArea();
                    });
                }
            });

            col.Item().PaddingTop(4).Element(e => e.Height(1).Background("#E2E8F0"));

            col.Item().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(9).FontColor("#64748B"));
                text.Span("Project ID: ");
                text.Span(project.ProjectId.ToString(CultureInfo.InvariantCulture)).SemiBold();
            });
        });
    }

    private static void ComposeKeyValue(IContainer container, string key, string value)
    {
        container.Row(row =>
        {
            row.ConstantItem(170).Text(key).FontSize(10).FontColor("#475569");
            row.RelativeItem().Text(value).FontSize(10).FontColor("#0F172A");
        });
    }

    private static IContainer CellHeader(IContainer container)
        => container.DefaultTextStyle(x => x.SemiBold().FontColor("#334155").FontSize(10))
            .Background("#F1F5F9")
            .PaddingVertical(6)
            .PaddingHorizontal(8)
            .Border(1)
            .BorderColor("#E2E8F0");

    private static IContainer CellBody(IContainer container)
        => container.DefaultTextStyle(x => x.FontSize(10).FontColor("#0F172A"))
            .PaddingVertical(5)
            .PaddingHorizontal(8)
            .BorderBottom(1)
            .BorderColor("#E2E8F0");
}

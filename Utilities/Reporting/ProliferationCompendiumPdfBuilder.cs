using ProjectManagement.Areas.Compendiums.Application.Dto;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace ProjectManagement.Utilities.Reporting;

public interface IProliferationCompendiumPdfBuilder
{
    byte[] Build(ProliferationCompendiumPdfContext context);
}

public sealed record ProliferationCompendiumPdfContext(
    IReadOnlyList<CompendiumProjectDetailDto> Projects,
    DateOnly GeneratedOn,
    byte[]? FooterLogoBytes);

// SECTION: Proliferation compendium A4 PDF builder
public sealed class ProliferationCompendiumPdfBuilder : IProliferationCompendiumPdfBuilder
{
    static ProliferationCompendiumPdfBuilder() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Build(ProliferationCompendiumPdfContext context)
    {
        // SECTION: Single-pass rendering for deterministic offline generation
        return CreateDocument(context).GeneratePdf();
    }

    // SECTION: Shared document composition
    private static Document CreateDocument(ProliferationCompendiumPdfContext context)
    {
        return Document.Create(container =>
        {
            // SECTION: Cover page
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.Content().PaddingVertical(70).Column(column =>
                {
                    column.Spacing(16);
                    column.Item().Text("Proliferation Compendium").FontSize(30).SemiBold().FontColor("#0F172A");
                    column.Item().Text("Projects available for proliferation (Completed)").FontSize(14).FontColor("#475569");
                    column.Item().Text($"Generated on: {context.GeneratedOn:dd MMM yyyy}").FontSize(11).FontColor("#1E3A8A");
                });
                page.Footer().Element(c => PrismPdfChrome.ComposeFooter(c, context.FooterLogoBytes, context.GeneratedOn));
            });

            // SECTION: Index page
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.Header().Text("Index").FontSize(20).SemiBold();
                page.Content().PaddingTop(12).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(30);
                        cols.RelativeColumn(3);
                        cols.RelativeColumn(2);
                        cols.ConstantColumn(80);
                        cols.ConstantColumn(60);
                    });

                    table.Header(h =>
                    {
                        h.Cell().Text("Ser").SemiBold();
                        h.Cell().Text("Project name").SemiBold();
                        h.Cell().Text("Sponsoring dte").SemiBold();
                        h.Cell().Text("Completion year").SemiBold();
                        h.Cell().AlignRight().Text("Page").SemiBold();
                    });

                    for (var index = 0; index < context.Projects.Count; index++)
                    {
                        var project = context.Projects[index];
                        table.Cell().Text((index + 1).ToString(CultureInfo.InvariantCulture));
                        table.Cell().Text(project.Name);
                        table.Cell().Text(project.SponsoringLineDirectorateName);
                        table.Cell().Text(project.CompletionYearText);

                        table.Cell().AlignRight().Text("-");
                    }
                });
                page.Footer().Element(c => PrismPdfChrome.ComposeFooter(c, context.FooterLogoBytes, context.GeneratedOn));
            });

            // SECTION: Project sections
            foreach (var project in context.Projects)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(35);
                    page.Content().Column(column =>
                    {
                        column.Item().Element(c => ComposeProject(c, project));
                    });
                    page.Footer().Element(c => PrismPdfChrome.ComposeFooter(c, context.FooterLogoBytes, context.GeneratedOn));
                });
            }

            // SECTION: Back cover
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.Content().AlignMiddle().AlignCenter().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text("PRISM ERP").FontSize(24).SemiBold();
                    col.Item().Text("Systems Development Directorate").FontSize(12).FontColor("#475569");
                    col.Item().Text($"Generated on: {context.GeneratedOn:dd MMM yyyy}").FontSize(10).FontColor("#64748B");
                });
                page.Footer().Element(c => PrismPdfChrome.ComposeFooter(c, context.FooterLogoBytes, context.GeneratedOn));
            });
        });
    }

    private static void ComposeProject(IContainer container, CompendiumProjectDetailDto project)
    {
        container.Column(column =>
        {
            column.Spacing(12);
            column.Item().Border(1).BorderColor("#CBD5E1").Padding(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(project.Name).FontSize(18).SemiBold();
                    left.Item().Text($"Sponsoring Dte: {project.SponsoringLineDirectorateName}").FontSize(10).FontColor("#475569");
                });
                row.ConstantItem(220).Column(right =>
                {
                    right.Item().Text($"Completion year: {project.CompletionYearText}").FontSize(10);
                    right.Item().Text($"Arms / Services: {project.ArmService}").FontSize(10);
                    right.Item().Text($"Proliferation cost: {FormatCost(project.ProliferationCostLakhs)}").FontSize(10);
                });
            });

            column.Item().Height(210).Border(1).BorderColor("#CBD5E1").AlignCenter().AlignMiddle().Element(c =>
            {
                if (project.CoverPhotoAvailable && project.CoverPhotoBytes is not null)
                {
                    c.Image(project.CoverPhotoBytes).FitArea();
                    return;
                }

                c.Text("No image available").FontSize(12).FontColor("#64748B");
            });

            column.Item().Text("Project description").SemiBold().FontSize(12).FontColor("#1E3A8A");
            column.Item().Text(project.Description);

            column.Item().Border(1).BorderColor("#E2E8F0").Padding(10).Column(cost =>
            {
                cost.Item().Text($"Proliferation cost: {FormatCost(project.ProliferationCostLakhs)}");
                cost.Item().PaddingTop(6).Text("Note: Software will be provided free of cost by SDD.").Italic().FontColor("#334155");
            });
        });
    }

    // SECTION: Cost formatter to avoid invalid unit suffixes on null values
    private static string FormatCost(decimal? value)
        => value.HasValue
            ? $"{value.Value.ToString("0.##", CultureInfo.InvariantCulture)} lakh INR"
            : "Not recorded";
}

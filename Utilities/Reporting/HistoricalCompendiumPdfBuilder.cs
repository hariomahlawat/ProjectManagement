using ProjectManagement.Areas.Compendiums.Application.Dto;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace ProjectManagement.Utilities.Reporting;

public interface IHistoricalCompendiumPdfBuilder
{
    byte[] Build(HistoricalCompendiumPdfContext context);
}

public sealed record HistoricalCompendiumPdfContext(
    IReadOnlyList<CompendiumProjectDetailDto> Projects,
    DateOnly GeneratedOn,
    byte[]? FooterLogoBytes);

// SECTION: Historical repository A4 PDF builder
public sealed class HistoricalCompendiumPdfBuilder : IHistoricalCompendiumPdfBuilder
{
    static HistoricalCompendiumPdfBuilder() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Build(HistoricalCompendiumPdfContext context)
    {
        var indexEntries = BuildIndexEntries(context.Projects);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.Content().PaddingVertical(70).Column(column =>
                {
                    column.Spacing(16);
                    column.Item().Text("Historical Repository Compendium").FontSize(30).SemiBold().FontColor("#0F172A");
                    column.Item().Text("Projects available for proliferation (Completed)").FontSize(14).FontColor("#475569");
                    column.Item().Text($"Generated on: {context.GeneratedOn:dd MMM yyyy}").FontSize(11).FontColor("#1E3A8A");
                });
                page.Footer().Element(c => PrismPdfChrome.ComposeFooter(c, context.FooterLogoBytes, context.GeneratedOn));
            });

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

                    foreach (var entry in indexEntries)
                    {
                        table.Cell().Text(entry.Serial.ToString(CultureInfo.InvariantCulture));
                        table.Cell().Text(entry.Project.Name);
                        table.Cell().Text(entry.Project.SponsoringLineDirectorateName);
                        table.Cell().Text(entry.Project.CompletionYearText);
                        table.Cell().AlignRight().Text(entry.StartPage.ToString(CultureInfo.InvariantCulture));
                    }
                });
                page.Footer().Element(c => PrismPdfChrome.ComposeFooter(c, context.FooterLogoBytes, context.GeneratedOn));
            });

            foreach (var project in context.Projects)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(35);
                    page.Content().Element(c => ComposeProject(c, project));
                    page.Footer().Element(c => PrismPdfChrome.ComposeFooter(c, context.FooterLogoBytes, context.GeneratedOn));
                });
            }

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

        return document.GeneratePdf();
    }

    private static void ComposeProject(IContainer container, CompendiumProjectDetailDto project)
    {
        var historical = project.HistoricalExtras;

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
                    right.Item().Text($"Proliferation cost: {(project.ProliferationCostLakhs.HasValue ? project.ProliferationCostLakhs.Value.ToString("0.##", CultureInfo.InvariantCulture) + " lakh INR" : "Not recorded")}").FontSize(10);
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

            if (historical is not null)
            {
                column.Item().Background("#F1F5F9").Border(1).BorderColor("#CBD5E1").Padding(10).Column(hist =>
                {
                    hist.Spacing(4);
                    hist.Item().Text("Historical record").SemiBold().FontColor("#0F172A");
                    hist.Item().Text($"R&D cost (lakh INR): {(historical.RdCostLakhs.HasValue ? historical.RdCostLakhs.Value.ToString("0.##", CultureInfo.InvariantCulture) : "Not recorded")}");
                    hist.Item().Text($"ToT status: {historical.TotStatusText}");
                    hist.Item().Text($"Completed on: {historical.TotCompletedOnText}");
                    hist.Item().Text($"Proliferation counts (all time): Total {historical.ProliferationTotalAllTime}, SDD {historical.ProliferationSddAllTime}, 515 ABW {historical.ProliferationAbw515AllTime}");
                });
            }

            column.Item().Border(1).BorderColor("#E2E8F0").Padding(10).Column(cost =>
            {
                cost.Item().Text($"Proliferation cost (lakh INR): {(project.ProliferationCostLakhs.HasValue ? project.ProliferationCostLakhs.Value.ToString("0.##", CultureInfo.InvariantCulture) : "Not recorded")}");
                cost.Item().PaddingTop(6).Text("Note: Software will be provided free of cost by SDD.").Italic().FontColor("#334155");
            });
        });
    }

    private static List<IndexEntry> BuildIndexEntries(IReadOnlyList<CompendiumProjectDetailDto> projects)
    {
        var entries = new List<IndexEntry>(projects.Count);
        var page = 3;

        for (var i = 0; i < projects.Count; i++)
        {
            var project = projects[i];
            entries.Add(new IndexEntry(i + 1, project, page));
            page += Math.Max(1, 1 + ((project.Description?.Length ?? 0) / 2600));
        }

        return entries;
    }

    private sealed record IndexEntry(int Serial, CompendiumProjectDetailDto Project, int StartPage);
}

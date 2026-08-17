using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProjectManagement.Services.Reports.ArppFyProjectUpdate;

public sealed class ArppFyProjectUpdatePdfBuilder
{
    private const string Ink = "#1F2937";
    private const string Muted = "#526174";
    private const string Navy = "#17365D";
    private const string HeaderFill = "#E8EEF5";
    private const string Border = "#8A99A8";

    static ArppFyProjectUpdatePdfBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Build(ArppFyProjectUpdateReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(22);
                // QuestPDF 2024.x enables standard ligatures in its bundled Lato font.
                // Disable them for reliable PDF search, copy/paste and text extraction.
                page.DefaultTextStyle(style => style
                    .FontSize(7.2f)
                    .FontColor(Ink)
                    .DisableFontFeature(FontFeatures.StandardLigatures));

                page.Header().PaddingBottom(8).AlignCenter().Text(report.FormalTitle)
                    .FontSize(11.5f)
                    .Bold()
                    .FontColor(Navy);

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(0.55f);
                        columns.RelativeColumn(0.85f);
                        columns.RelativeColumn(2.7f);
                        columns.RelativeColumn(1.2f);
                        columns.RelativeColumn(0.9f);
                        columns.RelativeColumn(0.95f);
                        columns.RelativeColumn(0.65f);
                        columns.RelativeColumn(1.0f);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(1.0f);
                        columns.RelativeColumn(0.8f);
                        columns.RelativeColumn(4.05f);
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header.Cell().RowSpan(2), "Ser No.");
                        HeaderCell(header.Cell().RowSpan(2), "ARPP No.");
                        HeaderCell(header.Cell().RowSpan(2), "Name of Project");
                        HeaderCell(header.Cell().RowSpan(2), "Dt of Grant of IPA / ARPP Listing");
                        HeaderCell(header.Cell().RowSpan(2), "Sch");
                        HeaderCell(header.Cell().RowSpan(2), "CFA");
                        HeaderCell(header.Cell().RowSpan(2), "Est");
                        HeaderCell(header.Cell().ColumnSpan(3), "Status");
                        HeaderCell(header.Cell().RowSpan(2), "Proj Case");
                        HeaderCell(header.Cell().RowSpan(2), "Remarks");

                        HeaderCell(header.Cell(), "AoN");
                        HeaderCell(header.Cell(), "SO amt & dt");
                        HeaderCell(header.Cell(), "PDC dt");
                    });

                    foreach (var row in report.Rows)
                    {
                        BodyCell(table.Cell(), row.SerialNumber.ToString(CultureInfo.InvariantCulture), center: true);
                        BodyCell(table.Cell(), row.PppNumber ?? string.Empty, center: true);
                        BodyCell(table.Cell(), row.ProjectName, bold: true);
                        BodyCell(table.Cell(), Date(row.FirstArppListingDate), center: true);
                        BodyCell(table.Cell(), row.DfpdsSchedule ?? string.Empty, center: true);
                        BodyCell(table.Cell(), row.Cfa ?? string.Empty, center: true);
                        BodyCell(table.Cell(), row.Establishment, center: true);
                        BodyCell(table.Cell(), Date(row.AonDate), center: true);
                        BodyCell(table.Cell(), SupplyOrder(row), center: true);
                        BodyCell(table.Cell(), Date(row.DevelopmentPdcDate), center: true);
                        BodyCell(table.Cell(), row.ProjectCaseDisplay, center: true);
                        BodyCell(table.Cell(), row.LatestExternalRemark ?? string.Empty);
                    }
                });

                page.Footer().PaddingTop(5).Row(footer =>
                {
                    footer.RelativeItem().Text("PRISM ERP · Simulator Development Division")
                        .FontSize(6.5f)
                        .FontColor(Muted);
                    footer.ConstantItem(100).AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(6.5f).FontColor(Muted));
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            });
        })
        .WithMetadata(new DocumentMetadata
        {
            Title = report.FormalTitle,
            Author = "Simulator Development Division",
            Subject = "ARPP listed projects and current update",
            Creator = "PRISM ERP",
            Producer = "PRISM ERP",
            CreationDate = report.GeneratedAtUtc,
            ModifiedDate = report.GeneratedAtUtc
        });

        return document.GeneratePdf();
    }

    private static void HeaderCell(IContainer container, string text)
        => container
            .Border(0.6f)
            .BorderColor(Border)
            .Background(HeaderFill)
            .PaddingVertical(4)
            .PaddingHorizontal(3)
            .AlignMiddle()
            .AlignCenter()
            .Text(text)
            .FontSize(6.8f)
            .Bold()
            .FontColor(Navy);

    private static void BodyCell(IContainer container, string text, bool center = false, bool bold = false)
    {
        var cell = container
            .Border(0.45f)
            .BorderColor(Border)
            .PaddingVertical(3.5f)
            .PaddingHorizontal(3)
            .AlignMiddle();

        if (center)
        {
            cell = cell.AlignCenter();
        }

        var content = cell.Text(text).FontSize(6.7f).FontColor(Ink);
        if (bold)
        {
            content.Bold();
        }
    }

    private static string Date(DateOnly? value)
        => value?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string SupplyOrder(ArppFyProjectUpdateRow row)
    {
        var amount = row.SupplyOrderAmountInCrores.HasValue
            ? $"₹{row.SupplyOrderAmountInCrores.Value.ToString("0.##", CultureInfo.InvariantCulture)} Cr"
            : null;
        var date = row.SupplyOrderDate.HasValue ? Date(row.SupplyOrderDate) : null;
        return amount is not null && date is not null
            ? $"{amount}\n{date}"
            : amount ?? date ?? string.Empty;
    }
}

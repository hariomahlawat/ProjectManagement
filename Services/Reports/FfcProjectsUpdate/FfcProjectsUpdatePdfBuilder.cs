using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Services.Reports.FfcProjectsUpdate;

public static class FfcProjectsUpdatePdfBuilder
{
    private const string Ink = "#1F2937";
    private const string Muted = "#526174";
    private const string Navy = "#17365D";
    private const string HeaderFill = "#E8EEF5";
    private const string GroupFill = "#F2F5F8";
    private const string Border = "#8A99A8";

    static FfcProjectsUpdatePdfBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Build(
        FfcProjectsUpdateReport report,
        FfcProjectsUpdatePresentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var resolved = options ?? FfcProjectsUpdatePresentationOptions.Default;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(22);
                page.DefaultTextStyle(style => style
                    .FontSize(7.2f)
                    .FontColor(Ink)
                    .DisableFontFeature(FontFeatures.StandardLigatures));

                page.Header()
                    .PaddingBottom(8)
                    .AlignCenter()
                    .Text(FfcProjectsUpdateReport.FormalTitle)
                    .FontSize(11.5f)
                    .Bold()
                    .FontColor(Navy);

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(0.55f);
                        columns.RelativeColumn(resolved.IncludeOverallStatus ? 2.4f : 2.8f);
                        columns.RelativeColumn(1.05f);
                        columns.RelativeColumn(0.75f);
                        columns.RelativeColumn(1.25f);
                        columns.RelativeColumn(resolved.IncludeOverallStatus ? 3.45f : 6.0f);
                        if (resolved.IncludeOverallStatus)
                        {
                            columns.RelativeColumn(3.55f);
                        }
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header.Cell(), "S. No.", center: true);
                        HeaderCell(header.Cell(), "Project");
                        HeaderCell(header.Cell(), "Cost (₹ lakh)", center: true);
                        HeaderCell(header.Cell(), "Quantity", center: true);
                        HeaderCell(header.Cell(), "Status");
                        HeaderCell(header.Cell(), "Current progress");
                        if (resolved.IncludeOverallStatus)
                        {
                            HeaderCell(header.Cell(), "Overall status");
                        }
                    });

                    var columnCount = resolved.IncludeOverallStatus ? 7 : 6;
                    foreach (var group in report.Groups)
                    {
                        GroupCell(
                            table.Cell().ColumnSpan((uint)columnCount),
                            $"{group.CountryName} – {group.Year}");

                        for (var index = 0; index < group.Rows.Count; index++)
                        {
                            var row = group.Rows[index];

                            BodyCell(table.Cell(), row.Serial.ToString(CultureInfo.InvariantCulture), center: true);
                            BodyCell(table.Cell(), row.ProjectName, bold: true);
                            BodyCell(
                                table.Cell(),
                                row.CostInCr.HasValue
                                    ? (row.CostInCr.Value * 100m).ToString("N2", CultureInfo.InvariantCulture)
                                    : string.Empty,
                                center: true);
                            BodyCell(table.Cell(), row.Quantity.ToString("N0", CultureInfo.InvariantCulture), center: true);
                            BodyCell(table.Cell(), row.Status);
                            BodyCell(table.Cell(), Narrative(row.ProgressText));

                            if (resolved.IncludeOverallStatus)
                            {
                                // Overall status belongs to the country-year record. Rendering
                                // it once avoids repeated long text and is robust when a group
                                // crosses a PDF page boundary.
                                BodyCell(
                                    table.Cell(),
                                    index == 0 ? Narrative(group.OverallRemarks) : string.Empty);
                            }
                        }
                    }
                });

                page.Footer().PaddingTop(5).Row(footer =>
                {
                    footer.RelativeItem()
                        .Text("PRISM ERP · Simulator Development Division")
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
            Title = FfcProjectsUpdateReport.FormalTitle,
            Author = "Simulator Development Division",
            Subject = "FFC country-year project update",
            Creator = "PRISM ERP",
            Producer = "PRISM ERP",
            CreationDate = report.GeneratedAtUtc,
            ModifiedDate = report.GeneratedAtUtc
        });

        return document.GeneratePdf();
    }

    private static void HeaderCell(IContainer container, string text, bool center = false)
    {
        var cell = container
            .Border(0.6f)
            .BorderColor(Border)
            .Background(HeaderFill)
            .PaddingVertical(4)
            .PaddingHorizontal(3)
            .AlignMiddle();

        if (center)
        {
            cell = cell.AlignCenter();
        }

        cell.Text(text)
            .FontSize(6.8f)
            .Bold()
            .FontColor(Navy);
    }

    private static void GroupCell(IContainer container, string label)
        => container
            .Border(0.45f)
            .BorderColor(Border)
            .Background(GroupFill)
            .PaddingVertical(4)
            .PaddingHorizontal(5)
            .Text(label)
            .FontSize(7.2f)
            .Bold()
            .FontColor(Navy);

    private static void BodyCell(
        IContainer container,
        string text,
        bool center = false,
        bool bold = false)
    {
        var cell = container
            .Border(0.45f)
            .BorderColor(Border)
            .PaddingVertical(3.5f)
            .PaddingHorizontal(3)
            .AlignTop();

        if (center)
        {
            cell = cell.AlignCenter();
        }

        var content = cell.Text(text).FontSize(6.8f).FontColor(Ink);
        if (bold)
        {
            content.Bold();
        }
    }

    private static string Narrative(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim()
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);
}

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace ProjectManagement.Utilities.Reporting;

// SECTION: Shared PDF chrome helpers for compendium reports
public static class PrismPdfChrome
{
    public static void ComposeFooter(IContainer container, byte[]? logoBytes, DateOnly generatedOn)
    {
        container.PaddingTop(6).BorderTop(1).BorderColor("#CBD5E1").Row(row =>
        {
            row.ConstantItem(110).AlignLeft().Height(22).Element(c =>
            {
                if (logoBytes is not null && logoBytes.Length > 0)
                {
                    c.Image(logoBytes).FitHeight();
                }
            });

            row.RelativeItem().AlignCenter().AlignMiddle().Text($"Generated on: {generatedOn.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}")
                .FontSize(9)
                .FontColor("#475569");

            row.ConstantItem(130).AlignRight().AlignMiddle().Text(text =>
            {
                text.Span("PRISM ERP  ").FontSize(9).SemiBold().FontColor("#1E293B");
                text.CurrentPageNumber().FontSize(9).FontColor("#475569");
                text.Span(" / ").FontSize(9).FontColor("#475569");
                text.TotalPages().FontSize(9).FontColor("#475569");
            });
        });
    }
}

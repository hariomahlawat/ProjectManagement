using System.Text.Json;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using UglyToad.PdfPig;

namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Runs before web-host construction when --compendium-offline-self-test is supplied.
/// It exercises the exact local font, SkiaSharp, QuestPDF and PdfPig chain without a
/// database, HTTP listener or internet connection.
/// </summary>
public static class CompendiumOfflineSelfTest
{
    public const string CommandLineSwitch = "--compendium-offline-self-test";

    public static int Run()
    {
        try
        {
            var resolution = PublicationFontContract.InspectDmSans();
            if (!resolution.IsAvailable)
            {
                throw PublicationFontContract.CreateMissingFontException(resolution);
            }

            foreach (var file in PublicationFontContract.RequiredDmSansFiles)
            {
                using var stream = File.OpenRead(Path.Combine(resolution.DirectoryPath!, file));
                FontManager.RegisterFontWithCustomName(PublicationFontService.PrimaryFamilyName, stream);
            }

            var regularPath = Path.Combine(resolution.DirectoryPath!, "DMSans-Regular.ttf");
            using var typeface = SKTypeface.FromFile(regularPath)
                                 ?? throw new InvalidOperationException("SkiaSharp could not create the DM Sans test typeface.");
            using var paint = new SKPaint
            {
                Typeface = typeface,
                TextSize = 10f,
                IsAntialias = true
            };
            var measuredWidth = paint.MeasureText("PRISM Compendium offline composition self-test");
            if (!float.IsFinite(measuredWidth) || measuredWidth <= 0f)
            {
                throw new InvalidOperationException("SkiaSharp returned an invalid publication text measurement.");
            }

            QuestPDF.Settings.License = LicenseType.Community;
            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(style => style
                        .FontFamily(PublicationFontService.PrimaryFamilyName)
                        .FontSize(10));
                    page.Content().Column(column =>
                    {
                        column.Spacing(8);
                        column.Item().Text("PRISM Compendium offline self-test").SemiBold().FontSize(16);
                        column.Item().Text(CompendiumBuildIdentity.BuildStamp);
                        column.Item().Text("Local DM Sans, SkiaSharp, QuestPDF and PdfPig are operational.");
                    });
                });
            }).GeneratePdf();

            using var pdfStream = new MemoryStream(pdfBytes, writable: false);
            using var pdf = PdfDocument.Open(pdfStream);
            var pageCount = pdf.GetPages().Count();
            if (pageCount != 1)
            {
                throw new InvalidOperationException($"The offline test PDF contains {pageCount} pages; exactly one was expected.");
            }

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "ok",
                build = CompendiumBuildIdentity.BuildStamp,
                pdfContract = CompendiumBuildIdentity.PdfContract,
                fontDirectory = resolution.DirectoryPath,
                measuredWidth,
                pdfBytes = pdfBytes.Length,
                pageCount
            }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                status = "failed",
                build = CompendiumBuildIdentity.BuildStamp,
                error = exception.Message,
                detail = exception.ToString()
            }));
            return 41;
        }
    }
}

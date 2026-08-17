using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ProjectManagement.Services.Reports.ArppFyProjectUpdate;

public sealed class ArppFyProjectUpdateWordBuilder
{
    private const string Font = "Arial";
    private const string Ink = "1F2937";
    private const string Muted = "526174";
    private const string Navy = "17365D";
    private const string HeaderFill = "E8EEF5";
    private const string Border = "8A99A8";
    private const int TableWidth = 16000;

    // Both variants intentionally total 16,000 twips: the exact printable width
    // used by the A4-landscape section below.
    private static readonly int[] StandardWidths =
        [500, 1400, 2550, 1150, 800, 1050, 650, 1100, 1400, 1100, 800, 3500];

    private static readonly int[] PresentStageWidths =
        [500, 1400, 2150, 1150, 750, 1000, 600, 1000, 1500, 1300, 1000, 750, 2900];

    public byte[] Build(
        ArppFyProjectUpdateReport report,
        ArppFyProjectUpdatePresentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var resolvedOptions = options ?? ArppFyProjectUpdatePresentationOptions.Default;
        var widths = GetWidths(resolvedOptions);

        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, autoSave: true))
        {
            document.PackageProperties.Title = report.FormalTitle;
            document.PackageProperties.Subject = "ARPP listed projects and current update";
            document.PackageProperties.Creator = "PRISM ERP";
            document.PackageProperties.LastModifiedBy = "PRISM ERP";
            document.PackageProperties.Created = report.GeneratedAtUtc.UtcDateTime;
            document.PackageProperties.Modified = report.GeneratedAtUtc.UtcDateTime;

            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new W.Document(new W.Body());
            AddStyles(mainPart);
            AddSettings(mainPart);

            var footerPart = mainPart.AddNewPart<FooterPart>();
            footerPart.Footer = BuildFooter();
            footerPart.Footer.Save();

            var body = mainPart.Document.Body!;
            body.Append(Paragraph(
                report.FormalTitle,
                22,
                bold: true,
                color: Navy,
                align: W.JustificationValues.Center,
                after: 90));

            body.Append(BuildTable(report.Rows, resolvedOptions, widths));
            body.Append(BuildSectionProperties(mainPart, footerPart));
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static int[] GetWidths(ArppFyProjectUpdatePresentationOptions options)
    {
        var widths = options.IncludePresentStage ? PresentStageWidths : StandardWidths;
        if (widths.Sum() != TableWidth)
        {
            throw new InvalidOperationException("ARPP report Word column widths must total the A4-landscape printable width.");
        }

        return widths;
    }

    private static W.Table BuildTable(
        IReadOnlyList<ArppFyProjectUpdateRow> rows,
        ArppFyProjectUpdatePresentationOptions options,
        IReadOnlyList<int> widths)
    {
        var table = new W.Table(
            new W.TableProperties(
                new W.TableWidth { Width = TableWidth.ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa },
                new W.TableLayout { Type = W.TableLayoutValues.Fixed },
                new W.TableBorders(
                    new W.TopBorder { Val = W.BorderValues.Single, Color = Border, Size = 5 },
                    new W.LeftBorder { Val = W.BorderValues.Single, Color = Border, Size = 5 },
                    new W.BottomBorder { Val = W.BorderValues.Single, Color = Border, Size = 5 },
                    new W.RightBorder { Val = W.BorderValues.Single, Color = Border, Size = 5 },
                    new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Color = Border, Size = 4 },
                    new W.InsideVerticalBorder { Val = W.BorderValues.Single, Color = Border, Size = 4 })));

        table.Append(new W.TableGrid(widths.Select(width => new W.GridColumn
        {
            Width = width.ToString(CultureInfo.InvariantCulture)
        })));

        table.Append(BuildHeaderRowOne(options, widths));
        table.Append(BuildHeaderRowTwo(options, widths));

        foreach (var row in rows)
        {
            var tableRow = new W.TableRow(new W.TableRowProperties(new W.CantSplit()));
            var cells = BuildDataCells(row, options, widths);
            foreach (var cell in cells)
            {
                tableRow.Append(cell);
            }

            table.Append(tableRow);
        }

        return table;
    }

    private static IReadOnlyList<W.TableCell> BuildDataCells(
        ArppFyProjectUpdateRow row,
        ArppFyProjectUpdatePresentationOptions options,
        IReadOnlyList<int> widths)
    {
        var cells = new List<W.TableCell>
        {
            Cell(row.SerialNumber.ToString(CultureInfo.InvariantCulture), widths[0], align: W.JustificationValues.Center),
            Cell(row.PppNumber ?? string.Empty, widths[1], align: W.JustificationValues.Center),
            Cell(row.ProjectName, widths[2], bold: true),
            Cell(Date(row.FirstArppListingDate), widths[3], align: W.JustificationValues.Center, noWrap: true),
            Cell(row.DfpdsSchedule ?? string.Empty, widths[4], align: W.JustificationValues.Center),
            Cell(row.Cfa ?? string.Empty, widths[5], align: W.JustificationValues.Center),
            Cell(row.Establishment, widths[6], align: W.JustificationValues.Center),
            Cell(Date(row.AonDate), widths[7], align: W.JustificationValues.Center, noWrap: true)
        };

        var offset = 8;
        if (options.IncludePresentStage)
        {
            cells.Add(Cell(row.StageDisplay, widths[offset], align: W.JustificationValues.Center));
            offset++;
        }

        cells.Add(Cell(SupplyOrder(row), widths[offset], align: W.JustificationValues.Center, noWrap: true));
        cells.Add(Cell(Pdc(row), widths[offset + 1], align: W.JustificationValues.Center, noWrap: true));
        cells.Add(Cell(row.ProjectCaseDisplay, widths[offset + 2], align: W.JustificationValues.Center));
        cells.Add(Cell(row.LatestExternalRemark ?? string.Empty, widths[offset + 3]));
        return cells;
    }

    private static W.TableRow BuildHeaderRowOne(
        ArppFyProjectUpdatePresentationOptions options,
        IReadOnlyList<int> widths)
    {
        var row = new W.TableRow(new W.TableRowProperties(new W.TableHeader(), new W.CantSplit()));
        row.Append(
            MergedHeader("Ser No.", widths[0], restart: true),
            MergedHeader("ARPP No.", widths[1], restart: true),
            MergedHeader("Name of Project", widths[2], restart: true),
            MergedHeader("Dt of Grant of IPA / ARPP Listing", widths[3], restart: true),
            MergedHeader("Sch", widths[4], restart: true),
            MergedHeader("CFA", widths[5], restart: true),
            MergedHeader("Est", widths[6], restart: true));

        var statusWidth = widths.Skip(7).Take(options.StatusColumnCount).Sum();
        row.Append(SpanHeader("Status", statusWidth, options.StatusColumnCount));

        var caseIndex = options.IncludePresentStage ? 11 : 10;
        var remarksIndex = caseIndex + 1;
        row.Append(
            MergedHeader("Proj Case", widths[caseIndex], restart: true),
            MergedHeader("Remarks", widths[remarksIndex], restart: true));
        return row;
    }

    private static W.TableRow BuildHeaderRowTwo(
        ArppFyProjectUpdatePresentationOptions options,
        IReadOnlyList<int> widths)
    {
        var row = new W.TableRow(new W.TableRowProperties(new W.TableHeader(), new W.CantSplit()));
        for (var index = 0; index < 7; index++)
        {
            row.Append(MergedHeader(string.Empty, widths[index], restart: false));
        }

        row.Append(Header("AoN", widths[7]));
        var offset = 8;
        if (options.IncludePresentStage)
        {
            row.Append(Header("Present Stage", widths[offset]));
            offset++;
        }

        row.Append(
            Header("SO amt & dt", widths[offset]),
            Header("PDC dt", widths[offset + 1]),
            MergedHeader(string.Empty, widths[offset + 2], restart: false),
            MergedHeader(string.Empty, widths[offset + 3], restart: false));
        return row;
    }

    private static W.TableCell MergedHeader(string text, int width, bool restart)
    {
        var properties = BaseCellProperties(width, HeaderFill);
        properties.Append(new W.VerticalMerge
        {
            Val = restart ? W.MergedCellValues.Restart : W.MergedCellValues.Continue
        });
        return new W.TableCell(properties, Paragraph(text, 14, bold: true, color: Navy, align: W.JustificationValues.Center, after: 0));
    }

    private static W.TableCell SpanHeader(string text, int width, int span)
    {
        var properties = BaseCellProperties(width, HeaderFill);
        properties.Append(new W.GridSpan { Val = span });
        return new W.TableCell(properties, Paragraph(text, 14, bold: true, color: Navy, align: W.JustificationValues.Center, after: 0));
    }

    private static W.TableCell Header(string text, int width)
        => new(
            BaseCellProperties(width, HeaderFill),
            Paragraph(text, 14, bold: true, color: Navy, align: W.JustificationValues.Center, after: 0));

    private static W.TableCell Cell(
        string text,
        int width,
        bool bold = false,
        W.JustificationValues? align = null,
        bool noWrap = false)
        => new(
            BaseCellProperties(width, null, noWrap),
            Paragraph(
                text,
                14,
                bold: bold,
                color: Ink,
                align: align ?? W.JustificationValues.Left,
                after: 0));

    private static W.TableCellProperties BaseCellProperties(int width, string? fill, bool noWrap = false)
    {
        var properties = new W.TableCellProperties(
            new W.TableCellWidth { Width = width.ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa },
            new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Center });

        if (!string.IsNullOrWhiteSpace(fill))
        {
            properties.Append(new W.Shading { Fill = fill, Val = W.ShadingPatternValues.Clear });
        }

        if (noWrap)
        {
            properties.Append(new W.NoWrap());
        }

        return properties;
    }

    private static string Date(DateOnly? value)
        => value?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Pdc(ArppFyProjectUpdateRow row)
        => row.IsCompleted ? "Completed" : Date(row.DevelopmentPdcDate);

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

    private static W.Paragraph Paragraph(
        string text,
        int fontSizeHalfPoints,
        bool bold = false,
        string color = Ink,
        W.JustificationValues? align = null,
        int after = 0)
    {
        var resolvedAlign = align ?? W.JustificationValues.Left;

        var paragraph = new W.Paragraph(
            new W.ParagraphProperties(
                new W.Justification { Val = resolvedAlign },
                new W.SpacingBetweenLines
                {
                    Before = "0",
                    After = after.ToString(CultureInfo.InvariantCulture),
                    Line = "205",
                    LineRule = W.LineSpacingRuleValues.Auto
                }));

        var runProperties = new W.RunProperties(
            new W.RunFonts { Ascii = Font, HighAnsi = Font, EastAsia = Font, ComplexScript = Font },
            new W.Color { Val = color },
            new W.FontSize { Val = fontSizeHalfPoints.ToString(CultureInfo.InvariantCulture) },
            new W.FontSizeComplexScript { Val = fontSizeHalfPoints.ToString(CultureInfo.InvariantCulture) });
        if (bold)
        {
            runProperties.Append(new W.Bold(), new W.BoldComplexScript());
        }

        var run = new W.Run(runProperties);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0) run.Append(new W.Break());
            run.Append(new W.Text(lines[index]) { Space = SpaceProcessingModeValues.Preserve });
        }
        paragraph.Append(run);
        return paragraph;
    }

    private static void AddStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new W.Styles(
            new W.Style(
                new W.StyleName { Val = "Normal" },
                new W.StyleRunProperties(
                    new W.RunFonts { Ascii = Font, HighAnsi = Font, EastAsia = Font, ComplexScript = Font },
                    new W.Color { Val = Ink },
                    new W.FontSize { Val = "16" },
                    new W.FontSizeComplexScript { Val = "16" }))
            {
                Type = W.StyleValues.Paragraph,
                StyleId = "Normal",
                Default = true
            });
        stylesPart.Styles.Save();
    }

    private static void AddSettings(MainDocumentPart mainPart)
    {
        var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings = new W.Settings(new W.UpdateFieldsOnOpen { Val = true });
        settingsPart.Settings.Save();
    }

    private static W.Footer BuildFooter()
    {
        const int leftWidth = 12000;
        var rightWidth = TableWidth - leftWidth;

        var table = new W.Table(
            new W.TableProperties(
                new W.TableWidth
                {
                    Width = TableWidth.ToString(CultureInfo.InvariantCulture),
                    Type = W.TableWidthUnitValues.Dxa
                },
                new W.TableLayout { Type = W.TableLayoutValues.Fixed }));

        table.Append(new W.TableGrid(
            new W.GridColumn { Width = leftWidth.ToString(CultureInfo.InvariantCulture) },
            new W.GridColumn { Width = rightWidth.ToString(CultureInfo.InvariantCulture) }));

        var row = new W.TableRow();
        row.Append(
            FooterTextCell(
                "PRISM ERP · Simulator Development Division",
                leftWidth,
                W.JustificationValues.Left),
            FooterPageCell(rightWidth));
        table.Append(row);

        return new W.Footer(table);
    }

    private static W.TableCell FooterTextCell(
        string text,
        int width,
        W.JustificationValues align)
        => new(
            new W.TableCellProperties(
                new W.TableCellWidth
                {
                    Width = width.ToString(CultureInfo.InvariantCulture),
                    Type = W.TableWidthUnitValues.Dxa
                }),
            FooterParagraph(text, align));

    private static W.TableCell FooterPageCell(int width)
    {
        var paragraph = new W.Paragraph(
            new W.ParagraphProperties(
                new W.Justification { Val = W.JustificationValues.Right },
                new W.SpacingBetweenLines { Before = "0", After = "0" }));

        paragraph.Append(FooterRun("Page "));
        paragraph.Append(FooterField("PAGE", "1"));
        paragraph.Append(FooterRun(" of "));
        paragraph.Append(FooterField("NUMPAGES", "1"));

        return new W.TableCell(
            new W.TableCellProperties(
                new W.TableCellWidth
                {
                    Width = width.ToString(CultureInfo.InvariantCulture),
                    Type = W.TableWidthUnitValues.Dxa
                }),
            paragraph);
    }

    private static W.Paragraph FooterParagraph(string text, W.JustificationValues align)
    {
        var paragraph = new W.Paragraph(
            new W.ParagraphProperties(
                new W.Justification { Val = align },
                new W.SpacingBetweenLines { Before = "0", After = "0" }));
        paragraph.Append(FooterRun(text));
        return paragraph;
    }

    private static W.Run FooterRun(string text)
        => new(
            FooterRunProperties(),
            new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });

    private static W.SimpleField FooterField(string instruction, string fallback)
    {
        var field = new W.SimpleField { Instruction = instruction };
        field.Append(new W.Run(
            FooterRunProperties(),
            new W.Text(fallback) { Space = SpaceProcessingModeValues.Preserve }));
        return field;
    }

    private static W.RunProperties FooterRunProperties()
        => new(
            new W.RunFonts { Ascii = Font, HighAnsi = Font, EastAsia = Font, ComplexScript = Font },
            new W.Color { Val = Muted },
            new W.FontSize { Val = "13" },
            new W.FontSizeComplexScript { Val = "13" });

    private static W.SectionProperties BuildSectionProperties(MainDocumentPart mainPart, FooterPart footerPart)
        => new(
            new W.FooterReference
            {
                Type = W.HeaderFooterValues.Default,
                Id = mainPart.GetIdOfPart(footerPart)
            },
            new W.PageSize
            {
                Width = 16838U,
                Height = 11906U,
                Orient = W.PageOrientationValues.Landscape
            },
            new W.PageMargin
            {
                Top = 420,
                Right = 420U,
                Bottom = 480,
                Left = 420U,
                Header = 240U,
                Footer = 240U,
                Gutter = 0U
            },
            new W.Columns { Space = "240" },
            new W.DocGrid { LinePitch = 300 });
}

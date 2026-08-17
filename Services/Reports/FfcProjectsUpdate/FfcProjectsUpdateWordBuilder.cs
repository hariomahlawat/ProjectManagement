using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Services.Reports.FfcProjectsUpdate;

public static class FfcProjectsUpdateWordBuilder
{
    private const string Font = "Arial";
    private const string Navy = "17365D";
    private const string Ink = "172033";
    private const string Muted = "526174";
    private const string HeaderFill = "E8EEF5";
    private const string GroupFill = "F2F5F8";
    private const string Border = "AAB7C6";
    private const int TableWidth = 15700;

    private static readonly int[] WithoutOverall =
        [850, 3200, 1300, 1050, 1600, 7700];

    private static readonly int[] WithOverall =
        [850, 2700, 1250, 1050, 1550, 4200, 4100];

    public static byte[] Build(
        FfcProjectsUpdateReport report,
        FfcProjectsUpdatePresentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var resolved = options ?? FfcProjectsUpdatePresentationOptions.Default;
        var widths = resolved.IncludeOverallStatus ? WithOverall : WithoutOverall;

        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
                   stream,
                   WordprocessingDocumentType.Document,
                   autoSave: true))
        {
            document.PackageProperties.Title = FfcProjectsUpdateReport.FormalTitle;
            document.PackageProperties.Subject = "FFC country-year project update";
            document.PackageProperties.Creator = "PRISM ERP";
            document.PackageProperties.LastModifiedBy = "PRISM ERP";
            document.PackageProperties.Created = report.GeneratedAtUtc.UtcDateTime;
            document.PackageProperties.Modified = report.GeneratedAtUtc.UtcDateTime;

            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new W.Document(new W.Body());
            AddSettings(mainPart);

            var footerPart = mainPart.AddNewPart<FooterPart>();
            footerPart.Footer = BuildFooter();
            footerPart.Footer.Save();

            var body = mainPart.Document.Body!;
            body.Append(TitleParagraph(FfcProjectsUpdateReport.FormalTitle));
            body.Append(BuildTable(report.Groups, resolved, widths));
            body.Append(BuildSectionProperties(mainPart, footerPart));
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static W.Table BuildTable(
        IReadOnlyList<FfcDetailedGroupVm> groups,
        FfcProjectsUpdatePresentationOptions options,
        IReadOnlyList<int> widths)
    {
        var table = new W.Table(
            new W.TableProperties(
                new W.TableWidth
                {
                    Width = TableWidth.ToString(CultureInfo.InvariantCulture),
                    Type = W.TableWidthUnitValues.Dxa
                },
                new W.TableLayout { Type = W.TableLayoutValues.Fixed },
                Borders()));

        table.Append(new W.TableGrid(
            widths.Select(width => new W.GridColumn
            {
                Width = width.ToString(CultureInfo.InvariantCulture)
            })));

        table.Append(HeaderRow(options, widths));

        foreach (var group in groups)
        {
            table.Append(GroupRow(group, widths.Count));

            for (var index = 0; index < group.Rows.Count; index++)
            {
                var row = group.Rows[index];
                var tableRow = new W.TableRow(
                    new W.TableRowProperties(new W.CantSplit()));

                tableRow.Append(
                    Cell(row.Serial.ToString(CultureInfo.InvariantCulture), widths[0], align: W.JustificationValues.Center),
                    Cell(row.ProjectName, widths[1], bold: true),
                    Cell(
                        row.CostInCr.HasValue
                            ? (row.CostInCr.Value * 100m).ToString("N2", CultureInfo.InvariantCulture)
                            : string.Empty,
                        widths[2],
                        align: W.JustificationValues.Right),
                    Cell(row.Quantity.ToString("N0", CultureInfo.InvariantCulture), widths[3], align: W.JustificationValues.Right),
                    Cell(row.Status, widths[4]),
                    Cell(Narrative(row.ProgressText), widths[5]));

                if (options.IncludeOverallStatus)
                {
                    tableRow.Append(OverallCell(
                        index == 0 ? Narrative(group.OverallRemarks) : string.Empty,
                        widths[6],
                        group.Rows.Count > 1,
                        index == 0));
                }

                table.Append(tableRow);
            }
        }

        return table;
    }

    private static W.TableRow HeaderRow(
        FfcProjectsUpdatePresentationOptions options,
        IReadOnlyList<int> widths)
    {
        var headings = options.IncludeOverallStatus
            ? new[] { "S. No.", "Project", "Cost (₹ lakh)", "Quantity", "Status", "Current progress", "Overall status" }
            : new[] { "S. No.", "Project", "Cost (₹ lakh)", "Quantity", "Status", "Current progress" };

        var row = new W.TableRow(
            new W.TableRowProperties(
                new W.TableHeader(),
                new W.CantSplit()));

        for (var index = 0; index < headings.Length; index++)
        {
            row.Append(Cell(
                headings[index],
                widths[index],
                bold: true,
                fill: HeaderFill,
                color: Navy,
                align: index == 0
                    ? W.JustificationValues.Center
                    : index is 2 or 3
                        ? W.JustificationValues.Right
                        : W.JustificationValues.Left,
                noWrap: index is 0 or 2 or 3 or 4));
        }

        return row;
    }

    private static W.TableRow GroupRow(FfcDetailedGroupVm group, int columnCount)
    {
        var cell = new W.TableCell(
            new W.TableCellProperties(
                new W.GridSpan { Val = columnCount },
                new W.TableCellWidth
                {
                    Width = TableWidth.ToString(CultureInfo.InvariantCulture),
                    Type = W.TableWidthUnitValues.Dxa
                },
                new W.Shading { Fill = GroupFill, Val = W.ShadingPatternValues.Clear }),
            Paragraph(
                $"{group.CountryName} – {group.Year}",
                17,
                bold: true,
                color: Navy,
                after: 0));

        return new W.TableRow(
            new W.TableRowProperties(new W.CantSplit()),
            cell);
    }

    private static W.TableCell OverallCell(
        string text,
        int width,
        bool mergeRows,
        bool restart)
    {
        var properties = BaseCellProperties(width, null);

        if (mergeRows)
        {
            properties.Append(new W.VerticalMerge
            {
                Val = restart ? W.MergedCellValues.Restart : W.MergedCellValues.Continue
            });
        }

        return new W.TableCell(
            properties,
            restart ? Paragraph(text, 16, after: 0) : new W.Paragraph());
    }

    private static W.TableCell Cell(
        string? text,
        int width,
        bool bold = false,
        string? fill = null,
        string color = Ink,
        W.JustificationValues? align = null,
        bool noWrap = false)
        => new(
            BaseCellProperties(width, fill, noWrap),
            Paragraph(
                text ?? string.Empty,
                16,
                bold: bold,
                color: color,
                align: align ?? W.JustificationValues.Left,
                after: 0));

    private static W.TableCellProperties BaseCellProperties(
        int width,
        string? fill,
        bool noWrap = false)
    {
        var properties = new W.TableCellProperties(
            new W.TableCellWidth
            {
                Width = width.ToString(CultureInfo.InvariantCulture),
                Type = W.TableWidthUnitValues.Dxa
            },
            new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Top });

        if (!string.IsNullOrWhiteSpace(fill))
        {
            properties.Append(new W.Shading
            {
                Fill = fill,
                Val = W.ShadingPatternValues.Clear
            });
        }

        if (noWrap)
        {
            properties.Append(new W.NoWrap());
        }

        return properties;
    }

    private static W.Paragraph TitleParagraph(string text)
        => Paragraph(
            text,
            26,
            bold: true,
            color: Navy,
            align: W.JustificationValues.Center,
            after: 100);

    private static W.Paragraph Paragraph(
        string text,
        int halfPoints,
        bool bold = false,
        string color = Ink,
        W.JustificationValues? align = null,
        int after = 0)
    {
        var runProperties = new W.RunProperties(
            new W.RunFonts
            {
                Ascii = Font,
                HighAnsi = Font,
                EastAsia = Font,
                ComplexScript = Font
            },
            new W.Color { Val = color },
            new W.FontSize { Val = halfPoints.ToString(CultureInfo.InvariantCulture) },
            new W.FontSizeComplexScript { Val = halfPoints.ToString(CultureInfo.InvariantCulture) });

        if (bold)
        {
            runProperties.Append(new W.Bold(), new W.BoldComplexScript());
        }

        var paragraph = new W.Paragraph(
            new W.ParagraphProperties(
                new W.Justification { Val = align ?? W.JustificationValues.Left },
                new W.SpacingBetweenLines
                {
                    Before = "0",
                    After = after.ToString(CultureInfo.InvariantCulture),
                    LineRule = W.LineSpacingRuleValues.Auto
                }),
            new W.Run(
                runProperties,
                new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));

        return paragraph;
    }

    private static W.TableBorders Borders()
        => new(
            new W.TopBorder { Val = W.BorderValues.Single, Color = Border, Size = 5 },
            new W.LeftBorder { Val = W.BorderValues.Single, Color = Border, Size = 5 },
            new W.BottomBorder { Val = W.BorderValues.Single, Color = Border, Size = 5 },
            new W.RightBorder { Val = W.BorderValues.Single, Color = Border, Size = 5 },
            new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Color = Border, Size = 4 },
            new W.InsideVerticalBorder { Val = W.BorderValues.Single, Color = Border, Size = 4 });

    private static void AddSettings(MainDocumentPart mainPart)
    {
        var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings = new W.Settings(new W.UpdateFieldsOnOpen { Val = true });
        settingsPart.Settings.Save();
    }

    private static W.Footer BuildFooter()
    {
        const int leftWidth = 11700;
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
            new W.RunFonts
            {
                Ascii = Font,
                HighAnsi = Font,
                EastAsia = Font,
                ComplexScript = Font
            },
            new W.Color { Val = Muted },
            new W.FontSize { Val = "13" },
            new W.FontSizeComplexScript { Val = "13" });

    private static W.SectionProperties BuildSectionProperties(
        MainDocumentPart mainPart,
        FooterPart footerPart)
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
                Top = 500,
                Right = 567U,
                Bottom = 500,
                Left = 567U,
                Header = 250U,
                Footer = 250U,
                Gutter = 0U
            });

    private static string Narrative(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim()
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);
}

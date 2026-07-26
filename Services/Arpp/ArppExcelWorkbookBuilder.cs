using ClosedXML.Excel;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Arpp;

public sealed class ArppExcelWorkbookBuilder
{
    private const int ColumnCount = 11;
    private const int HeaderRow = 8;

    public byte[] Build(ArppIssueDetails issue, DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(issue);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("ARPP");

        BuildDocumentHeader(worksheet, issue, generatedAtUtc);
        BuildSummary(worksheet, issue);
        BuildTable(worksheet, issue);
        ConfigureLayout(worksheet, Math.Max(HeaderRow, HeaderRow + issue.Entries.Count));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildDocumentHeader(
        IXLWorksheet worksheet,
        ArppIssueDetails issue,
        DateTimeOffset generatedAtUtc)
    {
        var title = worksheet.Range(1, 1, 1, ColumnCount).Merge();
        title.Value = issue.Name;
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 16;
        title.Style.Font.FontColor = XLColor.FromHtml("#17365D");

        var identity = worksheet.Range(2, 1, 2, ColumnCount).Merge();
        var issueLabel = issue.Kind == ArppIssueKind.Original
            ? "Original ARPP"
            : $"Addendum {issue.IssueSequence}";
        identity.Value = $"ARPP / PPP · FY {FinancialYearHelper.Format(issue.FinancialYearStart)} · " +
                         $"{issueLabel} · Issued {issue.IssueDate:dd MMM yyyy}";
        identity.Style.Font.FontColor = XLColor.FromHtml("#526174");

        var generated = worksheet.Range(3, 1, 3, ColumnCount).Merge();
        generated.Value = $"Generated from PRISM ERP on {generatedAtUtc:dd MMM yyyy, HH:mm} UTC";
        generated.Style.Font.FontColor = XLColor.FromHtml("#526174");

        var source = worksheet.Range(4, 1, 4, ColumnCount).Merge();
        var sourceLabel = issue.Attachment is null
            ? "Issued HQ PDF: not attached in PRISM"
            : $"Issued HQ PDF: {issue.Attachment.OriginalFileName} · SHA-256 {issue.Attachment.Sha256[..Math.Min(12, issue.Attachment.Sha256.Length)]}…";
        var verificationLabel = issue.IsVerified
            ? $"Record status: Verified and locked · {issue.VerifiedAtUtc:dd MMM yyyy HH:mm} UTC"
            : "Record status: Unverified";
        source.Value = $"{sourceLabel} · {verificationLabel}";
        source.Style.Font.FontColor = issue.Attachment is null || !issue.IsVerified
            ? XLColor.FromHtml("#9A6700")
            : XLColor.FromHtml("#146C43");
    }

    private static void BuildSummary(IXLWorksheet worksheet, ArppIssueDetails issue)
    {
        SetSummary(worksheet, 5, 1, "Rows", issue.Entries.Count);
        SetSummary(worksheet, 5, 3, "Linked", issue.LinkedCount);
        SetSummary(worksheet, 5, 5, "Unlinked", issue.UnlinkedCount);
        SetSummary(worksheet, 5, 7, "Total IPA cost (₹)", issue.TotalIpaCost, "[$₹-en-IN] #,##,##0.00");

        var column = 1;
        foreach (var category in Enum.GetValues<ArppCategory>())
        {
            var summary = issue.CategorySummary[category];
            SetSummary(
                worksheet,
                6,
                column,
                ArppDisplayNames.For(category),
                $"{summary.Count} · {IndianCurrencyFormatter.FormatRupees(summary.TotalIpaCost)}");
            column += 2;
        }

        var summaryRange = worksheet.Range(5, 1, 6, ColumnCount);
        summaryRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        summaryRange.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        summaryRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#AAB7C6");
        summaryRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#D8E0E8");
        summaryRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void SetSummary(
        IXLWorksheet worksheet,
        int row,
        int labelColumn,
        string label,
        object value,
        string? numberFormat = null)
    {
        var labelCell = worksheet.Cell(row, labelColumn);
        labelCell.Value = label;
        labelCell.Style.Font.Bold = true;
        labelCell.Style.Font.FontColor = XLColor.FromHtml("#17365D");
        labelCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F7FA");

        var valueCell = worksheet.Cell(row, labelColumn + 1);
        switch (value)
        {
            case int intValue:
                valueCell.Value = intValue;
                valueCell.Style.NumberFormat.Format = "#,##0";
                break;
            case decimal decimalValue:
                valueCell.Value = decimalValue;
                valueCell.Style.NumberFormat.Format = numberFormat ?? "#,##0.00";
                break;
            default:
                valueCell.Value = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                break;
        }
    }

    private static void BuildTable(IXLWorksheet worksheet, ArppIssueDetails issue)
    {
        var headings = new[]
        {
            "Order",
            "Serial No.",
            "Project reference as issued",
            "Linked PRISM project",
            "Case file",
            "Category",
            "IPA cost (₹)",
            "CFA",
            "Fund",
            "DFPDS schedule",
            "Link status"
        };

        for (var column = 1; column <= headings.Length; column++)
        {
            worksheet.Cell(HeaderRow, column).Value = headings[column - 1];
        }

        var header = worksheet.Range(HeaderRow, 1, HeaderRow, ColumnCount);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.FromHtml("#17365D");
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
        header.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        header.Style.Border.BottomBorderColor = XLColor.FromHtml("#7F91A5");
        header.Style.Alignment.WrapText = true;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Row(HeaderRow).Height = 30;

        var rowNumber = HeaderRow + 1;
        foreach (var entry in issue.Entries.OrderBy(entry => entry.SortOrder).ThenBy(entry => entry.Id))
        {
            worksheet.Cell(rowNumber, 1).Value = entry.SortOrder;
            worksheet.Cell(rowNumber, 2).Value = entry.SerialNumber;
            worksheet.Cell(rowNumber, 3).Value = entry.ProjectReference;
            worksheet.Cell(rowNumber, 4).Value = entry.ProjectName ?? string.Empty;
            worksheet.Cell(rowNumber, 5).Value = entry.ProjectCaseFileNumber ?? string.Empty;
            worksheet.Cell(rowNumber, 6).Value = ArppDisplayNames.For(entry.Category);
            worksheet.Cell(rowNumber, 7).Value = entry.IpaCost;
            worksheet.Cell(rowNumber, 7).Style.NumberFormat.Format = "[$₹-en-IN] #,##,##0.00";
            worksheet.Cell(rowNumber, 8).Value = entry.Cfa;
            worksheet.Cell(rowNumber, 9).Value = entry.Fund;
            worksheet.Cell(rowNumber, 10).Value = entry.DfpdsSchedule;
            worksheet.Cell(rowNumber, 11).Value = entry.ProjectId.HasValue ? "Linked" : "Linkage pending";

            if (entry.Category == ArppCategory.Delisted)
            {
                worksheet.Range(rowNumber, 1, rowNumber, ColumnCount)
                    .Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF4D6");
            }

            rowNumber++;
        }

        var lastRow = Math.Max(HeaderRow, rowNumber - 1);
        if (lastRow > HeaderRow)
        {
            var data = worksheet.Range(HeaderRow + 1, 1, lastRow, ColumnCount);
            data.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
            data.Style.Border.BottomBorderColor = XLColor.FromHtml("#D8E0E8");
            data.Style.Alignment.WrapText = true;
            data.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            worksheet.Range(HeaderRow + 1, 1, lastRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(HeaderRow + 1, 6, lastRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(HeaderRow + 1, 7, lastRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        worksheet.Range(HeaderRow, 1, lastRow, ColumnCount).SetAutoFilter();
    }

    private static void ConfigureLayout(IXLWorksheet worksheet, int lastRow)
    {
        worksheet.SheetView.FreezeRows(HeaderRow);
        worksheet.SheetView.FreezeColumns(2);

        worksheet.Column(1).Width = 8;
        worksheet.Column(2).Width = 13;
        worksheet.Column(3).Width = 42;
        worksheet.Column(4).Width = 28;
        worksheet.Column(5).Width = 18;
        worksheet.Column(6).Width = 15;
        worksheet.Column(7).Width = 18;
        worksheet.Column(8).Width = 24;
        worksheet.Column(9).Width = 16;
        worksheet.Column(10).Width = 18;
        worksheet.Column(11).Width = 16;

        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        worksheet.PageSetup.FitToPages(1, 0);
        worksheet.PageSetup.Margins.Top = 0.35;
        worksheet.PageSetup.Margins.Bottom = 0.45;
        worksheet.PageSetup.Margins.Left = 0.25;
        worksheet.PageSetup.Margins.Right = 0.25;
        worksheet.PageSetup.SetRowsToRepeatAtTop(HeaderRow, HeaderRow);
        worksheet.PageSetup.Footer.Left.AddText("PRISM ERP · Simulator Development Division");
        worksheet.PageSetup.Footer.Right.AddText("Page &P of &N");
    }
}

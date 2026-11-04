using System.Globalization;
using ClosedXML.Excel;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services.IndustryPartners;

// SECTION: Industry Directory Excel export
// Two sheets: one row per organisation (with its primary contact), and every contact.
// Values are written as text, never as formulas, so a field starting with "=" or "+"
// (an international phone number, for instance) is shown exactly as entered.
public static class IndustryPartnerDirectoryExcel
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const double MaxColumnWidth = 60;

    public static byte[] Build(
        IReadOnlyList<IndustryPartnerExportRow> rows,
        string viewDescription,
        DateTimeOffset exportedUtc)
    {
        ArgumentNullException.ThrowIfNull(rows);

        using var workbook = new XLWorkbook();
        var exportedIst = IstClock.ToIst(exportedUtc).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

        var organisations = workbook.AddWorksheet("Organisations");
        WriteHeader(organisations, new[]
        {
            "Organisation", "Location", "Primary contact", "Phone", "Email",
            "Contacts", "JDP status", "JDP projects", "Current JDP projects", "Notes", "Last updated"
        });

        var line = 2;
        foreach (var row in rows)
        {
            var primary = row.PrimaryContact;
            var column = 1;
            SetText(organisations.Cell(line, column++), row.Name);
            SetText(organisations.Cell(line, column++), row.Location);
            SetText(organisations.Cell(line, column++), primary is null ? null : DisplayName(primary));
            SetText(organisations.Cell(line, column++), primary?.Phone);
            SetText(organisations.Cell(line, column++), primary?.Email);
            organisations.Cell(line, column++).Value = row.Contacts.Count;
            SetText(organisations.Cell(line, column++), row.StatusLabel);
            organisations.Cell(line, column++).Value = row.ProjectCount;
            organisations.Cell(line, column++).Value = row.ActiveProjectCount;
            SetText(organisations.Cell(line, column++), row.Remarks);
            SetText(organisations.Cell(line, column), IstClock.ToIst(row.LastUpdatedUtc).ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
            line++;
        }

        Finish(organisations, line - 1);

        var contacts = workbook.AddWorksheet("Contacts");
        WriteHeader(contacts, new[] { "Organisation", "Contact", "Phone", "Email", "Primary" });
        line = 2;
        foreach (var row in rows)
        {
            for (var index = 0; index < row.Contacts.Count; index++)
            {
                var contact = row.Contacts[index];
                SetText(contacts.Cell(line, 1), row.Name);
                SetText(contacts.Cell(line, 2), DisplayName(contact));
                SetText(contacts.Cell(line, 3), contact.Phone);
                SetText(contacts.Cell(line, 4), contact.Email);
                SetText(contacts.Cell(line, 5), index == 0 ? "Yes" : string.Empty);
                line++;
            }
        }

        Finish(contacts, line - 1);

        var about = workbook.AddWorksheet("About");
        SetText(about.Cell(1, 1), "PRISM Industry Directory export");
        about.Cell(1, 1).Style.Font.Bold = true;
        SetText(about.Cell(2, 1), "View");
        SetText(about.Cell(2, 2), viewDescription);
        SetText(about.Cell(3, 1), "Organisations");
        about.Cell(3, 2).Value = rows.Count;
        SetText(about.Cell(4, 1), "Exported (IST)");
        SetText(about.Cell(4, 2), exportedIst);
        about.Column(1).Width = 18;
        about.Column(2).Width = 60;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string DisplayName(IndustryPartnerExportContact contact) =>
        string.IsNullOrWhiteSpace(contact.Name) ? "General contact" : contact.Name.Trim();

    private static void SetText(IXLCell cell, string? value)
    {
        // Explicit text: never interpreted as a number, date or formula.
        cell.SetValue(value ?? string.Empty);
        cell.Style.NumberFormat.Format = "@";
    }

    private static void WriteHeader(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            var cell = sheet.Cell(1, index + 1);
            cell.SetValue(headers[index]);
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF3FF");
        }

        sheet.SheetView.FreezeRows(1);
    }

    private static void Finish(IXLWorksheet sheet, int lastRow)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        if (lastRow >= 1)
        {
            sheet.Range(1, 1, Math.Max(1, lastRow), lastColumn).SetAutoFilter();
        }

        sheet.Columns(1, lastColumn).AdjustToContents(1, Math.Min(Math.Max(1, lastRow), 500));
        foreach (var column in sheet.Columns(1, lastColumn))
        {
            if (column.Width > MaxColumnWidth)
            {
                column.Width = MaxColumnWidth;
                column.Style.Alignment.WrapText = true;
            }
        }
    }
}

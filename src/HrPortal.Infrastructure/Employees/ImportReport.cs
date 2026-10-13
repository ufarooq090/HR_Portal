using ClosedXML.Excel;

namespace HrPortal.Infrastructure.Employees;

/// <summary>
/// The workbooks every spreadsheet import shares (EMP-07, RB-12): a template with instructions, and an error report
/// that is the uploaded file with an Errors column, ready to fix and upload again.
/// </summary>
public static class ImportReport
{
    public static byte[] ErrorReport(string sheetName, Sheet sheet, IReadOnlyList<ImportError> errors)
    {
        using var workbook = new XLWorkbook();
        var data = workbook.AddWorksheet(sheetName);
        var headers = sheet.Headers.Where(h => SpreadsheetReader.Key(h) != "errors").ToList();
        for (var c = 0; c < headers.Count; c++)
        {
            data.Cell(1, c + 1).Value = headers[c];
        }

        data.Cell(1, headers.Count + 1).Value = "Errors";
        data.Row(1).Style.Font.Bold = true;

        var byRow = errors.GroupBy(e => e.Row).ToDictionary(g => g.Key, g => string.Join("; ", g.Select(e => $"{e.Column}: {e.Message}")));
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            for (var c = 0; c < headers.Count; c++)
            {
                // Text cells, so IDs and phone numbers keep their leading zeros on re-upload.
                data.Cell(r + 2, c + 1).SetValue(row.Get(headers[c]));
            }

            if (byRow.TryGetValue(row.RowNumber, out var message))
            {
                data.Cell(r + 2, headers.Count + 1).Value = message;
                data.Row(r + 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDE7E9");
            }
        }

        data.SheetView.FreezeRows(1);
        data.Columns().AdjustToContents(1, Math.Min(sheet.Rows.Count + 1, 200), 8, 60);

        var list = workbook.AddWorksheet("Error list");
        list.Cell(1, 1).Value = "Row";
        list.Cell(1, 2).Value = "Column";
        list.Cell(1, 3).Value = "Problem";
        list.Row(1).Style.Font.Bold = true;
        for (var i = 0; i < errors.Count; i++)
        {
            list.Cell(i + 2, 1).Value = errors[i].Row;
            list.Cell(i + 2, 2).Value = errors[i].Column;
            list.Cell(i + 2, 3).Value = errors[i].Message;
        }

        list.Columns().AdjustToContents();
        return ToBytes(workbook);
    }

    /// <param name="validValues">Lists of allowed values, shown under the column help.</param>
    public static byte[] Template(string sheetName, IReadOnlyList<ImportColumn> columns, IEnumerable<(string Title, IEnumerable<string> Values)> validValues)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(sheetName);
        for (var c = 0; c < columns.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = columns[c].Name;
            sheet.Column(c + 1).Style.NumberFormat.Format = "@";
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();

        var help = workbook.AddWorksheet("Instructions");
        help.Cell(1, 1).Value = "Column";
        help.Cell(1, 2).Value = "Required";
        help.Cell(1, 3).Value = "What to enter";
        for (var i = 0; i < columns.Count; i++)
        {
            help.Cell(i + 2, 1).Value = columns[i].Name;
            help.Cell(i + 2, 2).Value = columns[i].Required ? "Yes" : "No";
            help.Cell(i + 2, 3).Value = columns[i].Help;
        }

        var row = columns.Count + 3;
        foreach (var (title, values) in validValues)
        {
            help.Cell(row++, 1).Value = title;
            foreach (var value in values)
            {
                help.Cell(row++, 3).Value = value;
            }

            row++;
        }

        help.Row(1).Style.Font.Bold = true;
        help.Column(1).Style.Font.Bold = true;
        help.Columns().AdjustToContents();
        return ToBytes(workbook);
    }

    private static byte[] ToBytes(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using HrPortal.Domain.Common;

namespace HrPortal.Infrastructure.Employees;

/// <param name="RowNumber">The row number HR sees in Excel (the header is row 1).</param>
/// <param name="Values">Cell text keyed by normalised header (see <see cref="SpreadsheetReader.Key"/>).</param>
public sealed record SheetRow(int RowNumber, IReadOnlyDictionary<string, string> Values)
{
    public string Get(string column) => Values.TryGetValue(SpreadsheetReader.Key(column), out var v) ? v : string.Empty;
}

public sealed record Sheet(IReadOnlyList<string> Headers, IReadOnlyList<SheetRow> Rows)
{
    public bool HasColumn(string column) => Headers.Any(h => SpreadsheetReader.Key(h) == SpreadsheetReader.Key(column));
}

/// <summary>Reads the first worksheet of an .xlsx file (ClosedXML) or a .csv file (CsvHelper) into text cells.</summary>
public static class SpreadsheetReader
{
    /// <summary>Header matching ignores case, spaces and punctuation: "Employee number" = "EmployeeNumber".</summary>
    public static string Key(string header) =>
        new string(header.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    public static Sheet Read(Stream stream, string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        try
        {
            return extension switch
            {
                ".xlsx" => ReadXlsx(stream),
                ".csv" => ReadCsv(stream),
                _ => throw new DomainValidationException("file", "Upload an Excel (.xlsx) or CSV (.csv) file."),
            };
        }
        catch (Exception e) when (e is not DomainValidationException)
        {
            throw new DomainValidationException("file", "The file could not be read. Check it is a valid .xlsx or .csv file.");
        }
    }

    private static Sheet ReadXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var headerRow = sheet.FirstRowUsed();
        if (headerRow is null)
        {
            return new Sheet([], []);
        }

        var lastColumn = headerRow.LastCellUsed().Address.ColumnNumber;
        var headers = Enumerable.Range(1, lastColumn).Select(c => headerRow.Cell(c).GetString().Trim()).ToList();

        var rows = new List<SheetRow>();
        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()))
        {
            var values = new Dictionary<string, string>();
            for (var c = 0; c < headers.Count; c++)
            {
                values[Key(headers[c])] = CellText(row.Cell(c + 1).Value);
            }

            AddIfNotBlank(rows, row.RowNumber(), values);
        }

        return new Sheet(headers, rows);
    }

    private static string CellText(XLCellValue value) =>
        value.IsBlank ? string.Empty
        : value.IsDateTime ? DateOnly.FromDateTime(value.GetDateTime()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : value.IsNumber ? value.GetNumber().ToString(CultureInfo.InvariantCulture)
        : value.IsText ? value.GetText().Trim()
        : value.ToString(CultureInfo.InvariantCulture).Trim();

    private static Sheet ReadCsv(Stream stream)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            BadDataFound = null,
        });

        if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is null)
        {
            return new Sheet([], []);
        }

        var headers = csv.HeaderRecord.Select(h => h.Trim()).ToList();
        var rows = new List<SheetRow>();
        while (csv.Read())
        {
            var values = new Dictionary<string, string>();
            for (var c = 0; c < headers.Count; c++)
            {
                values[Key(headers[c])] = csv.TryGetField<string>(c, out var field) ? field?.Trim() ?? string.Empty : string.Empty;
            }

            AddIfNotBlank(rows, csv.Parser.Row, values);
        }

        return new Sheet(headers, rows);
    }

    private static void AddIfNotBlank(List<SheetRow> rows, int rowNumber, Dictionary<string, string> values)
    {
        if (values.Values.Any(v => v.Length > 0))
        {
            rows.Add(new SheetRow(rowNumber, values));
        }
    }
}

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace NetOpsTerminal.Application.Devices;

public sealed partial class DeviceTransferService
{
    private static List<Item> ReadExcel(Stream input, CancellationToken cancellationToken)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(input, false);
        WorkbookPart workbook = document.WorkbookPart
            ?? throw new InvalidDataException("The Excel workbook is missing its workbook data.");
        Workbook workbookXml = workbook.Workbook
            ?? throw new InvalidDataException("The Excel workbook is missing its workbook definition.");
        Sheet sheet = workbookXml.Sheets?.Elements<Sheet>()
            .FirstOrDefault(item => string.Equals(item.Name?.Value, "Devices", StringComparison.OrdinalIgnoreCase))
            ?? workbookXml.Sheets?.Elements<Sheet>().FirstOrDefault()
            ?? throw new InvalidDataException("The Excel workbook has no worksheets.");
        if (sheet.Id?.Value is not { } relationshipId)
            throw new InvalidDataException("The Excel device worksheet is invalid.");
        WorksheetPart worksheet = (WorksheetPart)workbook.GetPartById(relationshipId);
        SharedStringTable? sharedStrings = workbook.SharedStringTablePart?.SharedStringTable;
        Worksheet worksheetXml = worksheet.Worksheet
            ?? throw new InvalidDataException("The Excel device worksheet is missing its data.");
        List<Row> rows = worksheetXml.GetFirstChild<SheetData>()?.Elements<Row>().ToList() ?? [];
        if (rows.Count == 0)
            throw new InvalidDataException("The Excel device worksheet is empty.");

        string[] headers = ReadExcelRow(rows[0], sharedStrings);
        Dictionary<string, int> columns = MapHeaders(headers);
        var items = new List<Item>();
        foreach (Row row in rows.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] values = ReadExcelRow(row, sharedStrings);
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            int rowNumber = checked((int)(row.RowIndex?.Value ?? (uint)(items.Count + 2)));
            items.Add(CreateSpreadsheetItem(values, columns, rowNumber));
            if (items.Count > MaximumDeviceCount)
                throw new InvalidDataException($"A device import may contain at most {MaximumDeviceCount:N0} devices.");
        }
        return items;
    }

    private static string[] ReadExcelRow(Row row, SharedStringTable? sharedStrings)
    {
        Cell[] cells = row.Elements<Cell>().ToArray();
        if (cells.Length == 0) return [];
        int maximumColumn = cells.Max(cell => ColumnIndex(cell.CellReference?.Value));
        var values = new string[maximumColumn + 1];
        foreach (Cell cell in cells)
        {
            if (cell.CellFormula is not null)
                throw new InvalidDataException($"Excel formulas are not supported in device imports ({cell.CellReference?.Value}).");
            values[ColumnIndex(cell.CellReference?.Value)] = CellText(cell, sharedStrings);
        }
        return values;
    }

    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return 0;
        int index = 0;
        foreach (char character in reference)
        {
            if (!char.IsLetter(character)) break;
            index = (index * 26) + char.ToUpperInvariant(character) - 'A' + 1;
        }
        return Math.Max(0, index - 1);
    }

    private static string CellText(Cell cell, SharedStringTable? sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.InnerText ?? string.Empty;
        string value = cell.CellValue?.InnerText ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(value, out int index)
            && sharedStrings is not null)
        {
            return sharedStrings.Elements<SharedStringItem>().ElementAtOrDefault(index)?.InnerText ?? string.Empty;
        }
        if (cell.DataType?.Value == CellValues.Boolean)
            return value == "1" ? "Yes" : "No";
        return value;
    }
}

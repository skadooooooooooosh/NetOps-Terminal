using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace NetOpsTerminal.Application.Devices;

public sealed partial class DeviceTransferService
{
    public async Task WriteExcelTemplateAsync(Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();

        // Avalonia's storage provider may return a write-only destination stream.
        // Open XML packages need a readable, seekable stream while they are built,
        // so complete the package in memory before touching the selected file.
        using var buffer = new MemoryStream();
        using (SpreadsheetDocument document = SpreadsheetDocument.Create(
                   buffer, SpreadsheetDocumentType.Workbook, true))
        {
            WorkbookPart workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            AddStyles(workbookPart);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            AddDevicesSheet(workbookPart, sheets);
            AddInstructionsSheet(workbookPart, sheets);
            workbookPart.Workbook.Save();
        }

        cancellationToken.ThrowIfCancellationRequested();
        buffer.Position = 0;
        if (output.CanSeek)
        {
            output.Position = 0;
            output.SetLength(0);
        }

        await buffer.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static readonly string[] ExportHeaders =
    [
        "Name", "Host", "Port", "Protocol", "Device Type", "Description",
        "Vendor", "Model", "Site", "Group", "Environment",
        "Credential Name", "Credential Username", "Username Override",
        "Favorite", "Recent", "Notes", "Terminal Type", "Encoding",
        "Connect Timeout Seconds", "Serial Data Bits", "Serial Parity",
        "Serial Stop Bits", "Serial Flow Control", "Raw Line Ending",
        "Telnet Acknowledged",
    ];

    public async Task WriteExcelExportAsync(
        DeviceExportOptions options,
        Stream output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();
        Item[] items = await CreateExportItemsAsync(options, cancellationToken).ConfigureAwait(false);

        // The selected storage-provider stream can be write-only.
        using var buffer = new MemoryStream();
        using (SpreadsheetDocument document = SpreadsheetDocument.Create(
                   buffer, SpreadsheetDocumentType.Workbook, true))
        {
            WorkbookPart workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            AddStyles(workbookPart);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            AddExportDevicesSheet(workbookPart, sheets, items);
            AddExportInstructionsSheet(workbookPart, sheets);
            workbookPart.Workbook.Save();
        }

        cancellationToken.ThrowIfCancellationRequested();
        buffer.Position = 0;
        if (output.CanSeek)
        {
            output.Position = 0;
            output.SetLength(0);
        }

        await buffer.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddExportDevicesSheet(
        WorkbookPart workbook,
        Sheets sheets,
        IReadOnlyList<Item> items)
    {
        WorksheetPart part = workbook.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        data.Append(new Row(ExportHeaders.Select((value, index) =>
            TextCell(value, index + 1, 1, 1)))
        { RowIndex = 1U });
        for (int index = 0; index < items.Count; index++)
        {
            uint rowIndex = checked((uint)(index + 2));
            Item item = items[index];
            string[] values =
            [
                item.Name, item.Host, item.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.ConnectionType.ToString(), item.DeviceType.ToString(), item.Description,
                item.Vendor, item.Model, item.SiteName, item.GroupName, item.Environment.ToString(),
                item.CredentialName, item.CredentialUsername, item.UsernameOverride ?? string.Empty,
                item.IsFavorite ? "Yes" : "No", item.IsRecent ? "Yes" : "No", item.Notes,
                item.TerminalType, item.Encoding,
                item.ConnectTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.SerialDataBits.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.SerialParity, item.SerialStopBits, item.SerialFlowControl, item.RawLineEnding,
                item.TelnetAcknowledged ? "Yes" : "No",
            ];
            data.Append(new Row(values.Select((value, column) =>
                TextCell(value, column + 1, rowIndex, 0)))
            { RowIndex = rowIndex });
        }

        var columns = new Columns(
            new Column { Min = 1, Max = 2, Width = 24, CustomWidth = true },
            new Column { Min = 3, Max = 5, Width = 15, CustomWidth = true },
            new Column { Min = 6, Max = 6, Width = 36, CustomWidth = true },
            new Column { Min = 7, Max = 26, Width = 20, CustomWidth = true });
        var view = new SheetView { WorkbookViewId = 0U };
        view.Append(new Pane
        {
            VerticalSplit = 1D,
            TopLeftCell = "A2",
            ActivePane = PaneValues.BottomLeft,
            State = PaneStateValues.Frozen,
        });
        part.Worksheet = new Worksheet(new SheetViews(view), columns, data);
        part.Worksheet.Append(new AutoFilter
        {
            Reference = $"A1:Z{items.Count + 1}",
        });
        sheets.Append(new Sheet
        {
            Id = workbook.GetIdOfPart(part),
            SheetId = 1U,
            Name = "Devices",
        });
    }

    private static void AddExportInstructionsSheet(WorkbookPart workbook, Sheets sheets)
    {
        WorksheetPart part = workbook.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        data.Append(RowOf(1, 1, "NetOps Terminal Device Export"));
        data.Append(RowOf(2, 0, "This workbook contains device metadata only. It has no passwords, secret references, private keys, or trusted host keys."));
        data.Append(RowOf(3, 0, "Use Import devices to review and restore these rows. SSH credentials must be mapped to local credential profiles."));
        data.Append(RowOf(4, 0, "Telnet and Raw TCP are plaintext. Telnet Acknowledged must be Yes before a Telnet device can be saved."));
        data.Append(RowOf(5, 0, "For Serial devices, Host is the COM port and Port is the baud rate."));
        data.Append(RowOf(6, 0, "Do not add formulas. Save as .xlsx before importing."));
        part.Worksheet = new Worksheet(
            new Columns(new Column { Min = 1, Max = 1, Width = 120, CustomWidth = true }),
            data);
        sheets.Append(new Sheet
        {
            Id = workbook.GetIdOfPart(part),
            SheetId = 2U,
            Name = "Instructions",
        });
    }

    private static void AddDevicesSheet(WorkbookPart workbook, Sheets sheets)
    {
        WorksheetPart part = workbook.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        data.Append(new Row(SpreadsheetHeaders.Select((value, index) => TextCell(value, index + 1, 1, 1))));
        var columns = new Columns(
            new Column { Min = 1, Max = 2, Width = 22, CustomWidth = true },
            new Column { Min = 3, Max = 3, Width = 10, CustomWidth = true },
            new Column { Min = 4, Max = 4, Width = 16, CustomWidth = true });
        var view = new SheetView { WorkbookViewId = 0U };
        view.Append(new Pane
        {
            VerticalSplit = 1D,
            TopLeftCell = "A2",
            ActivePane = PaneValues.BottomLeft,
            State = PaneStateValues.Frozen,
        });
        part.Worksheet = new Worksheet(new SheetViews(view), columns, data);
        part.Worksheet.Append(new AutoFilter { Reference = "A1:D1" });
        part.Worksheet.Append(CreateValidations());
        sheets.Append(new Sheet
        {
            Id = workbook.GetIdOfPart(part),
            SheetId = 1U,
            Name = "Devices",
        });
    }

    private static void AddInstructionsSheet(WorkbookPart workbook, Sheets sheets)
    {
        WorksheetPart part = workbook.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        data.Append(RowOf(1, 1, "NetOps Terminal Device Import Template"));
        data.Append(RowOf(2, 0, "Enter one device per row. Name and Host are required."));
        data.Append(RowOf(3, 0, "Port is optional and defaults to 22. Device Type is optional and defaults to Other."));
        data.Append(RowOf(4, 0, "Device Type values: Switch, Router, Firewall, Server, AccessPoint, Storage, Other. Legacy Appliance and ConsoleServer values remain importable."));
        data.Append(RowOf(5, 0, "Optional metadata columns from older templates are still accepted when present."));
        data.Append(RowOf(6, 0, "Do not use formulas. Save the workbook as .xlsx before importing."));
        part.Worksheet = new Worksheet(
            new Columns(new Column { Min = 1, Max = 1, Width = 110, CustomWidth = true }),
            data);
        sheets.Append(new Sheet
        {
            Id = workbook.GetIdOfPart(part),
            SheetId = 2U,
            Name = "Instructions",
        });
    }

    private static void AddStyles(WorkbookPart workbook)
    {
        WorkbookStylesPart part = workbook.AddNewPart<WorkbookStylesPart>();
        var headerFont = new Font(new Bold(), new Color { Rgb = "FFFFFFFF" });
        var headerFill = new Fill(new PatternFill(new ForegroundColor { Rgb = "FF1769AA" })
        {
            PatternType = PatternValues.Solid,
        });
        var headerFormat = new CellFormat
        {
            FontId = 1U,
            FillId = 2U,
            BorderId = 0U,
            ApplyFont = true,
            ApplyFill = true,
        };
        headerFormat.Append(new Alignment { Vertical = VerticalAlignmentValues.Center });
        part.Stylesheet = new Stylesheet(
            new Fonts(new Font(), headerFont),
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                headerFill),
            new Borders(new Border()),
            new CellStyleFormats(new CellFormat()),
            new CellFormats(new CellFormat(), headerFormat));
        part.Stylesheet.Save();
    }

    private static DataValidations CreateValidations()
    {
        DataValidation[] rules =
        [
            ListValidation("D2:D10001", "Switch,Router,Firewall,Server,AccessPoint,Storage,Other"),
        ];
        var validations = new DataValidations { Count = (uint)rules.Length };
        validations.Append(rules);
        return validations;
    }

    private static DataValidation ListValidation(string range, string values)
    {
        var validation = new DataValidation
        {
            Type = DataValidationValues.List,
            AllowBlank = true,
            ShowErrorMessage = true,
            ErrorTitle = "Invalid value",
            Error = "Choose a value from the list.",
            SequenceOfReferences = new ListValue<StringValue> { InnerText = range },
        };
        validation.Append(new Formula1($"\"{values}\""));
        return validation;
    }

    private static Row RowOf(uint rowIndex, uint styleIndex, string value)
        => new(TextCell(value, 1, rowIndex, styleIndex)) { RowIndex = rowIndex };

    private static Cell TextCell(string value, int columnIndex, uint rowIndex, uint styleIndex)
    {
        string reference = ColumnName(columnIndex) + rowIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new Cell(new InlineString(new Text(value)))
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            StyleIndex = styleIndex,
        };
    }

    private static string ColumnName(int index)
    {
        string name = string.Empty;
        while (index > 0)
        {
            index--;
            name = (char)('A' + (index % 26)) + name;
            index /= 26;
        }
        return name;
    }
}

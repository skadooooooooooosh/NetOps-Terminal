using System.Globalization;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Devices;

public sealed partial class DeviceTransferService
{
    private static readonly string[] SpreadsheetHeaders =
    [
        "Name", "Host", "Port", "Device Type", "Protocol", "Telnet Acknowledged",
        "Access Mode", "Web Address", "Browser Preference", "Default Open Action",
    ];

    public async Task<string> ConvertToJsonAsync(
        Stream input,
        DeviceTransferFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (format == DeviceTransferFormat.Json)
        {
            using var reader = new StreamReader(input, leaveOpen: true);
            string json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            _ = Parse(json);
            return json;
        }

        IReadOnlyList<Item> items = format == DeviceTransferFormat.Excel
            ? ReadExcel(input, cancellationToken)
            : ReadCsv(input, cancellationToken);
        string converted = JsonSerializer.Serialize(
            new Document(CurrentSchemaVersion, DateTimeOffset.UtcNow, items), JsonOptions);
        _ = Parse(converted);
        return converted;
    }

    private static List<Item> ReadCsv(Stream input, CancellationToken cancellationToken)
    {
        using var parser = new TextFieldParser(input)
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false,
            TextFieldType = FieldType.Delimited,
        };
        parser.SetDelimiters(",");
        string[] headers = parser.ReadFields() ?? throw new InvalidDataException("The CSV file has no header row.");
        Dictionary<string, int> columns = MapHeaders(headers);
        var items = new List<Item>();
        int rowNumber = 1;
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            string[] values = parser.ReadFields() ?? [];
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            items.Add(CreateSpreadsheetItem(values, columns, rowNumber));
            if (items.Count > MaximumDeviceCount)
                throw new InvalidDataException($"A device import may contain at most {MaximumDeviceCount:N0} devices.");
        }
        return items;
    }

    private static Dictionary<string, int> MapHeaders(IReadOnlyList<string> headers)
    {
        var columns = headers.Select((name, index) => (Name: NormalizeHeader(name), index))
            .Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
        foreach (string required in new[] { "name", "host" })
        {
            if (!columns.ContainsKey(required))
                throw new InvalidDataException($"The spreadsheet is missing the required '{required}' column.");
        }
        return columns;
    }

    private static string NormalizeHeader(string? value)
        => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Field(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> columns, string name)
        => columns.TryGetValue(NormalizeHeader(name), out int index) && index < values.Count
            ? values[index]?.Trim() ?? string.Empty
            : string.Empty;

    private static Item CreateSpreadsheetItem(
        IReadOnlyList<string> values,
        IReadOnlyDictionary<string, int> columns,
        int rowNumber)
    {
        try
        {
            string protocolValue = Field(values, columns, "Protocol");
            ConnectionType protocol = ParseEnum(
                protocolValue, ConnectionType.Ssh, "protocol");
            int port = ParsePort(Field(values, columns, "Port"), protocol);
            if (string.IsNullOrWhiteSpace(protocolValue) && port != 22)
                throw new ArgumentException("Set Protocol explicitly for a port other than 22. For Telnet, select Telnet and acknowledge plaintext traffic.");
            DeviceAccessMode accessMode = ParseEnum(Field(values, columns, "Access Mode"),
                DeviceAccessMode.TerminalOnly, "access mode");
            DeviceType type = ParseEnum(Field(values, columns, "Device Type"), DeviceType.Other, "device type");
            DeviceEnvironment environment = ParseEnum(Field(values, columns, "Environment"), DeviceEnvironment.Unknown, "environment");
            return new Item(
                Field(values, columns, "Name"), Field(values, columns, "Description"),
                Field(values, columns, "Host"), port, type, Field(values, columns, "Vendor"),
                Field(values, columns, "Model"), Field(values, columns, "Site"),
                Field(values, columns, "Group"), environment, Field(values, columns, "Credential Name"),
                Field(values, columns, "Credential Username"), NullIfEmpty(Field(values, columns, "Username Override")),
                ParseBoolean(Field(values, columns, "Favorite")), ParseBoolean(Field(values, columns, "Recent")),
                Field(values, columns, "Notes"))
            {
                ConnectionType = protocol,
                AccessMode = accessMode,
                WebAddress = Field(values, columns, "Web Address"),
                BrowserPreference = ParseEnum(Field(values, columns, "Browser Preference"),
                    WebBrowserPreference.WindowsDefault, "browser preference"),
                DefaultOpenAction = ParseEnum(Field(values, columns, "Default Open Action"),
                    accessMode == DeviceAccessMode.WebOnly ? DeviceOpenAction.Web : DeviceOpenAction.Terminal,
                    "default open action"),
                TerminalType = Defaulted(Field(values, columns, "Terminal Type"), "xterm-256color"),
                Encoding = ParseEnum(Field(values, columns, "Encoding"),
                    TerminalTextEncoding.Utf8, "encoding").ToString(),
                ConnectTimeoutSeconds = ParseInteger(Field(values, columns, "Connect Timeout Seconds"), 15),
                SerialDataBits = ParseInteger(Field(values, columns, "Serial Data Bits"), 8),
                SerialParity = ParseEnum(Field(values, columns, "Serial Parity"),
                    SerialParity.None, "serial parity").ToString(),
                SerialStopBits = ParseEnum(Field(values, columns, "Serial Stop Bits"),
                    SerialStopBits.One, "serial stop bits").ToString(),
                SerialFlowControl = ParseEnum(Field(values, columns, "Serial Flow Control"),
                    SerialFlowControl.None, "serial flow control").ToString(),
                RawLineEnding = ParseEnum(Field(values, columns, "Raw Line Ending"),
                    TerminalLineEnding.CarriageReturnLineFeed, "raw line ending").ToString(),
                TelnetAcknowledged = ParseBoolean(Field(values, columns, "Telnet Acknowledged")),
            };
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidDataException($"Spreadsheet row {rowNumber} is invalid: {exception.Message}", exception);
        }
    }

    private static int ParsePort(string value, ConnectionType protocol)
        => ParseInteger(value, protocol switch
        {
            ConnectionType.Ssh => SshDeviceConnectionSettings.DefaultPort,
            ConnectionType.Telnet => TelnetConnectionOptions.DefaultPort,
            ConnectionType.Serial => SerialConnectionOptions.DefaultBaudRate,
            _ => 0,
        });

    private static int ParseInteger(string value, int fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

    private static string Defaulted(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static T ParseEnum<T>(string value, T fallback, string label) where T : struct, Enum
        => string.IsNullOrWhiteSpace(value) ? fallback
            : Enum.TryParse(value.Replace(" ", string.Empty), true, out T parsed) && Enum.IsDefined(parsed)
                ? parsed : throw new FormatException($"Unknown {label} '{value}'.");

    private static bool ParseBoolean(string value) => value.Trim().ToUpperInvariant() switch
    {
        "" or "NO" or "N" or "FALSE" or "0" => false,
        "YES" or "Y" or "TRUE" or "1" => true,
        _ => throw new FormatException($"Expected Yes or No, but found '{value}'."),
    };

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}

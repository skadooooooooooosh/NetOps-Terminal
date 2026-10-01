using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Core.Devices;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace NetOpsTerminal.Application.Tests;

public sealed class DeviceTransferServiceTests
{
    [Fact]
    public async Task ExportAndImportTransfersMetadataWithoutSecretReferences()
    {
        Guid credentialId = Guid.NewGuid();
        var credential = new CredentialProfile(credentialId, "Operations", "netadmin",
            "SECRET-REFERENCE-MUST-NOT-EXPORT", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var site = new Site(Guid.NewGuid(), "Singapore");
        var group = new DeviceGroup(Guid.NewGuid(), "Core", site.Id);
        var device = new DeviceProfile(Guid.NewGuid(), "R1-Core", "Main router",
            new SshDeviceConnectionSettings("10.0.0.1"), DeviceType.Router, "Cisco", "C9300",
            site.Id, group.Id, DeviceEnvironment.Production, credentialId, null, true, "Rack 1",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        var source = new FakeCatalog([device], [site], [group], [credential], [device.Id]);
        var service = new DeviceTransferService(source);

        string json = await service.ExportAsync(new DeviceExportOptions(), CancellationToken.None);

        Assert.DoesNotContain("SECRET-REFERENCE-MUST-NOT-EXPORT", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secretReference", json, StringComparison.OrdinalIgnoreCase);
        var target = new FakeCatalog([], [], [], [credential], []);
        var targetService = new DeviceTransferService(target);
        DeviceImportPreview preview = await targetService.ReviewImportAsync(json, CancellationToken.None);
        DeviceImportResult result = await targetService.ImportAsync(
            json, DeviceImportConflictPolicy.KeepBoth, null, CancellationToken.None);
        Assert.Equal(1, preview.DeviceCount);
        Assert.Equal(1, result.ImportedCount);
        DeviceEditRequest saved = Assert.Single(target.Saved);
        Assert.Equal("Singapore", saved.SiteName);
        Assert.Equal("Core", saved.GroupName);
        Assert.True(saved.IsFavorite);
        Assert.Single(target.Recent);
    }

    [Theory]
    [InlineData(ConnectionType.Telnet, "legacy.test", 23)]
    [InlineData(ConnectionType.Serial, "COM3", 115200)]
    [InlineData(ConnectionType.RawTcp, "console.test", 2001)]
    public async Task ExportImportPreservesNonSshProtocolWithoutCredential(
        ConnectionType type, string host, int port)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var device = new DeviceProfile(Guid.NewGuid(), type.ToString(), string.Empty,
            new SavedDeviceConnectionSettings(type, host, port, encoding: "Ascii",
                telnetAcknowledged: type == ConnectionType.Telnet),
            DeviceType.Other, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Lab, null, null, false, string.Empty, now, now, null);
        var source = new DeviceTransferService(new FakeCatalog([device], [], [], [], []));
        string json = await source.ExportAsync(new DeviceExportOptions(), CancellationToken.None);
        var target = new FakeCatalog([], [], [], [], []);
        var transfer = new DeviceTransferService(target);

        DeviceImportPreview preview = await transfer.ReviewImportAsync(json, CancellationToken.None);
        DeviceImportResult result = await transfer.ImportAsync(
            json, DeviceImportConflictPolicy.KeepBoth, null, CancellationToken.None);

        Assert.Equal(0, preview.CredentialRequiredCount);
        Assert.Equal(1, result.ImportedCount);
        DeviceEditRequest saved = Assert.Single(target.Saved);
        Assert.Equal(type, saved.ConnectionType);
        Assert.Equal("Ascii", saved.Encoding);
        Assert.Null(saved.CredentialProfileId);
    }

    [Theory]
    [InlineData(DeviceTransferFormat.Json)]
    [InlineData(DeviceTransferFormat.Excel)]
    public async Task WebDevicesRoundTripWithoutBecomingSshConnections(DeviceTransferFormat format)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var webOnly = new DeviceProfile(Guid.NewGuid(), "GUI only", string.Empty,
            new SavedDeviceConnectionSettings(ConnectionType.Ssh, "gui.test", 22),
            DeviceType.Switch, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Unknown, null, null, false, string.Empty,
            now, now, null, DeviceAccessMode.WebOnly,
            new DeviceWebAccessSettings("https://gui.test/manage",
                WebBrowserPreference.MicrosoftEdge, DeviceOpenAction.Web));
        var both = new DeviceProfile(Guid.NewGuid(), "CLI and GUI", string.Empty,
            new SavedDeviceConnectionSettings(ConnectionType.Telnet, "both.test", 23,
                telnetAcknowledged: true),
            DeviceType.Switch, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Unknown, null, null, false, string.Empty,
            now, now, null, DeviceAccessMode.TerminalAndWeb,
            new DeviceWebAccessSettings("https://both.test/",
                WebBrowserPreference.WindowsDefault, DeviceOpenAction.Terminal));
        var source = new DeviceTransferService(new FakeCatalog(
            [webOnly, both], [], [], [], []));
        string json;
        if (format == DeviceTransferFormat.Excel)
        {
            using var stream = new MemoryStream();
            await source.WriteExcelExportAsync(new DeviceExportOptions(), stream, CancellationToken.None);
            stream.Position = 0;
            json = await source.ConvertToJsonAsync(stream, DeviceTransferFormat.Excel, CancellationToken.None);
        }
        else
        {
            json = await source.ExportAsync(new DeviceExportOptions(), CancellationToken.None);
        }
        var target = new FakeCatalog([], [], [], [], []);
        var transfer = new DeviceTransferService(target);

        DeviceImportPreview preview = await transfer.ReviewImportAsync(json, CancellationToken.None);
        DeviceImportResult result = await transfer.ImportAsync(
            json, DeviceImportConflictPolicy.KeepBoth, null, CancellationToken.None);

        Assert.Equal(0, preview.CredentialRequiredCount);
        Assert.Equal(2, result.ImportedCount);
        DeviceEditRequest gui = Assert.Single(target.Saved, item => item.Name == "GUI only");
        Assert.Equal(DeviceAccessMode.WebOnly, gui.AccessMode);
        Assert.Null(gui.CredentialProfileId);
        Assert.Equal("https://gui.test/manage", gui.WebAccess?.Address);
        Assert.Equal(WebBrowserPreference.MicrosoftEdge, gui.WebAccess?.Browser);
        DeviceEditRequest dual = Assert.Single(target.Saved, item => item.Name == "CLI and GUI");
        Assert.Equal(DeviceAccessMode.TerminalAndWeb, dual.AccessMode);
        Assert.Equal(ConnectionType.Telnet, dual.ConnectionType);
        Assert.Equal("https://both.test/", dual.WebAccess?.Address);
    }
    [Fact]
    public async Task ImportSkipsDeviceWhenCredentialCannotBeMapped()
    {
        var source = new FakeCatalog([], [], [], [], []);
        var service = new DeviceTransferService(source);
        const string json = """{"schemaVersion":1,"exportedAtUtc":"2026-09-24T00:00:00Z","devices":[{"name":"R1","description":"","host":"10.0.0.1","port":22,"deviceType":1,"vendor":"","model":"","siteName":"","groupName":"","environment":4,"credentialName":"Missing","credentialUsername":"user","usernameOverride":null,"isFavorite":false,"isRecent":false,"notes":""}]}""";

        DeviceImportResult result = await service.ImportAsync(
            json, DeviceImportConflictPolicy.KeepBoth, null, CancellationToken.None);

        Assert.Equal(1, result.CredentialRequiredCount);
        Assert.Empty(source.Saved);
    }

    [Fact]
    public async Task ExcelExportRoundTripsAllProtocolsWithoutSecrets()
    {
        var credential = new CredentialProfile(Guid.NewGuid(), "Operations", "operator",
            "SECRET-REFERENCE-MUST-NOT-EXPORT", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeviceProfile[] devices =
        [
            new(Guid.NewGuid(), "SSH", "=1+1",
                new SshDeviceConnectionSettings("ssh.test"), DeviceType.Switch,
                string.Empty, string.Empty, null, null, DeviceEnvironment.Production,
                credential.Id, null, true, string.Empty, now, now, null),
            new(Guid.NewGuid(), "Telnet", string.Empty,
                new SavedDeviceConnectionSettings(ConnectionType.Telnet, "telnet.test", 23,
                    telnetAcknowledged: true), DeviceType.Router,
                string.Empty, string.Empty, null, null, DeviceEnvironment.Lab,
                null, null, false, string.Empty, now, now, null),
            new(Guid.NewGuid(), "Serial", string.Empty,
                new SavedDeviceConnectionSettings(ConnectionType.Serial, "COM3", 115200,
                    serialDataBits: 7), DeviceType.ConsoleServer,
                string.Empty, string.Empty, null, null, DeviceEnvironment.Lab,
                null, null, false, string.Empty, now, now, null),
            new(Guid.NewGuid(), "Raw", string.Empty,
                new SavedDeviceConnectionSettings(ConnectionType.RawTcp, "raw.test", 2001,
                    rawLineEnding: "LineFeed"), DeviceType.Other,
                string.Empty, string.Empty, null, null, DeviceEnvironment.Lab,
                null, null, false, string.Empty, now, now, null),
        ];
        var source = new DeviceTransferService(
            new FakeCatalog(devices, [], [], [credential], [devices[1].Id]));
        using var stream = new MemoryStream();

        await source.WriteExcelExportAsync(
            new DeviceExportOptions(), stream, CancellationToken.None);
        stream.Position = 0;
        using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator().Validate(workbook).ToList();
            Assert.Empty(errors);
            WorkbookPart part = Assert.IsType<WorkbookPart>(workbook.WorkbookPart);
            Sheet sheet = Assert.Single(
                Assert.IsType<Workbook>(part.Workbook).Sheets!.Elements<Sheet>(),
                x => x.Name == "Devices");
            Worksheet worksheet = Assert.IsType<Worksheet>(
                Assert.IsType<WorksheetPart>(part.GetPartById(sheet.Id!)).Worksheet);
            Assert.Equal(5, worksheet.GetFirstChild<SheetData>()!.Elements<Row>().Count());
            AssertDropdown(worksheet, "AA2:AA10001", "TerminalOnly,WebOnly,TerminalAndWeb");
            AssertDropdown(worksheet, "AC2:AC10001", "WindowsDefault,MicrosoftEdge,GoogleChrome");
            AssertDropdown(worksheet, "AD2:AD10001", "Terminal,Web");
            Assert.All(worksheet.Descendants<Cell>(), cell =>
            {
                Assert.Null(cell.CellFormula);
                Assert.Equal(CellValues.InlineString, cell.DataType?.Value);
            });
            Assert.DoesNotContain("SECRET-REFERENCE-MUST-NOT-EXPORT",
                worksheet.InnerText, StringComparison.Ordinal);
        }

        stream.Position = 0;
        string json = await source.ConvertToJsonAsync(
            stream, DeviceTransferFormat.Excel, CancellationToken.None);
        var target = new FakeCatalog([], [], [], [credential], []);
        var importer = new DeviceTransferService(target);
        DeviceImportResult result = await importer.ImportAsync(
            json, DeviceImportConflictPolicy.KeepBoth, null, CancellationToken.None);

        Assert.Equal(4, result.ImportedCount);
        Assert.Equal(
            new[] { ConnectionType.Ssh, ConnectionType.Telnet, ConnectionType.Serial, ConnectionType.RawTcp },
            target.Saved.Select(x => x.ConnectionType));
        Assert.True(target.Saved[0].IsFavorite);
        Assert.True(target.Saved[1].TelnetAcknowledged);
        Assert.Equal(7, target.Saved[2].SerialDataBits);
        Assert.Equal("LineFeed", target.Saved[3].RawLineEnding);
        Assert.Single(target.Recent);
    }

    [Fact]
    public async Task ExportKeepsNestedFolderPath()
    {
        CredentialProfile credential = CreateCredential();
        Site site = new(Guid.NewGuid(), "DC");
        DeviceGroup root = new(Guid.NewGuid(), "Core", site.Id);
        DeviceGroup child = new(Guid.NewGuid(), "Switches", site.Id, root.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeviceProfile device = new(
            Guid.NewGuid(), "SW-01", string.Empty,
            new SshDeviceConnectionSettings("192.0.2.10"),
            DeviceType.Switch, string.Empty, string.Empty, site.Id, child.Id,
            DeviceEnvironment.Unknown, credential.Id, null, false, string.Empty, now, now, null);
        var service = new DeviceTransferService(
            new FakeCatalog([device], [site], [root, child], [credential], []));

        string json = await service.ExportAsync(new DeviceExportOptions(), CancellationToken.None);
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);

        Assert.Equal("Core / Switches",
            document.RootElement.GetProperty("devices")[0].GetProperty("groupName").GetString());
    }

    [Fact]
    public async Task ExcelExportSupportsWriteOnlySaveStream()
    {
        var service = new DeviceTransferService(new FakeCatalog([], [], [], [], []));
        string path = Path.Combine(
            Path.GetTempPath(), $"netops-device-export-{Guid.NewGuid():N}.xlsx");
        try
        {
            await using (var output = new FileStream(
                             path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                Assert.False(output.CanRead);
                await service.WriteExcelExportAsync(
                    new DeviceExportOptions(), output, CancellationToken.None);
            }

            await using FileStream input = File.OpenRead(path);
            using SpreadsheetDocument workbook = SpreadsheetDocument.Open(input, false);
            Assert.Empty(new OpenXmlValidator().Validate(workbook));
            Assert.Contains(
                Assert.IsType<Sheets>(Assert.IsType<Workbook>(
                    Assert.IsType<WorkbookPart>(workbook.WorkbookPart).Workbook).Sheets)
                    .Elements<Sheet>(),
                sheet => sheet.Name == "Devices");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExcelExportRespectsScopeAndMetadataOptions()
    {
        var credential = CreateCredential();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeviceProfile[] devices =
        [
            new(Guid.NewGuid(), "Selected", string.Empty,
                new SshDeviceConnectionSettings("selected.test"), DeviceType.Switch,
                string.Empty, string.Empty, null, null, DeviceEnvironment.Lab,
                credential.Id, null, true, string.Empty, now, now, null),
            new(Guid.NewGuid(), "Other", string.Empty,
                new SshDeviceConnectionSettings("other.test"), DeviceType.Router,
                string.Empty, string.Empty, null, null, DeviceEnvironment.Lab,
                credential.Id, null, true, string.Empty, now, now, null),
        ];
        var service = new DeviceTransferService(
            new FakeCatalog(devices, [], [], [credential], [devices[0].Id]));
        using var stream = new MemoryStream();

        await service.WriteExcelExportAsync(
            new DeviceExportOptions([devices[0].Id], false, false),
            stream, CancellationToken.None);
        stream.Position = 0;
        string json = await service.ConvertToJsonAsync(
            stream, DeviceTransferFormat.Excel, CancellationToken.None);
        var target = new FakeCatalog([], [], [], [credential], []);
        await new DeviceTransferService(target).ImportAsync(
            json, DeviceImportConflictPolicy.KeepBoth, null, CancellationToken.None);

        DeviceEditRequest saved = Assert.Single(target.Saved);
        Assert.Equal("Selected", saved.Name);
        Assert.False(saved.IsFavorite);
        Assert.Empty(target.Recent);
    }

    [Fact]
    public async Task ExcelTemplateRoundTripsDeviceRows()
    {
        var credential = CreateCredential();
        var catalog = new FakeCatalog([], [], [], [credential], []);
        var service = new DeviceTransferService(catalog);
        using var stream = new MemoryStream();
        await service.WriteExcelTemplateAsync(stream, CancellationToken.None);
        stream.Position = 0;
        using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, true))
        {
            WorkbookPart part = Assert.IsType<WorkbookPart>(workbook.WorkbookPart);
            Workbook workbookXml = Assert.IsType<Workbook>(part.Workbook);
            Sheet sheet = Assert.Single(workbookXml.Sheets!.Elements<Sheet>(), x => x.Name == "Devices");
            var worksheet = Assert.IsType<WorksheetPart>(part.GetPartById(sheet.Id!));
            Worksheet worksheetXml = Assert.IsType<Worksheet>(worksheet.Worksheet);
            SheetData data = Assert.IsType<SheetData>(worksheetXml.GetFirstChild<SheetData>());
            data.Append(SpreadsheetRow(2, "Edge-01", "10.20.30.40", "22", "Router"));
            worksheetXml.Save();
        }
        stream.Position = 0;

        string json = await service.ConvertToJsonAsync(stream, DeviceTransferFormat.Excel, CancellationToken.None);
        DeviceImportResult result = await service.ImportAsync(
            json, DeviceImportConflictPolicy.KeepBoth, credential.Id, CancellationToken.None);

        Assert.Equal(1, result.ImportedCount);
        DeviceEditRequest saved = Assert.Single(catalog.Saved);
        Assert.Equal("Edge-01", saved.Name);
        Assert.Equal(22, saved.Port);
        Assert.Equal(DeviceType.Router, saved.DeviceType);
        Assert.Equal(DeviceEnvironment.Unknown, saved.Environment);
        Assert.Equal(string.Empty, saved.GroupName);
    }

    [Fact]
    public async Task CsvImportHandlesQuotedFields()
    {
        var credential = CreateCredential();
        var catalog = new FakeCatalog([], [], [], [credential], []);
        var service = new DeviceTransferService(catalog);
        const string csv = "Name,Host,Port,Device Type,Description,Environment,Favorite,Notes\r\nR2,10.0.0.2,22,Router,\"Core, backup\",Lab,Yes,\"Rack 2, row B\"\r\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        string json = await service.ConvertToJsonAsync(stream, DeviceTransferFormat.Csv, CancellationToken.None);
        await service.ImportAsync(json, DeviceImportConflictPolicy.KeepBoth, credential.Id, CancellationToken.None);

        DeviceEditRequest saved = Assert.Single(catalog.Saved);
        Assert.Equal("Core, backup", saved.Description);
        Assert.Equal("Rack 2, row B", saved.Notes);
    }

    [Fact]
    public async Task ExcelTemplateDeviceTypeDropdownOmitsRetiredChoices()
    {
        var service = new DeviceTransferService(new FakeCatalog([], [], [], [], []));
        using var stream = new MemoryStream();
        await service.WriteExcelTemplateAsync(stream, CancellationToken.None);
        stream.Position = 0;
        using SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, false);
        WorkbookPart part = Assert.IsType<WorkbookPart>(workbook.WorkbookPart);
        Workbook workbookXml = Assert.IsType<Workbook>(part.Workbook);
        Sheet sheet = Assert.Single(workbookXml.Sheets!.Elements<Sheet>(), x => x.Name == "Devices");
        var worksheet = Assert.IsType<WorksheetPart>(part.GetPartById(sheet.Id!));
        Worksheet worksheetXml = Assert.IsType<Worksheet>(worksheet.Worksheet);
        DataValidation[] validations = Assert.IsType<DataValidations>(
            worksheetXml.GetFirstChild<DataValidations>())
            .Elements<DataValidation>().ToArray();
        DataValidation validation = Assert.Single(validations, rule =>
            rule.SequenceOfReferences?.InnerText == "D2:D10001");
        Assert.Contains(validations, rule => rule.SequenceOfReferences?.InnerText == "E2:E10001"
            && rule.Formula1?.Text?.Contains("Telnet", StringComparison.Ordinal) == true);

        Assert.Equal("Switch,Router,Firewall,Server,AccessPoint,Storage,Other",
            validation.Formula1?.Text?.Trim('"'));
        AssertDropdown(worksheetXml, "G2:G10001", "TerminalOnly,WebOnly,TerminalAndWeb");
        AssertDropdown(worksheetXml, "I2:I10001", "WindowsDefault,MicrosoftEdge,GoogleChrome");
        AssertDropdown(worksheetXml, "J2:J10001", "Terminal,Web");
    }

    [Fact]
    public async Task ExcelTemplateIsSchemaValid()
    {
        var service = new DeviceTransferService(new FakeCatalog([], [], [], [], []));
        using var stream = new MemoryStream();
        await service.WriteExcelTemplateAsync(stream, CancellationToken.None);
        stream.Position = 0;
        using SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, false);

        List<ValidationErrorInfo> errors = new OpenXmlValidator().Validate(workbook).ToList();

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine,
            errors.Select(error => $"{error.Id}: {error.Description} at {error.Path?.XPath}")));
    }

    [Fact]
    public async Task ExcelTemplateCanBeSavedToWriteOnlyStream()
    {
        var service = new DeviceTransferService(new FakeCatalog([], [], [], [], []));
        string path = Path.Combine(Path.GetTempPath(), $"netops-device-template-{Guid.NewGuid():N}.xlsx");
        try
        {
            await using (var output = new FileStream(
                             path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                Assert.False(output.CanRead);
                await service.WriteExcelTemplateAsync(output, CancellationToken.None);
            }

            await using FileStream input = File.OpenRead(path);
            using SpreadsheetDocument workbook = SpreadsheetDocument.Open(input, false);
            WorkbookPart part = Assert.IsType<WorkbookPart>(workbook.WorkbookPart);
            Workbook workbookXml = Assert.IsType<Workbook>(part.Workbook);
            Sheets sheets = Assert.IsType<Sheets>(workbookXml.Sheets);
            Assert.Contains(sheets.Elements<Sheet>(), sheet => sheet.Name == "Devices");
            Assert.Contains(sheets.Elements<Sheet>(), sheet => sheet.Name == "Instructions");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExcelImportRejectsFormulaCells()
    {
        var service = new DeviceTransferService(new FakeCatalog([], [], [], [], []));
        using var stream = new MemoryStream();
        await service.WriteExcelTemplateAsync(stream, CancellationToken.None);
        stream.Position = 0;
        using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, true))
        {
            WorkbookPart part = Assert.IsType<WorkbookPart>(workbook.WorkbookPart);
            Workbook workbookXml = Assert.IsType<Workbook>(part.Workbook);
            Sheet sheet = Assert.Single(workbookXml.Sheets!.Elements<Sheet>(), x => x.Name == "Devices");
            var worksheet = Assert.IsType<WorksheetPart>(part.GetPartById(sheet.Id!));
            Worksheet worksheetXml = Assert.IsType<Worksheet>(worksheet.Worksheet);
            SheetData data = Assert.IsType<SheetData>(worksheetXml.GetFirstChild<SheetData>());
            Row row = SpreadsheetRow(2, "R3", "10.0.0.3");
            row.Elements<Cell>().First().CellFormula = new CellFormula("1+1");
            data.Append(row);
            worksheetXml.Save();
        }
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ConvertToJsonAsync(stream, DeviceTransferFormat.Excel, CancellationToken.None));
    }

    [Fact]
    public async Task UpdatingNinetySevenMatchingDevicesKeepsTheirIds()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeviceProfile[] devices = Enumerable.Range(1, 97)
            .Select(index => new DeviceProfile(
                Guid.NewGuid(), $"Device-{index}", string.Empty,
                new SavedDeviceConnectionSettings(
                    ConnectionType.Serial, $"COM{index}", 9600),
                DeviceType.Other, string.Empty, string.Empty,
                null, null, DeviceEnvironment.Unknown,
                null, null, false, string.Empty, now, now, null))
            .ToArray();
        var source = new DeviceTransferService(
            new FakeCatalog(devices, [], [], [], []));
        string json = await source.ExportAsync(
            new DeviceExportOptions(), CancellationToken.None);
        var targetCatalog = new FakeCatalog(devices, [], [], [], []);
        var target = new DeviceTransferService(targetCatalog);

        DeviceImportPreview preview = await target.ReviewImportAsync(
            json, CancellationToken.None);
        DeviceImportResult result = await target.ImportAsync(
            json, DeviceImportConflictPolicy.Update, null, CancellationToken.None);

        Assert.Equal(97, preview.ConflictCount);
        Assert.Equal(97, result.UpdatedCount);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(devices.Select(device => device.Id).ToHashSet(),
            targetCatalog.Saved.Select(request => request.Id!.Value).ToHashSet());
    }
    [Theory]
    [InlineData("", "Set Protocol explicitly")]
    [InlineData(",Protocol", "SSH imports must use port 22")]
    public async Task ImportRejectsSshPort23(string extraHeader, string expectedMessage)
    {
        var service = new DeviceTransferService(new FakeCatalog([], [], [], [], []));
        string protocolValue = extraHeader.Length == 0 ? string.Empty : ",SSH";
        string csv = $"Name,Host,Port{extraHeader}\r\nEdge-01,192.0.2.10,23{protocolValue}\r\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ConvertToJsonAsync(stream, DeviceTransferFormat.Csv, CancellationToken.None));

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }
    [Fact]
    public async Task ExcelTemplateTelnetRowUsesPort23()
    {
        var service = new DeviceTransferService(new FakeCatalog([], [], [], [], []));
        using var stream = new MemoryStream();
        await service.WriteExcelTemplateAsync(stream, CancellationToken.None);
        stream.Position = 0;
        using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, true))
        {
            WorkbookPart part = Assert.IsType<WorkbookPart>(workbook.WorkbookPart);
            Sheet sheet = Assert.Single(Assert.IsType<Workbook>(part.Workbook)
                .Sheets!.Elements<Sheet>(), item => item.Name == "Devices");
            var worksheet = Assert.IsType<WorksheetPart>(part.GetPartById(sheet.Id!));
            Assert.IsType<SheetData>(Assert.IsType<Worksheet>(worksheet.Worksheet).GetFirstChild<SheetData>())
                .Append(SpreadsheetRow(2, "Edge-01", "192.0.2.10", "23", "Switch", "Telnet", "Yes"));
            worksheet.Worksheet.Save();
        }
        stream.Position = 0;

        string json = await service.ConvertToJsonAsync(
            stream, DeviceTransferFormat.Excel, CancellationToken.None);
        DeviceImportPreview preview = await service.ReviewImportAsync(json, CancellationToken.None);

        DeviceImportPreviewItem item = Assert.Single(preview.Devices);
        Assert.Equal(ConnectionType.Telnet, item.ConnectionType);
        Assert.Equal(23, item.Port);
    }
    [Fact]
    public async Task ExplicitTelnetProtocolUpdatesMatchingSshDeviceWithoutCredential()
    {
        CredentialProfile credential = CreateCredential();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var existing = new DeviceProfile(
            Guid.NewGuid(), "Edge-01", string.Empty,
            new SshDeviceConnectionSettings("192.0.2.10", 22),
            DeviceType.Switch, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Unknown, credential.Id, null, false, string.Empty,
            now, now, null);
        var catalog = new FakeCatalog([existing], [], [], [credential], []);
        var service = new DeviceTransferService(catalog);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            "Name,Host,Port,Device Type,Protocol,Telnet Acknowledged\r\n" +
            "Edge-01,192.0.2.10,23,Switch,Telnet,Yes\r\n"));
        string json = await service.ConvertToJsonAsync(
            stream, DeviceTransferFormat.Csv, CancellationToken.None);

        DeviceImportResult result = await service.ImportAsync(
            json, DeviceImportConflictPolicy.Update, null, CancellationToken.None);

        Assert.Equal(1, result.UpdatedCount);
        DeviceEditRequest saved = Assert.Single(catalog.Saved);
        Assert.Equal(existing.Id, saved.Id);
        Assert.Equal(ConnectionType.Telnet, saved.ConnectionType);
        Assert.Equal(23, saved.Port);
        Assert.Null(saved.CredentialProfileId);
    }

    [Fact]
    public async Task KeepBothAssignsDifferentNamesToCopies()
    {
        CredentialProfile credential = CreateCredential();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var existing = new DeviceProfile(
            Guid.NewGuid(), "Edge-01", string.Empty,
            new SshDeviceConnectionSettings("192.0.2.10"),
            DeviceType.Switch, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Unknown, credential.Id, null, false, string.Empty,
            now, now, null);
        var source = new DeviceTransferService(
            new FakeCatalog([existing, existing], [], [], [credential], []));
        string json = await source.ExportAsync(new DeviceExportOptions(), CancellationToken.None);
        var catalog = new FakeCatalog([existing], [], [], [credential], []);
        var service = new DeviceTransferService(catalog);

        DeviceImportResult result = await service.ImportAsync(
            json, DeviceImportConflictPolicy.KeepBoth, null, CancellationToken.None);

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal("Edge-01 (imported)", catalog.Saved[0].Name);
        Assert.Equal("Edge-01 (imported 2)", catalog.Saved[1].Name);
        Assert.All(catalog.Saved, request => Assert.Equal(credential.Id, request.CredentialProfileId));
    }

    private static void AssertDropdown(Worksheet worksheet, string range, string options)
    {
        DataValidations validations = Assert.IsType<DataValidations>(
            worksheet.GetFirstChild<DataValidations>());
        DataValidation rule = Assert.Single(validations.Elements<DataValidation>(),
            item => item.SequenceOfReferences?.InnerText == range);
        Assert.Equal(DataValidationValues.List, rule.Type?.Value);
        Assert.True(rule.AllowBlank?.Value == true);
        Assert.True(rule.ShowErrorMessage?.Value == true);
        Assert.Equal(((char)34) + options + ((char)34), rule.Formula1?.Text);
    }

    private static CredentialProfile CreateCredential() => new(
        Guid.NewGuid(), "Local", "admin", "protected-reference", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static Row SpreadsheetRow(uint index, params string[] values)
    {
        var row = new Row { RowIndex = index };
        for (int column = 0; column < values.Length; column++)
        {
            row.Append(new Cell
            {
                CellReference = $"{(char)('A' + column)}{index}",
                DataType = CellValues.String,
                CellValue = new CellValue(values[column]),
            });
        }
        return row;
    }

    private sealed class FakeCatalog(
        IReadOnlyList<DeviceProfile> devices,
        IReadOnlyList<Site> sites,
        IReadOnlyList<DeviceGroup> groups,
        IReadOnlyList<CredentialProfile> credentials,
        IReadOnlyList<Guid> recent) : IDeviceCatalogService
    {
        public List<DeviceEditRequest> Saved { get; } = [];
        public List<Guid> Recent { get; } = [];
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<DeviceCatalogSnapshot> LoadAsync(string? searchText, DeviceCatalogFilter filter, CancellationToken cancellationToken)
            => Task.FromResult(new DeviceCatalogSnapshot(devices, sites, groups, credentials, recent));
        public Task SaveAsync(DeviceEditRequest request, CancellationToken cancellationToken)
        {
            Saved.Add(request);
            return Task.CompletedTask;
        }
        public Task DeleteEmptySiteAsync(Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteEmptyFolderAsync(Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Site> CreateSiteAsync(string name, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<DeviceGroup> CreateFolderAsync(
            string name, Guid? siteId, Guid? parentGroupId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task MoveDeviceAsync(
            Guid deviceId, Guid? siteId, Guid? groupId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task SaveExplorerOrderAsync(
            IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ResetExplorerOrderAsync(
            IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task RecordRecentAsync(Guid deviceId, DateTimeOffset connectedAtUtc, CancellationToken cancellationToken)
        {
            Recent.Add(deviceId);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

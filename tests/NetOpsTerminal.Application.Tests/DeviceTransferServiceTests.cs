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
            data.Append(SpreadsheetRow(2, "Edge-01", "10.20.30.40", "2222", "Router"));
            worksheetXml.Save();
        }
        stream.Position = 0;

        string json = await service.ConvertToJsonAsync(stream, DeviceTransferFormat.Excel, CancellationToken.None);
        DeviceImportResult result = await service.ImportAsync(
            json, DeviceImportConflictPolicy.KeepBoth, credential.Id, CancellationToken.None);

        Assert.Equal(1, result.ImportedCount);
        DeviceEditRequest saved = Assert.Single(catalog.Saved);
        Assert.Equal("Edge-01", saved.Name);
        Assert.Equal(2222, saved.Port);
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
        DataValidation validation = Assert.Single(
            Assert.IsType<DataValidations>(worksheetXml.GetFirstChild<DataValidations>())
                .Elements<DataValidation>());

        Assert.Equal("Switch,Router,Firewall,Server,AccessPoint,Storage,Other",
            validation.Formula1?.Text?.Trim('"'));
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
        public Task RecordRecentAsync(Guid deviceId, DateTimeOffset connectedAtUtc, CancellationToken cancellationToken)
        {
            Recent.Add(deviceId);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

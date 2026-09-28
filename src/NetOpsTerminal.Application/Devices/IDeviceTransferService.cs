namespace NetOpsTerminal.Application.Devices;

public interface IDeviceTransferService
{
    Task<string> ExportAsync(DeviceExportOptions options, CancellationToken cancellationToken);
    Task<DeviceImportPreview> ReviewImportAsync(string json, CancellationToken cancellationToken);
    Task<DeviceImportResult> ImportAsync(string json, DeviceImportConflictPolicy conflictPolicy, Guid? fallbackCredentialId, CancellationToken cancellationToken);
    Task<string> ConvertToJsonAsync(Stream input, DeviceTransferFormat format, CancellationToken cancellationToken);
    Task WriteExcelTemplateAsync(Stream output, CancellationToken cancellationToken);
    Task WriteExcelExportAsync(DeviceExportOptions options, Stream output, CancellationToken cancellationToken);
}

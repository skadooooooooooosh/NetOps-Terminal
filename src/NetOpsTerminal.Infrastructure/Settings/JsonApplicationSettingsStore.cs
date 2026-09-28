using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NetOpsTerminal.Application.Abstractions;
using NetOpsTerminal.Application.Settings;
using NetOpsTerminal.Infrastructure.Platform;

namespace NetOpsTerminal.Infrastructure.Settings;

public sealed partial class JsonApplicationSettingsStore(
    IApplicationDataPathProvider pathProvider,
    ILogger<JsonApplicationSettingsStore> logger) : IApplicationSettingsStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ApplicationSettings?> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(pathProvider.SettingsFilePath))
            {
                return null;
            }

            await using var stream = new FileStream(
                pathProvider.SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4_096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            try
            {
                return await JsonSerializer.DeserializeAsync<ApplicationSettings>(
                    stream,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                LogInvalidSettings(logger, pathProvider.SettingsFilePath, exception);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(pathProvider.SettingsFilePath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        var temporaryPath = $"{pathProvider.SettingsFilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(directory);

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4_096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, pathProvider.SettingsFilePath, overwrite: true);
            LogSettingsSaved(logger, pathProvider.SettingsFilePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Application settings at {SettingsPath} are invalid; defaults will be used")]
    private static partial void LogInvalidSettings(
        ILogger logger,
        string settingsPath,
        Exception exception);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Debug,
        Message = "Saved application UI settings to {SettingsPath}")]
    private static partial void LogSettingsSaved(ILogger logger, string settingsPath);
}

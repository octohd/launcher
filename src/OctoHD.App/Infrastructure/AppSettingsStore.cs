using System.Text.Json;
using System.Text.Json.Serialization;

namespace OctoHD.App.Infrastructure;

public sealed class AppSettings
{
    public string? DataFolder { get; init; }

    public string? SelectedPatchSourceId { get; init; }

    public bool IsListView { get; init; }

    public List<CustomPatchSourceSettings> PatchSources { get; init; } = [];
}

public sealed record CustomPatchSourceSettings(string Id, string DisplayName, string BaseUrl);

public sealed class AppSettingsStore
{
    private static readonly string DefaultSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OctoHD",
        "settings-v1.json");
    private readonly string _settingsDirectory;
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public AppSettingsStore(string? settingsPath = null)
    {
        _settingsPath = Path.GetFullPath(settingsPath ?? DefaultSettingsPath);
        _settingsDirectory = Path.GetDirectoryName(_settingsPath)
            ?? throw new ArgumentException("The settings path must have a parent directory.", nameof(settingsPath));
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync(
                       stream,
                       AppSettingsJsonContext.Default.AppSettings,
                       cancellationToken)
                   ?? new AppSettings();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            var temporaryPath = Path.Combine(_settingsDirectory, $"settings-{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        settings,
                        AppSettingsJsonContext.Default.AppSettings,
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, _settingsPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(CustomPatchSourceSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;

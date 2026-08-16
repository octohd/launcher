using System.Text.Json;
using System.Text.Json.Serialization;

namespace OctoHD.App.Infrastructure;

public sealed class AppSettings
{
    public string? DataFolder { get; init; }

    public string? SelectedPatchSourceId { get; init; }

    public List<CustomPatchSourceSettings> PatchSources { get; init; } = [];
}

public sealed record CustomPatchSourceSettings(string Id, string DisplayName, string BaseUrl);

public sealed class AppSettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OctoHD");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings-v1.json");
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
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
            Directory.CreateDirectory(SettingsDirectory);
            var temporaryPath = Path.Combine(SettingsDirectory, $"settings-{Guid.NewGuid():N}.tmp");
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

                File.Move(temporaryPath, SettingsPath, true);
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

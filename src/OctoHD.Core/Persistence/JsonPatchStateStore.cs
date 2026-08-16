using System.Text.Json;

namespace OctoHD.Core.Persistence;

public sealed class JsonPatchStateStore : IPatchStateStore
{
    public const string MetadataDirectoryName = ".octohd";
    public const string StateFileName = "state-v1.json";

    public async Task<LocalStateDocument> LoadAsync(
        string dataFolder,
        CancellationToken cancellationToken = default)
    {
        var statePath = GetStatePath(dataFolder);
        if (!File.Exists(statePath))
        {
            return new LocalStateDocument();
        }

        try
        {
            await using var stream = new FileStream(
                statePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync(
                stream,
                LocalStateJsonContext.Default.LocalStateDocument,
                cancellationToken).ConfigureAwait(false);
            return state?.SchemaVersion == 1 ? state : new LocalStateDocument();
        }
        catch (JsonException)
        {
            PreserveCorruptState(statePath);
            return new LocalStateDocument();
        }
    }

    public async Task SaveAsync(
        string dataFolder,
        LocalStateDocument state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var metadataDirectory = GetMetadataDirectory(dataFolder);
        Directory.CreateDirectory(metadataDirectory);

        var statePath = Path.Combine(metadataDirectory, StateFileName);
        var temporaryPath = Path.Combine(metadataDirectory, $"{StateFileName}.{Guid.NewGuid():N}.tmp");
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
                    state,
                    LocalStateJsonContext.Default.LocalStateDocument,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, statePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static string GetMetadataDirectory(string dataFolder) =>
        Path.Combine(Path.GetFullPath(dataFolder), MetadataDirectoryName);

    public static string GetStatePath(string dataFolder) =>
        Path.Combine(GetMetadataDirectory(dataFolder), StateFileName);

    private static void PreserveCorruptState(string statePath)
    {
        var backupPath = $"{statePath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        try
        {
            File.Move(statePath, backupPath, false);
        }
        catch (IOException)
        {
            // A concurrent process may already have moved the file. A fresh scan is still safe.
        }
    }
}

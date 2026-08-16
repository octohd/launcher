using OctoHD.Core.Models;
using OctoHD.Core.Persistence;

namespace OctoHD.Core.Services;

public sealed class DataFolderValidator
{
    private static readonly string[] KnownClientNames =
    [
        "WoW.exe",
        "WoWFoV.exe",
        "TurtleWoW.exe",
        "OctoWoW.exe"
    ];

    public async Task<FolderValidationResult> ValidateAsync(
        string? selectedPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return Invalid("Select an OctoWoW folder or Data folder.");
        }

        string normalizedPath;
        try
        {
            normalizedPath = ResolveDirectory(selectedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Invalid($"The folder path is invalid: {exception.Message}");
        }

        if (!Directory.Exists(normalizedPath))
        {
            return Invalid("The selected folder does not exist.");
        }

        var selectedDirectory = new DirectoryInfo(normalizedPath);
        if (!string.Equals(selectedDirectory.Name, "Data", StringComparison.OrdinalIgnoreCase))
        {
            DirectoryInfo? dataDirectory;
            try
            {
                dataDirectory = selectedDirectory
                    .EnumerateDirectories()
                    .FirstOrDefault(directory =>
                        string.Equals(directory.Name, "Data", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Invalid($"The selected OctoWoW folder could not be searched: {exception.Message}");
            }

            if (dataDirectory is null)
            {
                return Invalid("The selected folder is not a Data folder and does not contain one.");
            }

            try
            {
                normalizedPath = ResolveDirectory(dataDirectory.FullName);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return Invalid($"The Data subfolder could not be resolved: {exception.Message}");
            }
        }

        var warnings = new List<string>();
        var parent = Directory.GetParent(normalizedPath)?.FullName;
        if (parent is null || !KnownClientNames.Any(name => File.Exists(Path.Combine(parent, name))))
        {
            warnings.Add("No known WoW executable was found in the parent folder. Wine and custom installations may still work.");
        }

        var metadataDirectory = JsonPatchStateStore.GetMetadataDirectory(normalizedPath);
        var writeProbe = Path.Combine(metadataDirectory, $"write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(metadataDirectory);
            await File.WriteAllTextAsync(writeProbe, "OctoHD", cancellationToken).ConfigureAwait(false);
            File.Delete(writeProbe);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid($"OctoHD cannot write to this Data folder: {exception.Message}");
        }
        finally
        {
            if (File.Exists(writeProbe))
            {
                File.Delete(writeProbe);
            }
        }

        return new FolderValidationResult(true, normalizedPath, null, warnings);
    }

    private static FolderValidationResult Invalid(string error) =>
        new(false, null, error, Array.Empty<string>());

    private static string ResolveDirectory(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            directory = directory.ResolveLinkTarget(true) as DirectoryInfo
                ?? throw new IOException("The link target folder could not be resolved.");
        }

        return Path.TrimEndingDirectorySeparator(directory.FullName);
    }
}

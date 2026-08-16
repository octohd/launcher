using OctoHD.Core.Catalog;
using OctoHD.Core.Models;
using OctoHD.Core.Persistence;

namespace OctoHD.Core.Services;

public sealed class PatchScanner : IPatchScanner
{
    private readonly IPatchCatalog _catalog;
    private readonly IPatchStateStore _stateStore;
    private readonly FileHashService _hashService;

    public PatchScanner(
        IPatchCatalog catalog,
        IPatchStateStore stateStore,
        FileHashService? hashService = null)
    {
        _catalog = catalog;
        _stateStore = stateStore;
        _hashService = hashService ?? new FileHashService();
    }

    public async Task<IReadOnlyList<PatchScanResult>> ScanAsync(
        string dataFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        var state = await _stateStore.LoadAsync(dataFolder, cancellationToken).ConfigureAwait(false);
        var results = new Dictionary<string, PatchScanResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var targetGroup in _catalog.Patches.GroupBy(
                     patch => patch.TargetFileName,
                     StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definitions = targetGroup.ToArray();
            var activePath = PatchFileNames.ResolveInsideDataFolder(dataFolder, definitions[0].TargetFileName);
            var disabledPath = Path.Combine(dataFolder, definitions[0].DisabledFileName);
            var activeExists = File.Exists(activePath);
            var disabledExists = File.Exists(disabledPath);

            if (activeExists && disabledExists)
            {
                AddForAll(definitions, results, PatchStatus.Conflict, null, "The enabled and disabled files both exist.");
                continue;
            }

            if (!activeExists && !disabledExists)
            {
                AddForAll(definitions, results, PatchStatus.NotInstalled);
                continue;
            }

            var installedPath = activeExists ? activePath : disabledPath;
            var fileInfo = new FileInfo(installedPath);
            if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                AddForAll(definitions, results, PatchStatus.ForeignFile, installedPath, "Linked MPQ files are not managed for security reasons.");
                continue;
            }

            if (!await MpqValidator.HasValidSignatureAsync(installedPath, cancellationToken).ConfigureAwait(false))
            {
                AddForAll(definitions, results, PatchStatus.Corrupt, installedPath, "The file does not have a valid MPQ header.");
                continue;
            }

            var record = state.Patches.Values.FirstOrDefault(candidate =>
                definitions.Any(definition => string.Equals(definition.Id, candidate.PatchId, StringComparison.OrdinalIgnoreCase))
                && string.Equals(candidate.TargetFileName, definitions[0].TargetFileName, StringComparison.OrdinalIgnoreCase));
            var selected = record is null
                ? MatchBySize(definitions, fileInfo.Length)
                : definitions.FirstOrDefault(definition => string.Equals(definition.Id, record.PatchId, StringComparison.OrdinalIgnoreCase));

            if (selected is null)
            {
                AddForAll(definitions, results, PatchStatus.ForeignFile, installedPath, "The existing file cannot be safely matched to an OctoHD variant.");
                continue;
            }

            if (record is not null
                && !await MatchesRecordedContentAsync(record, fileInfo, cancellationToken).ConfigureAwait(false))
            {
                AddForAll(
                    definitions,
                    results,
                    PatchStatus.Corrupt,
                    installedPath,
                    "The contents of an OctoHD-managed patch were modified outside the app.");
                continue;
            }

            var isOutdated = record is not null
                && (string.IsNullOrWhiteSpace(record.DownloadSourceId)
                    || string.Equals(
                        record.DownloadSourceId,
                        PatchSourceDefinition.ProjectReforgedId,
                        StringComparison.OrdinalIgnoreCase))
                && (!string.Equals(record.SourceVersion, selected.Version, StringComparison.Ordinal)
                    || !string.Equals(record.ETag, selected.ETag, StringComparison.Ordinal)
                    || fileInfo.Length != selected.ExpectedSize);
            var status = (activeExists, isOutdated) switch
            {
                (true, true) => PatchStatus.UpdateAvailableActive,
                (false, true) => PatchStatus.UpdateAvailableDisabled,
                (true, false) => PatchStatus.Active,
                _ => PatchStatus.Disabled
            };

            results[selected.Id] = new PatchScanResult(
                selected,
                status,
                installedPath,
                fileInfo.Length,
                record?.SourceVersion,
                record is null ? "Detected from its unique file fingerprint." : null,
                record?.DownloadSourceId ?? PatchSourceDefinition.ProjectReforgedId);

            foreach (var other in definitions.Where(definition => !string.Equals(definition.Id, selected.Id, StringComparison.OrdinalIgnoreCase)))
            {
                results[other.Id] = new PatchScanResult(
                    other,
                    PatchStatus.NotInstalled,
                    Message: $"The {selected.VariantName} variant is installed.");
            }
        }

        return _catalog.Patches.Select(patch => results[patch.Id]).ToArray();
    }

    private async Task<bool> MatchesRecordedContentAsync(
        InstalledPatchRecord record,
        FileInfo fileInfo,
        CancellationToken cancellationToken)
    {
        if (record.FileSize != fileInfo.Length)
        {
            return false;
        }

        var recordedUtc = record.LastWriteUtc.UtcDateTime;
        var timestampDifference = (fileInfo.LastWriteTimeUtc - recordedUtc).Duration();
        if (timestampDifference <= TimeSpan.FromSeconds(2))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(record.Sha256))
        {
            return false;
        }

        var actualHash = await _hashService
            .ComputeSha256Async(fileInfo.FullName, cancellationToken)
            .ConfigureAwait(false);
        return string.Equals(actualHash, record.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static PatchDefinition? MatchBySize(IReadOnlyList<PatchDefinition> definitions, long size)
    {
        var matches = definitions.Where(definition => definition.ExpectedSize == size).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static void AddForAll(
        IEnumerable<PatchDefinition> definitions,
        IDictionary<string, PatchScanResult> results,
        PatchStatus status,
        string? path = null,
        string? message = null)
    {
        foreach (var definition in definitions)
        {
            results[definition.Id] = new PatchScanResult(
                definition,
                status,
                path,
                path is null ? null : new FileInfo(path).Length,
                Message: message);
        }
    }
}

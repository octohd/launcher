using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using OctoHD.Core.Catalog;
using OctoHD.Core.Models;
using OctoHD.Core.Persistence;

namespace OctoHD.Core.Services;

public sealed class PatchManager
{
    private const long MaximumCustomPatchSize = 8L * 1024 * 1024 * 1024;

    private readonly IPatchCatalog _catalog;
    private readonly IPatchStateStore _stateStore;
    private readonly IPatchScanner _scanner;
    private readonly PatchDependencyService _dependencyService;
    private readonly FileHashService _hashService;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public PatchManager(
        IPatchCatalog catalog,
        IPatchStateStore stateStore,
        IPatchScanner scanner,
        PatchDependencyService dependencyService,
        FileHashService hashService,
        HttpClient httpClient)
    {
        _catalog = catalog;
        _stateStore = stateStore;
        _scanner = scanner;
        _dependencyService = dependencyService;
        _hashService = hashService;
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<PatchScanResult>> InstallAsync(
        string dataFolder,
        PatchDefinition patch,
        IProgress<PatchOperationProgress>? progress = null,
        PatchSourceDefinition? source = null,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCatalogPatch(patch);
            source ??= PatchSourceDefinition.ProjectReforged;
            var downloadUri = source.Resolve(patch);
            EnsureAllowedUri(downloadUri, source);
            EnsureDataFolder(dataFolder);
            var scanResults = await _scanner.ScanAsync(dataFolder, cancellationToken).ConfigureAwait(false);
            EnsureDependenciesAreActive(patch, scanResults);

            var targetGroup = scanResults
                .Where(result => string.Equals(result.Patch.TargetFileName, patch.TargetFileName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var blocker = targetGroup.FirstOrDefault(result =>
                result.Status is PatchStatus.Conflict or PatchStatus.ForeignFile or PatchStatus.Corrupt);
            if (blocker is not null)
            {
                throw new PatchOperationException(blocker.Message ?? "The target name is occupied by a file that OctoHD cannot manage.");
            }

            var currentlyInstalled = targetGroup.FirstOrDefault(result => result.CanToggle);
            if (currentlyInstalled is not null
                && !string.Equals(currentlyInstalled.Patch.Id, patch.Id, StringComparison.OrdinalIgnoreCase))
            {
                var blockedDependents = _dependencyService
                    .GetActiveDependents(currentlyInstalled.Patch, scanResults)
                    .Where(dependent => !dependent.Dependencies.Contains(patch.Id, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                if (blockedDependents.Length > 0)
                {
                    throw new PatchOperationException(
                        $"The variant cannot be changed while {FormatNames(blockedDependents)} is enabled.");
                }
            }

            EnsureFreeSpace(dataFolder, patch.ExpectedSize);
            var metadataDirectory = JsonPatchStateStore.GetMetadataDirectory(dataFolder);
            var temporaryDirectory = Path.Combine(metadataDirectory, "tmp");
            Directory.CreateDirectory(temporaryDirectory);
            var partPath = Path.Combine(temporaryDirectory, $"{patch.Id}.mpq.part");
            var partMetadataPath = $"{partPath}.etag";

            var resumeEtag = await PreparePartFileAsync(
                partPath,
                partMetadataPath,
                patch,
                source,
                downloadUri,
                cancellationToken).ConfigureAwait(false);
            var download = await DownloadAsync(
                patch,
                source,
                downloadUri,
                dataFolder,
                partPath,
                partMetadataPath,
                resumeEtag,
                progress,
                cancellationToken).ConfigureAwait(false);
            await InstallValidatedFileAsync(
                dataFolder,
                patch,
                source,
                partPath,
                partMetadataPath,
                download,
                currentlyInstalled?.Status is not PatchStatus.Disabled and not PatchStatus.UpdateAvailableDisabled,
                cancellationToken).ConfigureAwait(false);

            return await _scanner.ScanAsync(dataFolder, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<IReadOnlyList<PatchScanResult>> SetEnabledAsync(
        string dataFolder,
        PatchDefinition patch,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCatalogPatch(patch);
            EnsureDataFolder(dataFolder);
            var scanResults = await _scanner.ScanAsync(dataFolder, cancellationToken).ConfigureAwait(false);
            var selected = scanResults.First(result => string.Equals(result.Patch.Id, patch.Id, StringComparison.OrdinalIgnoreCase));
            if (!selected.CanToggle)
            {
                throw new PatchOperationException(selected.Message ?? "This patch cannot be toggled in its current state.");
            }

            if (selected.IsActive == enabled)
            {
                return scanResults;
            }

            if (enabled)
            {
                EnsureDependenciesAreActive(patch, scanResults);
            }
            else
            {
                var dependents = _dependencyService.GetActiveDependents(patch, scanResults);
                if (dependents.Count > 0)
                {
                    throw new PatchOperationException(
                        $"Disable dependent patches first: {FormatNames(dependents)}.");
                }
            }

            var activePath = PatchFileNames.ResolveInsideDataFolder(dataFolder, patch.TargetFileName);
            var disabledPath = Path.Combine(dataFolder, patch.DisabledFileName);
            var sourcePath = enabled ? disabledPath : activePath;
            var destinationPath = enabled ? activePath : disabledPath;
            if (!File.Exists(sourcePath) || File.Exists(destinationPath))
            {
                throw new PatchOperationException("The patch state changed while toggling. Please scan again.");
            }

            var state = await _stateStore.LoadAsync(dataFolder, cancellationToken).ConfigureAwait(false);
            var existingRecord = FindRecord(state, patch.TargetFileName);
            var sha256 = existingRecord?.Sha256;
            if (string.IsNullOrWhiteSpace(sha256))
            {
                sha256 = await _hashService.ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false);
            }

            File.Move(sourcePath, destinationPath, false);
            try
            {
                RemoveTargetGroupRecords(state, patch.TargetFileName);
                var fileInfo = new FileInfo(destinationPath);
                state.Patches[patch.Id] = new InstalledPatchRecord(
                    patch.Id,
                    existingRecord?.SourceVersion ?? patch.Version,
                    patch.TargetFileName,
                    enabled,
                    fileInfo.Length,
                    sha256,
                    existingRecord?.ETag ?? patch.ETag,
                    existingRecord?.InstalledAtUtc ?? DateTimeOffset.UtcNow,
                    fileInfo.LastWriteTimeUtc,
                    existingRecord?.DownloadSourceId);
                await _stateStore.SaveAsync(dataFolder, state, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (File.Exists(destinationPath) && !File.Exists(sourcePath))
                {
                    File.Move(destinationPath, sourcePath, false);
                }

                throw;
            }

            return await _scanner.ScanAsync(dataFolder, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<string?> PreparePartFileAsync(
        string partPath,
        string metadataPath,
        PatchDefinition patch,
        PatchSourceDefinition source,
        Uri downloadUri,
        CancellationToken cancellationToken)
    {
        var fingerprint = GetDownloadFingerprint(source, downloadUri, patch);
        string? recordedEtag = null;
        if (File.Exists(partPath))
        {
            var metadata = File.Exists(metadataPath)
                ? await File.ReadAllLinesAsync(metadataPath, cancellationToken).ConfigureAwait(false)
                : [];
            var recordedFingerprint = metadata.FirstOrDefault();
            recordedEtag = metadata.Skip(1).FirstOrDefault();
            var maximumLength = source.IsOfficial ? patch.ExpectedSize : MaximumCustomPatchSize;
            if (!string.Equals(recordedFingerprint, fingerprint, StringComparison.Ordinal)
                || new FileInfo(partPath).Length > maximumLength
                || !source.IsOfficial && string.IsNullOrWhiteSpace(recordedEtag))
            {
                File.Delete(partPath);
                recordedEtag = null;
            }
        }

        var resumeEtag = source.IsOfficial ? patch.ETag : recordedEtag;
        await WritePartMetadataAsync(metadataPath, fingerprint, resumeEtag, cancellationToken).ConfigureAwait(false);
        return resumeEtag;
    }

    private async Task<DownloadResult> DownloadAsync(
        PatchDefinition patch,
        PatchSourceDefinition source,
        Uri downloadUri,
        string dataFolder,
        string partPath,
        string partMetadataPath,
        string? resumeEtag,
        IProgress<PatchOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existingLength = File.Exists(partPath) ? new FileInfo(partPath).Length : 0L;
        var responseEtag = source.IsOfficial ? patch.ETag : string.Empty;
        if (!source.IsOfficial || existingLength < patch.ExpectedSize)
        {
            using var response = await SendDownloadRequestAsync(
                source,
                downloadUri,
                existingLength,
                resumeEtag,
                cancellationToken).ConfigureAwait(false);
            responseEtag = response.Headers.ETag?.ToString() ?? string.Empty;
            if (source.IsOfficial && !string.Equals(responseEtag, patch.ETag, StringComparison.Ordinal))
            {
                throw new PatchOperationException("The download fingerprint does not match the approved catalog.");
            }

            if (!source.IsOfficial)
            {
                await WritePartMetadataAsync(
                    partMetadataPath,
                    GetDownloadFingerprint(source, downloadUri, patch),
                    responseEtag,
                    cancellationToken).ConfigureAwait(false);
            }

            if (response.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                var isPartial = response.StatusCode == HttpStatusCode.PartialContent;
                if (existingLength > 0 && !isPartial)
                {
                    existingLength = 0;
                }

                var totalBytes = source.IsOfficial
                    ? patch.ExpectedSize
                    : response.Content.Headers.ContentRange?.Length
                      ?? (response.Content.Headers.ContentLength is { } contentLength
                          ? existingLength + contentLength
                          : null);
                if (totalBytes is > MaximumCustomPatchSize && !source.IsOfficial)
                {
                    throw new PatchOperationException("The custom patch exceeds the 8 GB safety limit.");
                }

                if (totalBytes is > 0 && !source.IsOfficial)
                {
                    EnsureFreeSpace(dataFolder, totalBytes.Value);
                }

                var fileMode = existingLength > 0 && isPartial ? FileMode.Append : FileMode.Create;
                await using var responseStream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var destination = new FileStream(
                    partPath,
                    fileMode,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
                var stopwatch = Stopwatch.StartNew();
                var downloadedThisRun = 0L;
                try
                {
                    while (true)
                    {
                        var read = await responseStream.ReadAsync(
                            buffer.AsMemory(0, buffer.Length),
                            cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        var total = existingLength + downloadedThisRun + read;
                        var maximumLength = source.IsOfficial ? patch.ExpectedSize : MaximumCustomPatchSize;
                        if (total > maximumLength)
                        {
                            throw new PatchOperationException(source.IsOfficial
                                ? "The server returned more data than the catalog specifies."
                                : "The custom patch exceeds the 8 GB safety limit.");
                        }

                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        downloadedThisRun += read;

                        progress?.Report(new PatchOperationProgress(
                            patch.Id,
                            "Download",
                            total,
                            totalBytes,
                            downloadedThisRun / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001)));
                    }

                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        var finalLength = new FileInfo(partPath).Length;
        if (source.IsOfficial && finalLength != patch.ExpectedSize)
        {
            throw new PatchOperationException(
                $"The download is incomplete ({finalLength:N0} of {patch.ExpectedSize:N0} bytes). It can resume on the next attempt.");
        }

        progress?.Report(new PatchOperationProgress(patch.Id, "Verifying MPQ", finalLength, finalLength, 0));
        if (!await MpqValidator.HasValidSignatureAsync(partPath, cancellationToken).ConfigureAwait(false))
        {
            throw new PatchOperationException("The downloaded file is not a valid MPQ archive.");
        }

        var sha256 = await _hashService.ComputeSha256Async(partPath, cancellationToken).ConfigureAwait(false);
        if (source.IsOfficial
            && !string.IsNullOrWhiteSpace(patch.Sha256)
            && !string.Equals(sha256, patch.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new PatchOperationException("The download SHA-256 checksum is invalid.");
        }

        return new DownloadResult(sha256, responseEtag);
    }

    private async Task<HttpResponseMessage> SendDownloadRequestAsync(
        PatchSourceDefinition source,
        Uri downloadUri,
        long existingLength,
        string? resumeEtag,
        CancellationToken cancellationToken)
    {
        var currentUri = downloadUri;
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            EnsureAllowedUri(currentUri, source);
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.UserAgent.ParseAdd("OctoHD/0.1");
            if (existingLength > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingLength, null);
                if (!string.IsNullOrWhiteSpace(resumeEtag))
                {
                    request.Headers.TryAddWithoutValidation("If-Range", resumeEtag);
                }
            }

            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var finalUri = response.RequestMessage?.RequestUri ?? currentUri;
            EnsureAllowedUri(finalUri, source);

            if (!IsRedirect(response.StatusCode))
            {
                if (existingLength > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    if (!source.IsOfficial
                        && response.Content.Headers.ContentRange?.Length == existingLength)
                    {
                        return response;
                    }

                    response.Dispose();
                    throw new PatchOperationException("The partial download cannot be resumed. Please try again.");
                }

                response.EnsureSuccessStatusCode();
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
            {
                throw new PatchOperationException("The download server returned a redirect without a destination.");
            }

            currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
        }

        throw new PatchOperationException("The download was redirected too many times.");
    }

    private async Task InstallValidatedFileAsync(
        string dataFolder,
        PatchDefinition patch,
        PatchSourceDefinition source,
        string partPath,
        string partMetadataPath,
        DownloadResult download,
        bool active,
        CancellationToken cancellationToken)
    {
        var activePath = PatchFileNames.ResolveInsideDataFolder(dataFolder, patch.TargetFileName);
        var disabledPath = Path.Combine(dataFolder, patch.DisabledFileName);
        if (File.Exists(activePath) && File.Exists(disabledPath))
        {
            throw new PatchOperationException("The enabled and disabled target files both exist.");
        }

        var existingPath = File.Exists(activePath) ? activePath : File.Exists(disabledPath) ? disabledPath : null;
        var finalPath = active ? activePath : disabledPath;
        var backupDirectory = Path.Combine(JsonPatchStateStore.GetMetadataDirectory(dataFolder), "backup");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, $"{patch.TargetFileName}.{Guid.NewGuid():N}.bak");
        var movedNewFile = false;

        try
        {
            if (existingPath is not null)
            {
                File.Move(existingPath, backupPath, false);
            }

            File.Move(partPath, finalPath, false);
            movedNewFile = true;

            var state = await _stateStore.LoadAsync(dataFolder, cancellationToken).ConfigureAwait(false);
            RemoveTargetGroupRecords(state, patch.TargetFileName);
            var fileInfo = new FileInfo(finalPath);
            state.Patches[patch.Id] = new InstalledPatchRecord(
                patch.Id,
                patch.Version,
                patch.TargetFileName,
                active,
                fileInfo.Length,
                download.Sha256,
                source.IsOfficial ? patch.ETag : download.ETag,
                DateTimeOffset.UtcNow,
                fileInfo.LastWriteTimeUtc,
                source.Id);
            await _stateStore.SaveAsync(dataFolder, state, cancellationToken).ConfigureAwait(false);

            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            if (File.Exists(partMetadataPath))
            {
                File.Delete(partMetadataPath);
            }
        }
        catch (Exception exception)
        {
            try
            {
                if (movedNewFile && File.Exists(finalPath))
                {
                    File.Move(finalPath, partPath, true);
                }

                if (existingPath is not null && File.Exists(backupPath) && !File.Exists(existingPath))
                {
                    File.Move(backupPath, existingPath, false);
                }
            }
            catch (Exception rollbackException)
            {
                throw new PatchOperationException(
                    $"Installation failed and could not be rolled back completely: {rollbackException.Message}",
                    exception);
            }

            throw new PatchOperationException($"Installation failed: {exception.Message}", exception);
        }
    }

    private void EnsureDependenciesAreActive(
        PatchDefinition patch,
        IReadOnlyList<PatchScanResult> scanResults)
    {
        var missing = _dependencyService.GetMissingDependencies(patch, scanResults);
        if (missing.Count > 0)
        {
            throw new PatchOperationException($"Enable required patches first: {FormatNames(missing)}.");
        }
    }

    private void RemoveTargetGroupRecords(LocalStateDocument state, string targetFileName)
    {
        foreach (var definition in _catalog.Patches.Where(candidate =>
                     string.Equals(candidate.TargetFileName, targetFileName, StringComparison.OrdinalIgnoreCase)))
        {
            state.Patches.Remove(definition.Id);
        }
    }

    private static InstalledPatchRecord? FindRecord(LocalStateDocument state, string targetFileName) =>
        state.Patches.Values.FirstOrDefault(record =>
            string.Equals(record.TargetFileName, targetFileName, StringComparison.OrdinalIgnoreCase));

    private void EnsureCatalogPatch(PatchDefinition patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var catalogPatch = _catalog.GetById(patch.Id);
        if (catalogPatch != patch)
        {
            throw new PatchOperationException("The patch does not come from the approved catalog.");
        }

    }

    private static void EnsureAllowedUri(Uri uri, PatchSourceDefinition source)
    {
        if (uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, source.BaseUri.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Port != source.BaseUri.Port
            || !uri.AbsolutePath.StartsWith(source.BaseUri.AbsolutePath, StringComparison.Ordinal))
        {
            throw new PatchOperationException(
                $"The download URL is outside the configured source '{source.DisplayName}'.");
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Redirect
        or HttpStatusCode.RedirectMethod
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private static void EnsureDataFolder(string dataFolder)
    {
        if (!Directory.Exists(dataFolder)
            || !string.Equals(new DirectoryInfo(dataFolder).Name, "Data", StringComparison.OrdinalIgnoreCase))
        {
            throw new PatchOperationException("The configured OctoWoW Data folder is no longer available.");
        }
    }

    private static void EnsureFreeSpace(string dataFolder, long patchSize)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dataFolder));
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                var required = patchSize + 128L * 1024 * 1024;
                if (drive.AvailableFreeSpace < required)
                {
                    throw new PatchOperationException(
                        $"Not enough free space. At least {required / 1024d / 1024d / 1024d:N1} GB is required.");
                }
            }
        }
        catch (ArgumentException)
        {
            // Some mounted/Wine paths cannot be represented as DriveInfo. The write itself remains guarded.
        }
    }

    private static string FormatNames(IEnumerable<PatchDefinition> patches) =>
        string.Join(", ", patches.Select(patch =>
            patch.VariantName is null ? patch.DisplayName : $"{patch.DisplayName} ({patch.VariantName})"));

    private static string GetDownloadFingerprint(
        PatchSourceDefinition source,
        Uri downloadUri,
        PatchDefinition patch) =>
        $"{source.Id}|{downloadUri.AbsoluteUri}|{(source.IsOfficial ? patch.ETag : "custom")}";

    private static Task WritePartMetadataAsync(
        string metadataPath,
        string fingerprint,
        string? etag,
        CancellationToken cancellationToken) =>
        File.WriteAllLinesAsync(
            metadataPath,
            string.IsNullOrWhiteSpace(etag) ? [fingerprint] : [fingerprint, etag],
            cancellationToken);

    private sealed record DownloadResult(string Sha256, string ETag);
}

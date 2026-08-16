using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OctoHD.Core.Updates;

public sealed partial class SelfUpdateService(HttpClient httpClient, Assembly entryAssembly)
{
    private const long MaximumPackageSize = 300L * 1024 * 1024;
    private readonly string? _repository = ReadRepository(entryAssembly);
    private readonly string _currentVersion = ReadCurrentVersion(entryAssembly);

    public bool IsConfigured => RepositoryPattern().IsMatch(_repository ?? string.Empty);

    public string CurrentVersion => _currentVersion;

    public static SelfUpdateService Create(HttpClient httpClient) =>
        new(httpClient, Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());

    public async Task<SelfUpdateResult> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured
            || !SemanticVersion.TryParse(_currentVersion, out var currentVersion))
        {
            return new SelfUpdateResult(false, _currentVersion);
        }

        var releaseUri = new Uri($"https://api.github.com/repos/{_repository}/releases/latest");
        using var request = CreateRequest(HttpMethod.Get, releaseUri);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var releaseStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync(
            releaseStream,
            SelfUpdateJsonContext.Default.GitHubRelease,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("GitHub returned an empty release response.");

        if (!SemanticVersion.TryParse(release.TagName, out var latestVersion)
            || latestVersion.CompareTo(currentVersion) <= 0)
        {
            return new SelfUpdateResult(false, _currentVersion);
        }

        var versionText = release.TagName.Trim().TrimStart('v', 'V');
        var package = ResolvePackage(versionText);
        var asset = release.Assets.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, package.AssetName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The latest release does not contain the required package '{package.AssetName}'.");
        if (asset.Size is <= 0 or > MaximumPackageSize)
        {
            throw new InvalidOperationException("The update package has an invalid size.");
        }

        var expectedHash = ParseDigest(asset.Digest);
        var payloadPath = Path.Combine(SelfUpdateBootstrapper.UpdatesDirectory, asset.Name);
        var pending = await SelfUpdateBootstrapper.LoadPendingAsync(cancellationToken).ConfigureAwait(false);
        if (pending is not null
            && string.Equals(pending.Version, versionText, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pending.PayloadPath, payloadPath, StringComparison.Ordinal)
            && File.Exists(payloadPath)
            && await MatchesHashAsync(payloadPath, expectedHash, cancellationToken).ConfigureAwait(false))
        {
            return new SelfUpdateResult(true, _currentVersion, versionText, release.Name);
        }

        Directory.CreateDirectory(SelfUpdateBootstrapper.UpdatesDirectory);
        var partPath = $"{payloadPath}.part";
        try
        {
            await DownloadAssetAsync(
                new Uri(asset.BrowserDownloadUrl),
                partPath,
                asset.Size,
                cancellationToken).ConfigureAwait(false);
            if (!await MatchesHashAsync(partPath, expectedHash, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The downloaded update failed its SHA-256 verification.");
            }

            File.Move(partPath, payloadPath, true);
            var document = new PendingUpdateDocument(
                1,
                versionText,
                payloadPath,
                package.TargetPath,
                package.PackageKind,
                expectedHash,
                DateTimeOffset.UtcNow);
            await SelfUpdateBootstrapper.SavePendingAsync(document, cancellationToken).ConfigureAwait(false);
            return new SelfUpdateResult(true, _currentVersion, versionText, release.Name);
        }
        finally
        {
            if (File.Exists(partPath))
            {
                File.Delete(partPath);
            }
        }
    }

    public bool TryRestartToApply(out string? error) =>
        SelfUpdateBootstrapper.TryStartPendingUpdate(out error);

    internal static UpdatePackage ResolvePackage(string version)
    {
        var assetName = ResolveAssetName(version);
        return new UpdatePackage(
            assetName,
            SelfUpdateBootstrapper.ResolveCurrentTargetPath(),
            OperatingSystem.IsMacOS() ? "macos-zip" : "file");
    }

    internal static string ResolveAssetName(string version)
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("OctoHD updates support x64 and ARM64 installations.")
        };

        if (OperatingSystem.IsWindows())
        {
            return $"OctoHD-{version}-windows-{architecture}.exe";
        }

        if (OperatingSystem.IsLinux())
        {
            return $"OctoHD-{version}-linux-{architecture}.AppImage";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"OctoHD-{version}-macos-{architecture}.zip";
        }

        throw new PlatformNotSupportedException("OctoHD self-updates are not supported on this platform.");
    }

    private async Task DownloadAssetAsync(
        Uri initialUri,
        string destinationPath,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        var currentUri = initialUri;
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            EnsureGitHubUri(currentUri);
            using var request = CreateRequest(HttpMethod.Get, currentUri);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location
                    ?? throw new InvalidOperationException("The update download redirected without a destination.");
                currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                continue;
            }

            response.EnsureSuccessStatusCode();
            var responseSize = response.Content.Headers.ContentLength;
            if (responseSize is > MaximumPackageSize
                || responseSize is > 0 && responseSize != expectedSize)
            {
                throw new InvalidOperationException("The update server returned an unexpected package size.");
            }

            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[1024 * 1024];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > expectedSize || total > MaximumPackageSize)
                {
                    throw new InvalidOperationException("The update package exceeded its declared size.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (total != expectedSize)
            {
                throw new InvalidOperationException("The update package download is incomplete.");
            }

            return;
        }

        throw new InvalidOperationException("The update download was redirected too many times.");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.UserAgent.ParseAdd("OctoHD-Updater/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2026-03-10");
        return request;
    }

    private static string ParseDigest(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null
            || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || digest.Length != prefix.Length + 64
            || !digest[prefix.Length..].All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("GitHub did not provide a valid SHA-256 digest for the update.");
        }

        return digest[prefix.Length..].ToUpperInvariant();
    }

    internal static async Task<bool> MatchesHashAsync(
        string path,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureGitHubUri(Uri uri)
    {
        var allowedHost = string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        if (uri.Scheme != Uri.UriSchemeHttps || !allowedHost)
        {
            throw new InvalidOperationException("The update download left GitHub's HTTPS infrastructure.");
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Redirect
        or HttpStatusCode.RedirectMethod
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private static string? ReadRepository(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                "OctoHDUpdateRepository",
                StringComparison.Ordinal))
            ?.Value;

    private static string ReadCurrentVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        return informational?.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();

    internal sealed record UpdatePackage(string AssetName, string TargetPath, string PackageKind);
}

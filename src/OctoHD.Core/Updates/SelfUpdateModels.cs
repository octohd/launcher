using System.Text.Json.Serialization;

namespace OctoHD.Core.Updates;

public sealed record SelfUpdateResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string? LatestVersion = null,
    string? ReleaseName = null);

internal sealed record PendingUpdateDocument(
    int SchemaVersion,
    string Version,
    string PayloadPath,
    string TargetPath,
    string PackageKind,
    string Sha256,
    DateTimeOffset DownloadedAtUtc);

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("assets")] List<GitHubReleaseAsset> Assets);

internal sealed record GitHubReleaseAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("digest")] string? Digest);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(PendingUpdateDocument))]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class SelfUpdateJsonContext : JsonSerializerContext;

internal readonly record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    int Revision,
    string? Prerelease) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().TrimStart('v', 'V');
        var buildIndex = normalized.IndexOf('+', StringComparison.Ordinal);
        if (buildIndex >= 0)
        {
            normalized = normalized[..buildIndex];
        }

        string? prerelease = null;
        var prereleaseIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        if (prereleaseIndex >= 0)
        {
            prerelease = normalized[(prereleaseIndex + 1)..];
            normalized = normalized[..prereleaseIndex];
        }

        var parts = normalized.Split('.');
        var patch = 0;
        var revision = 0;
        if (parts.Length is < 2 or > 4
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || parts.Length > 2 && !int.TryParse(parts[2], out patch)
            || parts.Length > 3 && !int.TryParse(parts[3], out revision)
            || string.IsNullOrEmpty(prerelease) && prereleaseIndex >= 0)
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, revision, prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core == 0) core = Minor.CompareTo(other.Minor);
        if (core == 0) core = Patch.CompareTo(other.Patch);
        if (core == 0) core = Revision.CompareTo(other.Revision);
        if (core != 0) return core;

        if (Prerelease is null && other.Prerelease is null) return 0;
        if (Prerelease is null) return 1;
        if (other.Prerelease is null) return -1;
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            if (index >= leftParts.Length) return -1;
            if (index >= rightParts.Length) return 1;

            var leftNumeric = int.TryParse(leftParts[index], out var leftNumber);
            var rightNumeric = int.TryParse(rightParts[index], out var rightNumber);
            var comparison = (leftNumeric, rightNumeric) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.Compare(leftParts[index], rightParts[index], StringComparison.OrdinalIgnoreCase)
            };
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}

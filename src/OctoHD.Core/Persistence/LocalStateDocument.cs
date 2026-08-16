using System.Text.Json.Serialization;

namespace OctoHD.Core.Persistence;

public sealed class LocalStateDocument
{
    public int SchemaVersion { get; init; } = 1;

    public Dictionary<string, InstalledPatchRecord> Patches { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record InstalledPatchRecord(
    string PatchId,
    string SourceVersion,
    string TargetFileName,
    bool Active,
    long FileSize,
    string Sha256,
    string ETag,
    DateTimeOffset InstalledAtUtc,
    DateTimeOffset LastWriteUtc,
    string? DownloadSourceId = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(LocalStateDocument))]
internal sealed partial class LocalStateJsonContext : JsonSerializerContext;

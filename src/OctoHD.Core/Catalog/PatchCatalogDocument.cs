using System.Text.Json.Serialization;

namespace OctoHD.Core.Catalog;

internal sealed record PatchCatalogDocument(
    int SchemaVersion,
    string UpdatedAt,
    List<PatchCatalogEntry> Patches);

internal sealed record PatchCatalogEntry(
    string Id,
    string DisplayName,
    string Description,
    string Category,
    string SourceFileName,
    string TargetFileName,
    string DownloadUrl,
    string Version,
    long ExpectedSize,
    string ETag,
    string? Sha256,
    string? VariantGroup,
    string? VariantName,
    string[]? Dependencies,
    string[]? RecommendedWith,
    bool IsCore,
    bool IsHeavy);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PatchCatalogDocument))]
internal sealed partial class PatchCatalogJsonContext : JsonSerializerContext;

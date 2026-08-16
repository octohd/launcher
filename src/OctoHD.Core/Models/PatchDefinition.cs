namespace OctoHD.Core.Models;

public sealed record PatchDefinition(
    string Id,
    string DisplayName,
    string Description,
    string Category,
    string SourceFileName,
    string TargetFileName,
    Uri DownloadUri,
    string Version,
    long ExpectedSize,
    string ETag,
    string? Sha256,
    string? VariantGroup,
    string? VariantName,
    string[] Dependencies,
    string[] RecommendedWith,
    bool IsCore,
    bool IsHeavy)
{
    public string DisabledFileName => $"__octohd_{TargetFileName}";

    public string MappingLabel => $"{SourceFileName}  →  {TargetFileName}";
}

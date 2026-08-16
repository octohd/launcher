namespace OctoHD.Core.Models;

public sealed record FolderValidationResult(
    bool IsValid,
    string? NormalizedPath,
    string? Error,
    IReadOnlyList<string> Warnings);

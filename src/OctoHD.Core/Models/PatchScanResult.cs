namespace OctoHD.Core.Models;

public sealed record PatchScanResult(
    PatchDefinition Patch,
    PatchStatus Status,
    string? FilePath = null,
    long? FileSize = null,
    string? InstalledVersion = null,
    string? Message = null,
    string? InstalledSourceId = null)
{
    public bool IsInstalled => Status is not PatchStatus.NotInstalled;

    public bool IsActive => Status is PatchStatus.Active or PatchStatus.UpdateAvailableActive;

    public bool CanToggle => Status is PatchStatus.Active
        or PatchStatus.Disabled
        or PatchStatus.UpdateAvailableActive
        or PatchStatus.UpdateAvailableDisabled;
}

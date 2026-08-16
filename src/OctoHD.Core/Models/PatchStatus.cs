namespace OctoHD.Core.Models;

public enum PatchStatus
{
    NotInstalled,
    Active,
    Disabled,
    UpdateAvailableActive,
    UpdateAvailableDisabled,
    Conflict,
    ForeignFile,
    Corrupt,
    Checking,
    Busy,
    Error
}

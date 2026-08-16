using OctoHD.Core.Models;

namespace OctoHD.Core.Services;

public interface IPatchScanner
{
    Task<IReadOnlyList<PatchScanResult>> ScanAsync(
        string dataFolder,
        CancellationToken cancellationToken = default);
}

using OctoHD.Core.Catalog;
using OctoHD.Core.Models;

namespace OctoHD.Core.Services;

public sealed class PatchDependencyService(IPatchCatalog catalog)
{
    public IReadOnlyList<PatchDefinition> GetMissingDependencies(
        PatchDefinition patch,
        IReadOnlyList<PatchScanResult> scanResults)
    {
        var activeIds = scanResults
            .Where(result => result.IsActive)
            .Select(result => result.Patch.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return patch.Dependencies
            .Where(dependencyId => !activeIds.Contains(dependencyId))
            .Select(catalog.GetById)
            .ToArray();
    }

    public IReadOnlyList<PatchDefinition> GetActiveDependents(
        PatchDefinition patch,
        IReadOnlyList<PatchScanResult> scanResults)
    {
        return scanResults
            .Where(result => result.IsActive
                && result.Patch.Dependencies.Contains(patch.Id, StringComparer.OrdinalIgnoreCase))
            .Select(result => result.Patch)
            .ToArray();
    }
}

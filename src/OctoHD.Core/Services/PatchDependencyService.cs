using OctoHD.Core.Catalog;
using OctoHD.Core.Models;

namespace OctoHD.Core.Services;

public sealed class PatchDependencyService(IPatchCatalog catalog)
{
    public IReadOnlyList<PatchDefinition> GetDependenciesInInstallOrder(PatchDefinition patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var ordered = new List<PatchDefinition>();
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<PatchDefinition>();

        Resolve(patch);
        return ordered;

        void Resolve(PatchDefinition current)
        {
            resolving.Add(current.Id);
            path.Add(current);

            foreach (var dependencyId in current.Dependencies)
            {
                var dependency = GetDependency(current, dependencyId);
                if (resolving.Contains(dependency.Id))
                {
                    var cycleStart = path.FindIndex(candidate =>
                        string.Equals(candidate.Id, dependency.Id, StringComparison.OrdinalIgnoreCase));
                    var cycle = path
                        .Skip(Math.Max(cycleStart, 0))
                        .Append(dependency)
                        .Select(FormatName);
                    throw new PatchOperationException(
                        $"Patch dependency cycle detected: {string.Join(" -> ", cycle)}.");
                }

                if (resolved.Contains(dependency.Id))
                {
                    continue;
                }

                Resolve(dependency);
                resolved.Add(dependency.Id);
                ordered.Add(dependency);
            }

            path.RemoveAt(path.Count - 1);
            resolving.Remove(current.Id);
        }
    }

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

    private PatchDefinition GetDependency(PatchDefinition patch, string dependencyId)
    {
        try
        {
            return catalog.GetById(dependencyId);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            throw new PatchOperationException(
                $"Patch '{FormatName(patch)}' requires unknown patch '{dependencyId}'.",
                exception);
        }
    }

    private static string FormatName(PatchDefinition patch) =>
        patch.VariantName is null ? patch.DisplayName : $"{patch.DisplayName} ({patch.VariantName})";
}

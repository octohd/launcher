using System.Reflection;
using System.Text.Json;
using OctoHD.Core.Models;

namespace OctoHD.Core.Catalog;

public sealed class EmbeddedPatchCatalog : IPatchCatalog
{
    private const string ResourceName = "OctoHD.Core.Resources.patch-catalog.json";
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "pub-0f05631d243e4046993fc02ca7be9542.r2.dev"
    };

    private readonly Dictionary<string, PatchDefinition> _byId;

    public EmbeddedPatchCatalog()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded patch catalog '{ResourceName}' was not found.");
        var document = JsonSerializer.Deserialize(stream, PatchCatalogJsonContext.Default.PatchCatalogDocument)
            ?? throw new InvalidOperationException("The embedded patch catalog is empty.");

        if (document.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported patch catalog schema {document.SchemaVersion}.");
        }

        var patches = document.Patches.Select(ToDefinition).ToArray();
        Validate(patches);
        Patches = patches;
        _byId = patches.ToDictionary(patch => patch.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PatchDefinition> Patches { get; }

    public PatchDefinition GetById(string patchId) =>
        _byId.TryGetValue(patchId, out var patch)
            ? patch
            : throw new KeyNotFoundException($"Unknown patch id '{patchId}'.");

    private static PatchDefinition ToDefinition(PatchCatalogEntry entry)
    {
        if (!Uri.TryCreate(entry.DownloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !AllowedHosts.Contains(uri.Host))
        {
            throw new InvalidOperationException($"Patch '{entry.Id}' has a disallowed download URL.");
        }

        return new PatchDefinition(
            entry.Id,
            entry.DisplayName,
            entry.Description,
            entry.Category,
            entry.SourceFileName,
            entry.TargetFileName,
            uri,
            entry.Version,
            entry.ExpectedSize,
            entry.ETag,
            entry.Sha256,
            entry.VariantGroup,
            entry.VariantName,
            entry.Dependencies ?? [],
            entry.RecommendedWith ?? [],
            entry.IsCore,
            entry.IsHeavy);
    }

    private static void Validate(IReadOnlyList<PatchDefinition> patches)
    {
        if (patches.Count == 0)
        {
            throw new InvalidOperationException("The patch catalog contains no patches.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var patch in patches)
        {
            if (!ids.Add(patch.Id))
            {
                throw new InvalidOperationException($"Duplicate patch id '{patch.Id}'.");
            }

            PatchFileNames.ValidateManagedFileName(patch.TargetFileName);
            if (patch.ExpectedSize <= 0)
            {
                throw new InvalidOperationException($"Patch '{patch.Id}' has no valid expected size.");
            }
        }

        foreach (var patch in patches)
        {
            foreach (var dependency in patch.Dependencies)
            {
                if (!ids.Contains(dependency))
                {
                    throw new InvalidOperationException($"Patch '{patch.Id}' references unknown dependency '{dependency}'.");
                }
            }
        }

        foreach (var targetGroup in patches.GroupBy(patch => patch.TargetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (targetGroup.Count() > 1 && targetGroup.Any(patch => string.IsNullOrWhiteSpace(patch.VariantGroup)))
            {
                throw new InvalidOperationException($"Target '{targetGroup.Key}' is shared without a variant group.");
            }
        }
    }
}

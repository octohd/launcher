using OctoHD.Core.Models;

namespace OctoHD.Core.Catalog;

public interface IPatchCatalog
{
    IReadOnlyList<PatchDefinition> Patches { get; }

    PatchDefinition GetById(string patchId);
}

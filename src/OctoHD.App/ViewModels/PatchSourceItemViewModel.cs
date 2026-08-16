using OctoHD.Core.Models;

namespace OctoHD.App.ViewModels;

public sealed class PatchSourceItemViewModel(PatchSourceDefinition definition)
{
    public PatchSourceDefinition Definition { get; } = definition;

    public string Id => Definition.Id;

    public string DisplayName => Definition.DisplayName;

    public string BaseUrl => Definition.BaseUri.AbsoluteUri;

    public bool IsOfficial => Definition.IsOfficial;

    public string DetailText => IsOfficial
        ? "Direct and catalog verified"
        : "Custom HTTPS bucket · MPQ checked";

    public override string ToString() => DisplayName;
}

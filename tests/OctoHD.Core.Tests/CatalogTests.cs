using OctoHD.Core.Catalog;

namespace OctoHD.Core.Tests;

public sealed class CatalogTests
{
    private readonly EmbeddedPatchCatalog _catalog = new();

    [Fact]
    public void Catalog_contains_all_current_turtle_modules_and_variants()
    {
        Assert.Equal(16, _catalog.Patches.Count);
        Assert.Contains(_catalog.Patches, patch => patch.Id == "l-regular");
        Assert.Contains(_catalog.Patches, patch => patch.Id == "l-less-thicc");
        Assert.Contains(_catalog.Patches, patch => patch.Id == "t-standard");
        Assert.Contains(_catalog.Patches, patch => patch.Id == "t-ultra-base");
    }

    [Fact]
    public void Every_patch_is_shifted_by_exactly_one_letter()
    {
        foreach (var patch in _catalog.Patches)
        {
            var sourceLetter = patch.SourceFileName[6];
            var targetLetter = patch.TargetFileName[6];
            Assert.Equal(sourceLetter + 1, targetLetter);
        }
    }

    [Fact]
    public void Payloads_are_downloaded_directly_from_the_project_reforged_cdn()
    {
        Assert.All(_catalog.Patches, patch =>
        {
            Assert.Equal(Uri.UriSchemeHttps, patch.DownloadUri.Scheme);
            Assert.Equal("pub-0f05631d243e4046993fc02ca7be9542.r2.dev", patch.DownloadUri.Host);
        });
    }
}

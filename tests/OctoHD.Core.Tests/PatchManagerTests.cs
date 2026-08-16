using OctoHD.Core.Models;
using OctoHD.Core.Persistence;
using OctoHD.Core.Services;

namespace OctoHD.Core.Tests;

public sealed class PatchManagerTests
{
    [Fact]
    public async Task Install_downloads_validates_and_renames_patch()
    {
        using var folder = new TemporaryDataFolder();
        var patch = TestPatches.Create();
        var handler = new StaticResponseHandler(TestPatches.MpqBytes());
        using var httpClient = new HttpClient(handler);
        var manager = CreateManager(new TestCatalog(patch), httpClient);

        var results = await manager.InstallAsync(folder.DataPath, patch);

        Assert.True(File.Exists(Path.Combine(folder.DataPath, "patch-B.mpq")));
        Assert.False(File.Exists(Path.Combine(folder.DataPath, "patch-A.mpq")));
        Assert.Equal(PatchStatus.Active, Assert.Single(results).Status);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Toggle_renames_without_a_second_download()
    {
        using var folder = new TemporaryDataFolder();
        var patch = TestPatches.Create();
        var handler = new StaticResponseHandler(TestPatches.MpqBytes());
        using var httpClient = new HttpClient(handler);
        var manager = CreateManager(new TestCatalog(patch), httpClient);
        await manager.InstallAsync(folder.DataPath, patch);

        var disabled = await manager.SetEnabledAsync(folder.DataPath, patch, false);
        var enabled = await manager.SetEnabledAsync(folder.DataPath, patch, true);

        Assert.Equal(PatchStatus.Disabled, Assert.Single(disabled).Status);
        Assert.Equal(PatchStatus.Active, Assert.Single(enabled).Status);
        Assert.True(File.Exists(Path.Combine(folder.DataPath, "patch-B.mpq")));
        Assert.False(File.Exists(Path.Combine(folder.DataPath, "__octohd_patch-B.mpq")));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Missing_dependency_blocks_download()
    {
        using var folder = new TemporaryDataFolder();
        var basis = TestPatches.Create("base", "patch-A.mpq", "patch-B.mpq");
        var dependent = TestPatches.Create("dependent", "patch-C.mpq", "patch-D.mpq", dependencies: [basis.Id]);
        var catalog = new TestCatalog(basis, dependent);
        var handler = new StaticResponseHandler(TestPatches.MpqBytes());
        using var httpClient = new HttpClient(handler);
        var manager = CreateManager(catalog, httpClient);

        var exception = await Assert.ThrowsAsync<PatchOperationException>(() =>
            manager.InstallAsync(folder.DataPath, dependent));

        Assert.Contains("Enable required patches first", exception.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Custom_source_resolves_filename_and_records_source()
    {
        using var folder = new TemporaryDataFolder();
        var patch = TestPatches.Create();
        var catalog = new TestCatalog(patch);
        var handler = new StaticResponseHandler(TestPatches.MpqBytes(12), "\"bucket-etag\"");
        using var httpClient = new HttpClient(handler);
        var manager = CreateManager(catalog, httpClient);
        var source = new PatchSourceDefinition(
            "custom-test",
            "Test bucket",
            new Uri("https://bucket.example/hd/"));

        var results = await manager.InstallAsync(folder.DataPath, patch, source: source);
        var state = await new JsonPatchStateStore().LoadAsync(folder.DataPath);

        Assert.Equal(new Uri("https://bucket.example/hd/patch-A.mpq"), handler.LastRequestUri);
        Assert.Equal("custom-test", state.Patches[patch.Id].DownloadSourceId);
        Assert.Equal(12, state.Patches[patch.Id].FileSize);
        Assert.Equal("\"bucket-etag\"", state.Patches[patch.Id].ETag);
        Assert.Equal(PatchStatus.Active, Assert.Single(results).Status);
    }

    [Fact]
    public void Patch_source_requires_safe_https_base_url()
    {
        Assert.Throws<ArgumentException>(() => new PatchSourceDefinition(
            "insecure",
            "Insecure",
            new Uri("http://bucket.example/")));
        Assert.Throws<ArgumentException>(() => new PatchSourceDefinition(
            "credentialed",
            "Credentialed",
            new Uri("https://user:secret@bucket.example/")));
    }

    private static PatchManager CreateManager(TestCatalog catalog, HttpClient httpClient)
    {
        var stateStore = new JsonPatchStateStore();
        var scanner = new PatchScanner(catalog, stateStore);
        return new PatchManager(
            catalog,
            stateStore,
            scanner,
            new PatchDependencyService(catalog),
            new FileHashService(),
            httpClient);
    }
}

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

        var results = await manager.InstallAsync(
            folder.DataPath,
            patch,
            cancellationToken: TestContext.Current.CancellationToken);

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
        await manager.InstallAsync(
            folder.DataPath,
            patch,
            cancellationToken: TestContext.Current.CancellationToken);

        var disabled = await manager.SetEnabledAsync(
            folder.DataPath,
            patch,
            false,
            TestContext.Current.CancellationToken);
        var enabled = await manager.SetEnabledAsync(
            folder.DataPath,
            patch,
            true,
            TestContext.Current.CancellationToken);

        Assert.Equal(PatchStatus.Disabled, Assert.Single(disabled).Status);
        Assert.Equal(PatchStatus.Active, Assert.Single(enabled).Status);
        Assert.True(File.Exists(Path.Combine(folder.DataPath, "patch-B.mpq")));
        Assert.False(File.Exists(Path.Combine(folder.DataPath, "__octohd_patch-B.mpq")));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Install_downloads_transitive_dependencies_in_topological_order()
    {
        using var folder = new TemporaryDataFolder();
        var basis = TestPatches.Create("base", "base-source.mpq", "patch-B.mpq");
        var middle = TestPatches.Create(
            "middle",
            "middle-source.mpq",
            "patch-C.mpq",
            dependencies: [basis.Id]);
        var requested = TestPatches.Create(
            "requested",
            "requested-source.mpq",
            "patch-D.mpq",
            dependencies: [middle.Id]);
        var catalog = new TestCatalog(requested, middle, basis);
        var handler = new StaticResponseHandler(TestPatches.MpqBytes());
        using var httpClient = new HttpClient(handler);
        var manager = CreateManager(catalog, httpClient);

        var results = await manager.InstallAsync(
            folder.DataPath,
            requested,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            [basis.DownloadUri, middle.DownloadUri, requested.DownloadUri],
            handler.RequestUris);
        Assert.All(results, result => Assert.Equal(PatchStatus.Active, result.Status));
        Assert.True(File.Exists(Path.Combine(folder.DataPath, basis.TargetFileName)));
        Assert.True(File.Exists(Path.Combine(folder.DataPath, middle.TargetFileName)));
        Assert.True(File.Exists(Path.Combine(folder.DataPath, requested.TargetFileName)));
    }

    [Fact]
    public async Task Install_downloads_shared_dependency_only_once()
    {
        using var folder = new TemporaryDataFolder();
        var shared = TestPatches.Create("shared", "shared-source.mpq", "patch-E.mpq");
        var left = TestPatches.Create(
            "left",
            "left-source.mpq",
            "patch-F.mpq",
            dependencies: [shared.Id]);
        var right = TestPatches.Create(
            "right",
            "right-source.mpq",
            "patch-G.mpq",
            dependencies: [shared.Id]);
        var requested = TestPatches.Create(
            "requested",
            "requested-source.mpq",
            "patch-H.mpq",
            dependencies: [left.Id, right.Id]);
        var handler = new StaticResponseHandler(TestPatches.MpqBytes());
        using var httpClient = new HttpClient(handler);
        var manager = CreateManager(new TestCatalog(requested, right, shared, left), httpClient);

        var results = await manager.InstallAsync(
            folder.DataPath,
            requested,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            [shared.DownloadUri, left.DownloadUri, right.DownloadUri, requested.DownloadUri],
            handler.RequestUris);
        Assert.Equal(4, handler.RequestCount);
        Assert.All(results, result => Assert.Equal(PatchStatus.Active, result.Status));
    }

    [Fact]
    public async Task Install_reenables_disabled_dependency_without_downloading_it_again()
    {
        using var folder = new TemporaryDataFolder();
        var dependency = TestPatches.Create(
            "dependency",
            "dependency-source.mpq",
            "patch-I.mpq");
        var requested = TestPatches.Create(
            "requested",
            "requested-source.mpq",
            "patch-J.mpq",
            dependencies: [dependency.Id]);
        var handler = new StaticResponseHandler(TestPatches.MpqBytes());
        using var httpClient = new HttpClient(handler);
        var manager = CreateManager(new TestCatalog(dependency, requested), httpClient);
        await manager.InstallAsync(
            folder.DataPath,
            dependency,
            cancellationToken: TestContext.Current.CancellationToken);
        await manager.SetEnabledAsync(
            folder.DataPath,
            dependency,
            false,
            TestContext.Current.CancellationToken);

        var results = await manager.InstallAsync(
            folder.DataPath,
            requested,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([dependency.DownloadUri, requested.DownloadUri], handler.RequestUris);
        Assert.Equal(PatchStatus.Active, results.Single(result => result.Patch.Id == dependency.Id).Status);
        Assert.Equal(PatchStatus.Active, results.Single(result => result.Patch.Id == requested.Id).Status);
        Assert.True(File.Exists(Path.Combine(folder.DataPath, dependency.TargetFileName)));
        Assert.False(File.Exists(Path.Combine(folder.DataPath, dependency.DisabledFileName)));
    }

    [Fact]
    public async Task Dependency_cycle_is_rejected_before_any_download()
    {
        using var folder = new TemporaryDataFolder();
        var first = TestPatches.Create(
            "first",
            "first-source.mpq",
            "patch-K.mpq",
            dependencies: ["second"]);
        var second = TestPatches.Create(
            "second",
            "second-source.mpq",
            "patch-L.mpq",
            dependencies: [first.Id]);
        var handler = new StaticResponseHandler(TestPatches.MpqBytes());
        using var httpClient = new HttpClient(handler);
        var manager = CreateManager(new TestCatalog(first, second), httpClient);

        var exception = await Assert.ThrowsAsync<PatchOperationException>(() =>
            manager.InstallAsync(
                folder.DataPath,
                first,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("dependency cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task Requested_patch_is_preflighted_before_dependencies_are_changed()
    {
        using var folder = new TemporaryDataFolder();
        var dependency = TestPatches.Create(
            "dependency",
            "dependency-source.mpq",
            "patch-O.mpq");
        var requested = TestPatches.Create(
            "requested",
            "requested-source.mpq",
            "patch-P.mpq",
            dependencies: [dependency.Id]);
        folder.WriteMpq(requested.TargetFileName, 12);
        var handler = new StaticResponseHandler(TestPatches.MpqBytes());
        using var httpClient = new HttpClient(handler);
        var manager = CreateManager(new TestCatalog(dependency, requested), httpClient);

        await Assert.ThrowsAsync<PatchOperationException>(() =>
            manager.InstallAsync(
                folder.DataPath,
                requested,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(handler.RequestUris);
        Assert.False(File.Exists(Path.Combine(folder.DataPath, dependency.TargetFileName)));
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

        var results = await manager.InstallAsync(
            folder.DataPath,
            patch,
            source: source,
            cancellationToken: TestContext.Current.CancellationToken);
        var state = await new JsonPatchStateStore().LoadAsync(
            folder.DataPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://bucket.example/hd/patch-A.mpq"), handler.LastRequestUri);
        Assert.Equal("custom-test", state.Patches[patch.Id].DownloadSourceId);
        Assert.Equal(12, state.Patches[patch.Id].FileSize);
        Assert.Equal("\"bucket-etag\"", state.Patches[patch.Id].ETag);
        Assert.Equal(PatchStatus.Active, Assert.Single(results).Status);
    }

    [Fact]
    public async Task Dependencies_use_the_selected_custom_source()
    {
        using var folder = new TemporaryDataFolder();
        var dependency = TestPatches.Create(
            "dependency",
            "dependency-source.mpq",
            "patch-M.mpq");
        var requested = TestPatches.Create(
            "requested",
            "requested-source.mpq",
            "patch-N.mpq",
            dependencies: [dependency.Id]);
        var handler = new StaticResponseHandler(TestPatches.MpqBytes(12), "\"bucket-etag\"");
        using var httpClient = new HttpClient(handler);
        var manager = CreateManager(new TestCatalog(dependency, requested), httpClient);
        var source = new PatchSourceDefinition(
            "custom-test",
            "Test bucket",
            new Uri("https://bucket.example/hd/"));

        var results = await manager.InstallAsync(
            folder.DataPath,
            requested,
            source: source,
            cancellationToken: TestContext.Current.CancellationToken);
        var state = await new JsonPatchStateStore().LoadAsync(
            folder.DataPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new Uri("https://bucket.example/hd/dependency-source.mpq"),
                new Uri("https://bucket.example/hd/requested-source.mpq")
            ],
            handler.RequestUris);
        Assert.All(results, result => Assert.Equal(PatchStatus.Active, result.Status));
        Assert.Equal("custom-test", state.Patches[dependency.Id].DownloadSourceId);
        Assert.Equal("custom-test", state.Patches[requested.Id].DownloadSourceId);
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

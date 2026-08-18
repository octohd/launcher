using System.Net;
using OctoHD.App.Infrastructure;
using OctoHD.App.ViewModels;
using OctoHD.Core.Catalog;
using OctoHD.Core.Models;
using OctoHD.Core.Persistence;
using OctoHD.Core.Services;
using OctoHD.Core.Updates;

namespace OctoHD.App.Tests;

internal sealed class TemporaryInstallation : IDisposable
{
    public TemporaryInstallation()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "OctoHD.App.Tests", Guid.NewGuid().ToString("N"));
        DataPath = Path.Combine(RootPath, "Data");
        SettingsPath = Path.Combine(RootPath, "settings.json");
        Directory.CreateDirectory(DataPath);
    }

    public string RootPath { get; }

    public string DataPath { get; }

    public string SettingsPath { get; }

    public void AddGameExecutable() => File.WriteAllText(Path.Combine(RootPath, "OctoWoW.exe"), string.Empty);

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, true);
        }
    }
}

internal sealed class TestCatalog(params PatchDefinition[] patches) : IPatchCatalog
{
    public IReadOnlyList<PatchDefinition> Patches { get; } = patches;

    public PatchDefinition GetById(string patchId) =>
        Patches.Single(patch => string.Equals(patch.Id, patchId, StringComparison.OrdinalIgnoreCase));
}

internal sealed class StubScanner : IPatchScanner
{
    public IReadOnlyList<PatchScanResult> Results { get; set; } = [];

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<PatchScanResult>> ScanAsync(
        string dataFolder,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(Results);
    }
}

internal sealed class InMemoryStateStore : IPatchStateStore
{
    public Task<LocalStateDocument> LoadAsync(
        string dataFolder,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new LocalStateDocument());

    public Task SaveAsync(
        string dataFolder,
        LocalStateDocument state,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

internal sealed class UnexpectedHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
}

internal static class AppTestFactory
{
    public static PatchDefinition Patch(
        string id = "patch-a",
        string category = "Textures",
        long size = 1536,
        string? variantName = null,
        string[]? dependencies = null,
        bool isCore = false,
        bool isHeavy = false) =>
        new(
            id,
            $"Patch {id}",
            $"Description for {id}",
            category,
            $"source-{id}.mpq",
            $"target-{id}.mpq",
            new Uri($"https://example.test/patches/source-{id}.mpq"),
            "1.2.3",
            size,
            "\"etag\"",
            null,
            variantName is null ? null : "quality",
            variantName,
            dependencies ?? [],
            [],
            isCore,
            isHeavy);

    public static MainWindowViewModel ViewModel(
        TestCatalog catalog,
        StubScanner scanner,
        string settingsPath)
    {
        var stateStore = new InMemoryStateStore();
        var httpClient = new HttpClient(new UnexpectedHttpHandler());
        var manager = new PatchManager(
            catalog,
            stateStore,
            scanner,
            new PatchDependencyService(catalog),
            new FileHashService(),
            httpClient);
        return new MainWindowViewModel(
            catalog,
            scanner,
            manager,
            new DataFolderValidator(),
            new AppSettingsStore(settingsPath),
            new GameLauncher(),
            new SelfUpdateService(httpClient, typeof(AppTestFactory).Assembly));
    }

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}

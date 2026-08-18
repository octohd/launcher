using OctoHD.Core.Models;
using OctoHD.Core.Persistence;
using OctoHD.Core.Services;

namespace OctoHD.Core.Tests;

public sealed class PatchScannerTests
{
    [Fact]
    public async Task Empty_folder_reports_not_installed()
    {
        using var folder = new TemporaryDataFolder();
        var patch = TestPatches.Create();
        var scanner = CreateScanner(patch);

        var result = Assert.Single(await scanner.ScanAsync(
            folder.DataPath,
            TestContext.Current.CancellationToken));

        Assert.Equal(PatchStatus.NotInstalled, result.Status);
    }

    [Theory]
    [InlineData("patch-B.mpq", PatchStatus.Active)]
    [InlineData("__octohd_patch-B.mpq", PatchStatus.Disabled)]
    public async Task Known_size_is_recognized_without_state_file(string fileName, PatchStatus expected)
    {
        using var folder = new TemporaryDataFolder();
        folder.WriteMpq(fileName);
        var patch = TestPatches.Create();
        var scanner = CreateScanner(patch);

        var result = Assert.Single(await scanner.ScanAsync(
            folder.DataPath,
            TestContext.Current.CancellationToken));

        Assert.Equal(expected, result.Status);
        Assert.Contains("file fingerprint", result.Message);
    }

    [Fact]
    public async Task Active_and_disabled_files_are_a_conflict()
    {
        using var folder = new TemporaryDataFolder();
        folder.WriteMpq("patch-B.mpq");
        folder.WriteMpq("__octohd_patch-B.mpq");
        var scanner = CreateScanner(TestPatches.Create());

        var result = Assert.Single(await scanner.ScanAsync(
            folder.DataPath,
            TestContext.Current.CancellationToken));

        Assert.Equal(PatchStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Unknown_size_is_never_adopted_silently()
    {
        using var folder = new TemporaryDataFolder();
        folder.WriteMpq("patch-B.mpq", 12);
        var scanner = CreateScanner(TestPatches.Create());

        var result = Assert.Single(await scanner.ScanAsync(
            folder.DataPath,
            TestContext.Current.CancellationToken));

        Assert.Equal(PatchStatus.ForeignFile, result.Status);
    }

    [Fact]
    public async Task State_with_old_version_reports_update()
    {
        using var folder = new TemporaryDataFolder();
        folder.WriteMpq("patch-B.mpq");
        var patch = TestPatches.Create();
        var stateStore = new JsonPatchStateStore();
        var state = new LocalStateDocument();
        state.Patches[patch.Id] = new InstalledPatchRecord(
            patch.Id,
            "0.9.0",
            patch.TargetFileName,
            true,
            8,
            "hash",
            "\"old\"",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        await stateStore.SaveAsync(
            folder.DataPath,
            state,
            TestContext.Current.CancellationToken);
        var scanner = new PatchScanner(new TestCatalog(patch), stateStore);

        var result = Assert.Single(await scanner.ScanAsync(
            folder.DataPath,
            TestContext.Current.CancellationToken));

        Assert.Equal(PatchStatus.UpdateAvailableActive, result.Status);
    }

    [Fact]
    public async Task Externally_changed_managed_file_is_reported_as_corrupt()
    {
        using var folder = new TemporaryDataFolder();
        folder.WriteMpq("patch-B.mpq");
        var patch = TestPatches.Create();
        var stateStore = new JsonPatchStateStore();
        var hashService = new FileHashService();
        var patchPath = Path.Combine(folder.DataPath, "patch-B.mpq");
        var originalHash = await hashService.ComputeSha256Async(
            patchPath,
            TestContext.Current.CancellationToken);
        var state = new LocalStateDocument();
        state.Patches[patch.Id] = new InstalledPatchRecord(
            patch.Id,
            patch.Version,
            patch.TargetFileName,
            true,
            8,
            originalHash,
            patch.ETag,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(-1));
        await stateStore.SaveAsync(
            folder.DataPath,
            state,
            TestContext.Current.CancellationToken);
        var changed = TestPatches.MpqBytes();
        changed[^1] = 0x7F;
        await File.WriteAllBytesAsync(
            patchPath,
            changed,
            TestContext.Current.CancellationToken);
        var scanner = new PatchScanner(new TestCatalog(patch), stateStore, hashService);

        var result = Assert.Single(await scanner.ScanAsync(
            folder.DataPath,
            TestContext.Current.CancellationToken));

        Assert.Equal(PatchStatus.Corrupt, result.Status);
    }

    [Fact]
    public async Task Managed_file_with_changed_size_is_reported_as_corrupt()
    {
        using var folder = new TemporaryDataFolder();
        folder.WriteMpq("patch-B.mpq", 12);
        var patch = TestPatches.Create();
        var stateStore = new JsonPatchStateStore();
        var state = new LocalStateDocument();
        state.Patches[patch.Id] = new InstalledPatchRecord(
            patch.Id,
            patch.Version,
            patch.TargetFileName,
            true,
            8,
            "recorded-hash",
            patch.ETag,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(-1));
        await stateStore.SaveAsync(
            folder.DataPath,
            state,
            TestContext.Current.CancellationToken);
        var scanner = new PatchScanner(new TestCatalog(patch), stateStore);

        var result = Assert.Single(await scanner.ScanAsync(
            folder.DataPath,
            TestContext.Current.CancellationToken));

        Assert.Equal(PatchStatus.Corrupt, result.Status);
    }

    private static PatchScanner CreateScanner(params OctoHD.Core.Models.PatchDefinition[] patches) =>
        new(new TestCatalog(patches), new JsonPatchStateStore());
}

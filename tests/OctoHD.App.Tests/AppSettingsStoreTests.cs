using OctoHD.App.Infrastructure;

namespace OctoHD.App.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public async Task Missing_settings_return_defaults()
    {
        using var installation = new TemporaryInstallation();
        var store = new AppSettingsStore(installation.SettingsPath);

        var settings = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Null(settings.DataFolder);
        Assert.Null(settings.SelectedPatchSourceId);
        Assert.Empty(settings.PatchSources);
    }

    [Fact]
    public async Task Settings_round_trip_through_json()
    {
        using var installation = new TemporaryInstallation();
        var store = new AppSettingsStore(installation.SettingsPath);
        var expected = new AppSettings
        {
            DataFolder = installation.DataPath,
            SelectedPatchSourceId = "custom-one",
            PatchSources =
            [
                new CustomPatchSourceSettings("custom-one", "Custom One", "https://example.test/patches/")
            ]
        };

        await store.SaveAsync(expected, TestContext.Current.CancellationToken);
        var actual = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected.DataFolder, actual.DataFolder);
        Assert.Equal(expected.SelectedPatchSourceId, actual.SelectedPatchSourceId);
        var source = Assert.Single(actual.PatchSources);
        Assert.Equal(expected.PatchSources[0], source);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(installation.RootPath),
            path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Malformed_settings_are_ignored()
    {
        using var installation = new TemporaryInstallation();
        await File.WriteAllTextAsync(
            installation.SettingsPath,
            "{not-json",
            TestContext.Current.CancellationToken);
        var store = new AppSettingsStore(installation.SettingsPath);

        var settings = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(settings.PatchSources);
    }
}

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
        Assert.False(settings.IsListView);
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
            IsListView = true,
            PatchSources =
            [
                new CustomPatchSourceSettings("custom-one", "Custom One", "https://example.test/patches/")
            ]
        };

        await store.SaveAsync(expected, TestContext.Current.CancellationToken);
        var actual = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected.DataFolder, actual.DataFolder);
        Assert.Equal(expected.SelectedPatchSourceId, actual.SelectedPatchSourceId);
        Assert.Equal(expected.IsListView, actual.IsListView);
        var source = Assert.Single(actual.PatchSources);
        Assert.Equal(expected.PatchSources[0], source);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(installation.RootPath),
            path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Legacy_settings_without_view_mode_default_to_cards()
    {
        using var installation = new TemporaryInstallation();
        await File.WriteAllTextAsync(
            installation.SettingsPath,
            """
            {
              "selectedPatchSourceId": "project-reforged",
              "patchSources": []
            }
            """,
            TestContext.Current.CancellationToken);
        var store = new AppSettingsStore(installation.SettingsPath);

        var settings = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(settings.IsListView);
        Assert.Equal("project-reforged", settings.SelectedPatchSourceId);
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

        Assert.False(settings.IsListView);
        Assert.Empty(settings.PatchSources);
    }
}

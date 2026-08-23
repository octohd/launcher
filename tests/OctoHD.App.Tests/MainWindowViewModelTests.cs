using OctoHD.App.Infrastructure;
using OctoHD.App.ViewModels;
using OctoHD.Core.Models;

namespace OctoHD.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task Patch_library_defaults_to_cards_and_can_switch_views()
    {
        using var installation = new TemporaryInstallation();
        var viewModel = AppTestFactory.ViewModel(
            new TestCatalog(AppTestFactory.Patch()),
            new StubScanner(),
            installation.SettingsPath);
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => notifications.Add(eventArgs.PropertyName);

        Assert.True(viewModel.IsCardView);
        Assert.False(viewModel.IsListView);

        await viewModel.SetPatchViewModeAsync(true);
        await viewModel.SetPatchViewModeAsync(true);

        Assert.False(viewModel.IsCardView);
        Assert.True(viewModel.IsListView);

        await viewModel.SetPatchViewModeAsync(false);

        Assert.True(viewModel.IsCardView);
        Assert.False(viewModel.IsListView);
        Assert.Equal(
            [
                nameof(MainWindowViewModel.IsListView),
                nameof(MainWindowViewModel.IsCardView),
                nameof(MainWindowViewModel.IsListView),
                nameof(MainWindowViewModel.IsCardView)
            ],
            notifications);
    }

    [Fact]
    public async Task Patch_library_view_mode_is_persisted_and_restored()
    {
        using var installation = new TemporaryInstallation();
        var catalog = new TestCatalog(AppTestFactory.Patch());
        var firstViewModel = AppTestFactory.ViewModel(
            catalog,
            new StubScanner(),
            installation.SettingsPath);
        await firstViewModel.InitializeAsync();

        await firstViewModel.SetPatchViewModeAsync(true);

        var saved = await new AppSettingsStore(installation.SettingsPath)
            .LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(saved.IsListView);

        var restoredViewModel = AppTestFactory.ViewModel(
            catalog,
            new StubScanner(),
            installation.SettingsPath);
        await restoredViewModel.InitializeAsync();

        Assert.True(restoredViewModel.IsListView);
        Assert.False(restoredViewModel.IsCardView);

        await restoredViewModel.SetPatchViewModeAsync(false);
        saved = await new AppSettingsStore(installation.SettingsPath)
            .LoadAsync(TestContext.Current.CancellationToken);
        Assert.False(saved.IsListView);
    }

    [Fact]
    public async Task View_model_exposes_summary_filters_and_source_state()
    {
        using var installation = new TemporaryInstallation();
        var basePatch = AppTestFactory.Patch("base", "Core", isCore: true);
        var updatePatch = AppTestFactory.Patch("update", "Textures", dependencies: [basePatch.Id]);
        var missingPatch = AppTestFactory.Patch("missing", "Audio");
        var catalog = new TestCatalog(basePatch, updatePatch, missingPatch);
        var viewModel = AppTestFactory.ViewModel(catalog, new StubScanner(), installation.SettingsPath);

        Assert.Equal(3, viewModel.Patches.Count);
        Assert.Equal(3, viewModel.VisiblePatches.Count);
        Assert.Equal(4, viewModel.FilterOptions.Count);
        Assert.NotEmpty(viewModel.ChangelogEntries);
        Assert.StartsWith("OCTOHD  v", viewModel.AppVersionText);
        Assert.Equal("No OctoWoW folder selected", viewModel.DataFolder);
        Assert.False(viewModel.HasDataFolder);
        Assert.True(viewModel.IsInstallationInvalid);
        Assert.Equal("No installation connected", viewModel.SummaryText);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.UpdateAllCommand.CanExecute(null));
        Assert.False(viewModel.LaunchCommand.CanExecute(null));
        Assert.False(viewModel.IsChangelogOpen);
        Assert.True(viewModel.IsMainContentEnabled);
        Assert.True(viewModel.OpenChangelogCommand.CanExecute(null));
        Assert.False(viewModel.CloseChangelogCommand.CanExecute(null));

        viewModel.OpenChangelogCommand.Execute(null);
        Assert.True(viewModel.IsChangelogOpen);
        Assert.False(viewModel.IsMainContentEnabled);
        Assert.False(viewModel.OpenChangelogCommand.CanExecute(null));
        Assert.True(viewModel.CloseChangelogCommand.CanExecute(null));

        viewModel.CloseChangelogCommand.Execute(null);
        Assert.False(viewModel.IsChangelogOpen);
        Assert.True(viewModel.IsMainContentEnabled);

        viewModel.Patches.Single(item => item.Definition.Id == basePatch.Id)
            .ApplyScanResult(new PatchScanResult(basePatch, PatchStatus.Active));
        viewModel.Patches.Single(item => item.Definition.Id == updatePatch.Id)
            .ApplyScanResult(new PatchScanResult(updatePatch, PatchStatus.UpdateAvailableDisabled));
        viewModel.Patches.Single(item => item.Definition.Id == missingPatch.Id)
            .ApplyScanResult(new PatchScanResult(missingPatch, PatchStatus.NotInstalled));

        Assert.Equal(1, viewModel.ActiveCount);
        Assert.Equal(2, viewModel.InstalledCount);
        Assert.Equal(1, viewModel.UpdateCount);
        Assert.Contains("Requires: Patch base", viewModel.Patches[1].DependencyText);

        viewModel.SelectedFilter = "Installed";
        Assert.Equal(2, viewModel.VisiblePatches.Count);
        viewModel.SelectedFilter = "Updates";
        Assert.Equal(updatePatch.Id, Assert.Single(viewModel.VisiblePatches).Definition.Id);
        viewModel.SelectedFilter = "Not installed";
        Assert.Equal(missingPatch.Id, Assert.Single(viewModel.VisiblePatches).Definition.Id);
        viewModel.SelectedFilter = "All patches";
        viewModel.SearchText = "audio";
        Assert.Equal(missingPatch.Id, Assert.Single(viewModel.VisiblePatches).Definition.Id);
        viewModel.SearchText = "description for base";
        Assert.Equal(basePatch.Id, Assert.Single(viewModel.VisiblePatches).Definition.Id);
        viewModel.SearchText = string.Empty;

        var customSource = new PatchSourceItemViewModel(new PatchSourceDefinition(
            "community",
            "Community",
            new Uri("https://cdn.example.test/patches")));
        viewModel.SelectedPatchSource = customSource;
        Assert.True(viewModel.CanRemovePatchSource);
        Assert.Equal(customSource.DetailText, viewModel.PatchSourceDetail);
        Assert.All(viewModel.Patches, patch => Assert.Equal("COMMUNITY", patch.SourceLabel));
        viewModel.SelectedPatchSource = null!;
        Assert.Same(customSource, viewModel.SelectedPatchSource);

        await viewModel.PersistSelectedPatchSourceAsync();
        viewModel.ReportSettingsError("read-only");
        Assert.Contains("read-only", viewModel.StatusMessage);
        Assert.True(viewModel.HasStatusMessage);
        viewModel.ReportExternalLinkError("blocked");
        Assert.Contains("blocked", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Selecting_a_valid_installation_scans_and_persists_it()
    {
        using var installation = new TemporaryInstallation();
        installation.AddGameExecutable();
        var patch = AppTestFactory.Patch();
        var catalog = new TestCatalog(patch);
        var scanner = new StubScanner
        {
            Results = [new PatchScanResult(patch, PatchStatus.Active)]
        };
        var viewModel = AppTestFactory.ViewModel(catalog, scanner, installation.SettingsPath);

        await viewModel.SetDataFolderAsync(installation.RootPath);

        Assert.True(viewModel.IsInstallationValid);
        Assert.True(viewModel.HasDataFolder);
        Assert.False(viewModel.IsInstallationInvalid);
        Assert.Equal(installation.RootPath, viewModel.DataFolder);
        Assert.Equal(1, scanner.CallCount);
        Assert.Equal(1, viewModel.ActiveCount);
        Assert.Equal("1 active  ·  1 installed  ·  0 updates", viewModel.SummaryText);
        Assert.False(viewModel.HasStatusMessage);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        Assert.True(viewModel.LaunchCommand.CanExecute(null));

        var saved = await new AppSettingsStore(installation.SettingsPath)
            .LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(installation.DataPath, saved.DataFolder);

        viewModel.RefreshCommand.Execute(null);
        await AppTestFactory.WaitUntilAsync(() => scanner.CallCount == 2);

        await viewModel.SetDataFolderAsync(Path.Combine(installation.RootPath, "missing"));
        Assert.True(viewModel.IsInstallationValid);
        Assert.Contains("invalid", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Initialization_restores_valid_custom_sources_and_ignores_invalid_entries()
    {
        using var installation = new TemporaryInstallation();
        var store = new AppSettingsStore(installation.SettingsPath);
        await store.SaveAsync(new AppSettings
        {
            SelectedPatchSourceId = "custom-valid",
            PatchSources =
            [
                new CustomPatchSourceSettings("custom-valid", "Valid Source", "https://example.test/patches/"),
                new CustomPatchSourceSettings("", "Missing Id", "https://example.test/ignored/"),
                new CustomPatchSourceSettings(
                    PatchSourceDefinition.ProjectReforgedId,
                    "Duplicate Official",
                    "https://example.test/ignored/"),
                new CustomPatchSourceSettings("custom-valid", "Duplicate", "https://example.test/ignored/"),
                new CustomPatchSourceSettings("custom-http", "Insecure", "http://example.test/patches/")
            ]
        }, TestContext.Current.CancellationToken);
        var patch = AppTestFactory.Patch();
        var viewModel = AppTestFactory.ViewModel(
            new TestCatalog(patch),
            new StubScanner(),
            installation.SettingsPath);

        await viewModel.InitializeAsync();
        await viewModel.InitializeAsync();

        Assert.Equal(2, viewModel.PatchSources.Count);
        Assert.Equal("custom-valid", viewModel.SelectedPatchSource.Id);
        Assert.True(viewModel.CanRemovePatchSource);
        Assert.Equal("A new OctoHD update is ready.", viewModel.UpdatePromptText);

        await viewModel.PersistSelectedPatchSourceAsync();
        Assert.Contains("Valid Source is now active", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Patch_source_commands_validate_add_cancel_and_remove()
    {
        using var installation = new TemporaryInstallation();
        var viewModel = AppTestFactory.ViewModel(
            new TestCatalog(AppTestFactory.Patch()),
            new StubScanner(),
            installation.SettingsPath);
        await viewModel.InitializeAsync();

        viewModel.BeginAddPatchSourceCommand.Execute(null);
        Assert.True(viewModel.IsAddingPatchSource);

        viewModel.NewPatchSourceName = "x";
        viewModel.NewPatchSourceUrl = "https://example.test/patches/";
        viewModel.SavePatchSourceCommand.Execute(null);
        Assert.Contains("between 2 and 40", viewModel.PatchSourceError);
        Assert.True(viewModel.HasPatchSourceError);

        viewModel.NewPatchSourceName = "Project Reforged";
        viewModel.SavePatchSourceCommand.Execute(null);
        Assert.Contains("already exists", viewModel.PatchSourceError);

        viewModel.NewPatchSourceName = "Community";
        viewModel.NewPatchSourceUrl = "relative/path";
        viewModel.SavePatchSourceCommand.Execute(null);
        Assert.Contains("absolute HTTPS", viewModel.PatchSourceError);

        viewModel.NewPatchSourceUrl = "http://example.test/patches/";
        viewModel.SavePatchSourceCommand.Execute(null);
        Assert.Contains("absolute HTTPS", viewModel.PatchSourceError);

        viewModel.NewPatchSourceUrl = "https://example.test/patches/";
        viewModel.SavePatchSourceCommand.Execute(null);
        await AppTestFactory.WaitUntilAsync(() => viewModel.PatchSources.Count == 2 && !viewModel.IsAddingPatchSource);

        Assert.Equal("Community", viewModel.SelectedPatchSource.DisplayName);
        Assert.True(viewModel.CanRemovePatchSource);
        Assert.Contains("was added", viewModel.StatusMessage);
        Assert.Empty(viewModel.NewPatchSourceName);
        Assert.Empty(viewModel.NewPatchSourceUrl);

        viewModel.RemovePatchSourceCommand.Execute(null);
        await AppTestFactory.WaitUntilAsync(() => viewModel.StatusMessage.Contains("was removed", StringComparison.Ordinal));
        Assert.False(viewModel.CanRemovePatchSource);
        Assert.True(viewModel.SelectedPatchSource.IsOfficial);
        Assert.Contains("was removed", viewModel.StatusMessage);

        viewModel.BeginAddPatchSourceCommand.Execute(null);
        viewModel.NewPatchSourceName = "Cancel Me";
        viewModel.NewPatchSourceUrl = "https://example.test/cancel/";
        viewModel.CancelAddPatchSourceCommand.Execute(null);
        Assert.False(viewModel.IsAddingPatchSource);
        Assert.Empty(viewModel.NewPatchSourceName);
        Assert.Empty(viewModel.NewPatchSourceUrl);
    }
}

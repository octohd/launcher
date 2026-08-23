using System.Collections.ObjectModel;
using OctoHD.App.Infrastructure;
using OctoHD.App.Mvvm;
using OctoHD.Core.Catalog;
using OctoHD.Core.Models;
using OctoHD.Core.Services;
using OctoHD.Core.Updates;

namespace OctoHD.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IPatchCatalog _catalog;
    private readonly IPatchScanner _scanner;
    private readonly PatchManager _manager;
    private readonly DataFolderValidator _folderValidator;
    private readonly AppSettingsStore _settingsStore;
    private readonly GameLauncher _gameLauncher;
    private readonly SelfUpdateService _selfUpdateService;
    private string? _dataFolder;
    private string _statusMessage = "Select your OctoWoW folder or its Data folder to get started.";
    private string _searchText = string.Empty;
    private string _selectedFilter = "All patches";
    private bool _isBusy;
    private bool _isInstallationValid;
    private bool _initialized;
    private bool _settingsReady;
    private PatchSourceItemViewModel _selectedPatchSource;
    private string _newPatchSourceName = string.Empty;
    private string _newPatchSourceUrl = string.Empty;
    private string _patchSourceError = string.Empty;
    private bool _isAddingPatchSource;
    private bool _isUpdateReady;
    private string? _latestAppVersion;
    private bool _isChangelogOpen;

    public MainWindowViewModel(
        IPatchCatalog catalog,
        IPatchScanner scanner,
        PatchManager manager,
        DataFolderValidator folderValidator,
        AppSettingsStore settingsStore,
        GameLauncher gameLauncher,
        SelfUpdateService selfUpdateService)
    {
        _catalog = catalog;
        _scanner = scanner;
        _manager = manager;
        _folderValidator = folderValidator;
        _settingsStore = settingsStore;
        _gameLauncher = gameLauncher;
        _selfUpdateService = selfUpdateService;

        _selectedPatchSource = new PatchSourceItemViewModel(PatchSourceDefinition.ProjectReforged);
        PatchSources = [_selectedPatchSource];

        var names = catalog.Patches.ToDictionary(
            patch => patch.Id,
            patch => patch.VariantName is null ? patch.DisplayName : $"{patch.DisplayName} ({patch.VariantName})",
            StringComparer.OrdinalIgnoreCase);
        Patches = new ObservableCollection<PatchItemViewModel>(catalog.Patches.Select(definition =>
            new PatchItemViewModel(
                definition,
                definition.Dependencies.Length == 0
                    ? string.Empty
                    : $"Requires: {string.Join(", ", definition.Dependencies.Select(id => names[id]))}",
                InstallPatchAsync,
                TogglePatchAsync)));
        VisiblePatches = new ObservableCollection<PatchItemViewModel>(Patches);

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => HasDataFolder && !IsBusy);
        UpdateAllCommand = new AsyncRelayCommand(UpdateAllAsync, () => HasDataFolder && !IsBusy && UpdateCount > 0);
        LaunchCommand = new AsyncRelayCommand(LaunchOctoLauncherAsync, () => HasDataFolder && !IsBusy);
        BeginAddPatchSourceCommand = new AsyncRelayCommand(BeginAddPatchSourceAsync, () => !IsBusy);
        SavePatchSourceCommand = new AsyncRelayCommand(AddPatchSourceAsync, () => !IsBusy);
        CancelAddPatchSourceCommand = new AsyncRelayCommand(CancelAddPatchSourceAsync, () => !IsBusy);
        RemovePatchSourceCommand = new AsyncRelayCommand(RemovePatchSourceAsync, () => CanRemovePatchSource && !IsBusy);
        RestartUpdateCommand = new AsyncRelayCommand(RestartToApplyUpdateAsync, () => IsUpdateReady);
        DeferUpdateCommand = new AsyncRelayCommand(DeferUpdateAsync, () => IsUpdateReady);
        OpenChangelogCommand = new AsyncRelayCommand(OpenChangelogAsync, () => !IsChangelogOpen);
        CloseChangelogCommand = new AsyncRelayCommand(CloseChangelogAsync, () => IsChangelogOpen);
        ChangelogEntries = EmbeddedChangelog.LoadOrFallback();
    }

    public ObservableCollection<PatchItemViewModel> Patches { get; }

    public ObservableCollection<PatchItemViewModel> VisiblePatches { get; }

    public ObservableCollection<PatchSourceItemViewModel> PatchSources { get; }

    public IReadOnlyList<string> FilterOptions { get; } = ["All patches", "Installed", "Not installed", "Updates"];

    public IReadOnlyList<ChangelogEntry> ChangelogEntries { get; }

    public string AppVersionText => $"OCTOHD  v{_selfUpdateService.CurrentVersion}";

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand UpdateAllCommand { get; }

    public AsyncRelayCommand LaunchCommand { get; }

    public AsyncRelayCommand BeginAddPatchSourceCommand { get; }

    public AsyncRelayCommand SavePatchSourceCommand { get; }

    public AsyncRelayCommand CancelAddPatchSourceCommand { get; }

    public AsyncRelayCommand RemovePatchSourceCommand { get; }

    public AsyncRelayCommand RestartUpdateCommand { get; }

    public AsyncRelayCommand DeferUpdateCommand { get; }

    public AsyncRelayCommand OpenChangelogCommand { get; }

    public AsyncRelayCommand CloseChangelogCommand { get; }

    public event Action? RestartRequested;

    public bool IsChangelogOpen
    {
        get => _isChangelogOpen;
        private set
        {
            if (SetProperty(ref _isChangelogOpen, value))
            {
                OnPropertyChanged(nameof(IsMainContentEnabled));
                OpenChangelogCommand.NotifyCanExecuteChanged();
                CloseChangelogCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsMainContentEnabled => !IsChangelogOpen;

    public bool IsUpdateReady
    {
        get => _isUpdateReady;
        private set
        {
            if (SetProperty(ref _isUpdateReady, value))
            {
                RestartUpdateCommand.NotifyCanExecuteChanged();
                DeferUpdateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string UpdatePromptText => _latestAppVersion is null
        ? "A new OctoHD update is ready."
        : $"OctoHD {_latestAppVersion} was downloaded and is ready.";

    public PatchSourceItemViewModel SelectedPatchSource
    {
        get => _selectedPatchSource;
        set
        {
            if (value is not null && SetProperty(ref _selectedPatchSource, value))
            {
                foreach (var patch in Patches)
                {
                    patch.SetPatchSource(value);
                }

                OnPropertyChanged(nameof(PatchSourceDetail));
                OnPropertyChanged(nameof(CanRemovePatchSource));
                RemovePatchSourceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string PatchSourceDetail => SelectedPatchSource.DetailText;

    public bool CanRemovePatchSource => !SelectedPatchSource.IsOfficial;

    public bool IsAddingPatchSource
    {
        get => _isAddingPatchSource;
        private set => SetProperty(ref _isAddingPatchSource, value);
    }

    public string NewPatchSourceName
    {
        get => _newPatchSourceName;
        set => SetProperty(ref _newPatchSourceName, value);
    }

    public string NewPatchSourceUrl
    {
        get => _newPatchSourceUrl;
        set => SetProperty(ref _newPatchSourceUrl, value);
    }

    public string PatchSourceError
    {
        get => _patchSourceError;
        private set
        {
            if (SetProperty(ref _patchSourceError, value))
            {
                OnPropertyChanged(nameof(HasPatchSourceError));
            }
        }
    }

    public bool HasPatchSourceError => !string.IsNullOrWhiteSpace(PatchSourceError);

    public string DataFolder => _dataFolder is null
        ? "No OctoWoW folder selected"
        : Directory.GetParent(_dataFolder)?.FullName ?? _dataFolder;

    public bool HasDataFolder => IsInstallationValid && !string.IsNullOrWhiteSpace(_dataFolder);

    public bool IsInstallationValid
    {
        get => _isInstallationValid;
        private set
        {
            if (SetProperty(ref _isInstallationValid, value))
            {
                OnPropertyChanged(nameof(HasDataFolder));
                RaiseSummary();
            }
        }
    }

    public bool IsInstallationInvalid => !IsInstallationValid;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyPatchFilter();
            }
        }
    }

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                ApplyPatchFilter();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                UpdateAllCommand.NotifyCanExecuteChanged();
                LaunchCommand.NotifyCanExecuteChanged();
                BeginAddPatchSourceCommand.NotifyCanExecuteChanged();
                SavePatchSourceCommand.NotifyCanExecuteChanged();
                CancelAddPatchSourceCommand.NotifyCanExecuteChanged();
                RemovePatchSourceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int ActiveCount => Patches.Count(patch => patch.IsEnabled);

    public int InstalledCount => Patches.Count(patch => patch.IsInstalled);

    public int UpdateCount => Patches.Count(patch => patch.IsUpdateAvailable);

    public string SummaryText => HasDataFolder
        ? $"{ActiveCount} active  ·  {InstalledCount} installed  ·  {UpdateCount} updates"
        : "No installation connected";

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            var settings = await _settingsStore.LoadAsync();
            RestorePatchSources(settings);
            _settingsReady = true;
            if (!string.IsNullOrWhiteSpace(settings.DataFolder) && Directory.Exists(settings.DataFolder))
            {
                await SetDataFolderAsync(settings.DataFolder);
            }
        }
        catch (Exception exception)
        {
            StatusMessage = $"Saved settings could not be loaded: {exception.Message}";
        }

        _ = CheckForUpdatesAsync();
    }

    public async Task SetDataFolderAsync(string path)
    {
        IsBusy = true;
        StatusMessage = "Checking the Data folder…";
        try
        {
            var validation = await _folderValidator.ValidateAsync(path);
            if (!validation.IsValid || validation.NormalizedPath is null)
            {
                if (_dataFolder is null)
                {
                    IsInstallationValid = false;
                }

                StatusMessage = validation.Error ?? "The Data folder is invalid.";
                return;
            }

            _dataFolder = validation.NormalizedPath;
            IsInstallationValid = true;
            OnPropertyChanged(nameof(DataFolder));
            OnPropertyChanged(nameof(HasDataFolder));
            await SaveSettingsAsync();
            await RefreshCoreAsync();
            if (validation.Warnings.Count > 0)
            {
                StatusMessage = string.Join(" ", validation.Warnings);
            }
        }
        catch (Exception exception)
        {
            if (_dataFolder is null)
            {
                IsInstallationValid = false;
            }

            StatusMessage = $"The folder could not be selected: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            RaiseSummary();
        }
    }

    private async Task RefreshAsync()
    {
        if (_dataFolder is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Detecting installed patches…";
        try
        {
            await RefreshCoreAsync();
        }
        catch (Exception exception)
        {
            if (_dataFolder is null || !Directory.Exists(_dataFolder))
            {
                IsInstallationValid = false;
            }

            StatusMessage = $"Scan failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshCoreAsync()
    {
        if (_dataFolder is null)
        {
            return;
        }

        var results = await _scanner.ScanAsync(_dataFolder);
        ApplyResults(results);
        IsInstallationValid = true;
        StatusMessage = string.Empty;
    }

    private async Task InstallPatchAsync(PatchItemViewModel item)
    {
        if (_dataFolder is null)
        {
            return;
        }

        item.BeginOperation("Preparing download…");
        StatusMessage = $"Downloading {item.Title} from {SelectedPatchSource.DisplayName}.";
        try
        {
            var progress = new Progress<PatchOperationProgress>(item.ApplyProgress);
            var results = await _manager.InstallAsync(
                _dataFolder,
                item.Definition,
                progress,
                SelectedPatchSource.Definition);
            ApplyResults(results);
            StatusMessage = $"{item.Title} was installed successfully.";
        }
        catch (Exception exception)
        {
            item.EndWithError(exception.Message);
            StatusMessage = exception.Message;
        }
    }

    private async Task TogglePatchAsync(PatchItemViewModel item)
    {
        if (_dataFolder is null)
        {
            return;
        }

        var enable = !item.IsEnabled;
        item.BeginOperation(enable ? "Enabling…" : "Disabling…");
        try
        {
            var results = await _manager.SetEnabledAsync(_dataFolder, item.Definition, enable);
            ApplyResults(results);
            StatusMessage = $"{item.Title} is now {(enable ? "enabled" : "disabled")}.";
        }
        catch (Exception exception)
        {
            item.EndWithError(exception.Message);
            StatusMessage = exception.Message;
        }
    }

    private async Task UpdateAllAsync()
    {
        var updates = Patches.Where(patch => patch.IsUpdateAvailable).ToArray();
        foreach (var patch in updates)
        {
            await InstallPatchAsync(patch);
        }
    }

    private Task LaunchOctoLauncherAsync()
    {
        if (_dataFolder is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            _gameLauncher.Launch(_dataFolder);
            StatusMessage = "OctoLauncher was opened.";
        }
        catch (PatchOperationException exception)
        {
            StatusMessage = exception.Message;
        }

        return Task.CompletedTask;
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var result = await _selfUpdateService.CheckAndDownloadAsync();
            if (result.UpdateAvailable)
            {
                _latestAppVersion = result.LatestVersion;
                OnPropertyChanged(nameof(UpdatePromptText));
                IsUpdateReady = true;
            }
        }
        catch
        {
            // Update checks are best effort and never block patch management.
        }
    }

    private Task RestartToApplyUpdateAsync()
    {
        if (_selfUpdateService.TryRestartToApply(out var error))
        {
            RestartRequested?.Invoke();
        }
        else
        {
            StatusMessage = $"The update could not be started: {error}";
        }

        return Task.CompletedTask;
    }

    private Task DeferUpdateAsync()
    {
        IsUpdateReady = false;
        return Task.CompletedTask;
    }

    private Task OpenChangelogAsync()
    {
        IsChangelogOpen = true;
        return Task.CompletedTask;
    }

    private Task CloseChangelogAsync()
    {
        IsChangelogOpen = false;
        return Task.CompletedTask;
    }

    public async Task PersistSelectedPatchSourceAsync()
    {
        if (!_settingsReady)
        {
            return;
        }

        await SaveSettingsAsync();
        StatusMessage = SelectedPatchSource.IsOfficial
            ? "Project Reforged is now the active verified patch source."
            : $"{SelectedPatchSource.DisplayName} is now active. Custom sources are trusted by URL and MPQ validation.";
    }

    public void ReportSettingsError(string message) =>
        StatusMessage = $"Patch source settings could not be saved: {message}";

    public void ReportExternalLinkError(string message) =>
        StatusMessage = $"The website could not be opened: {message}";

    private Task BeginAddPatchSourceAsync()
    {
        PatchSourceError = string.Empty;
        IsAddingPatchSource = true;
        return Task.CompletedTask;
    }

    private Task CancelAddPatchSourceAsync()
    {
        PatchSourceError = string.Empty;
        NewPatchSourceName = string.Empty;
        NewPatchSourceUrl = string.Empty;
        IsAddingPatchSource = false;
        return Task.CompletedTask;
    }

    private async Task AddPatchSourceAsync()
    {
        PatchSourceError = string.Empty;
        var name = NewPatchSourceName.Trim();
        if (name.Length is < 2 or > 40)
        {
            PatchSourceError = "Use a source name between 2 and 40 characters.";
            return;
        }

        if (PatchSources.Any(source => string.Equals(source.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
        {
            PatchSourceError = "A source with this name already exists.";
            return;
        }

        if (!Uri.TryCreate(NewPatchSourceUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            PatchSourceError = "Enter an absolute HTTPS bucket URL.";
            return;
        }

        try
        {
            var definition = new PatchSourceDefinition($"custom-{Guid.NewGuid():N}", name, baseUri);
            var source = new PatchSourceItemViewModel(definition);
            PatchSources.Add(source);
            SelectedPatchSource = source;
            await SaveSettingsAsync();
            await CancelAddPatchSourceAsync();
            StatusMessage = $"{source.DisplayName} was added. Downloads resolve patch filenames below its base URL.";
        }
        catch (ArgumentException exception)
        {
            PatchSourceError = exception.Message;
        }
    }

    private async Task RemovePatchSourceAsync()
    {
        if (!CanRemovePatchSource)
        {
            return;
        }

        var removedName = SelectedPatchSource.DisplayName;
        var removed = SelectedPatchSource;
        SelectedPatchSource = PatchSources[0];
        PatchSources.Remove(removed);
        await SaveSettingsAsync();
        StatusMessage = $"{removedName} was removed. Project Reforged is active again.";
    }

    private void RestorePatchSources(AppSettings settings)
    {
        foreach (var saved in settings.PatchSources ?? [])
        {
            try
            {
                if (string.IsNullOrWhiteSpace(saved.Id)
                    || string.Equals(saved.Id, PatchSourceDefinition.ProjectReforgedId, StringComparison.OrdinalIgnoreCase)
                    || PatchSources.Any(source => string.Equals(source.Id, saved.Id, StringComparison.OrdinalIgnoreCase))
                    || !Uri.TryCreate(saved.BaseUrl, UriKind.Absolute, out var uri))
                {
                    continue;
                }

                PatchSources.Add(new PatchSourceItemViewModel(
                    new PatchSourceDefinition(saved.Id, saved.DisplayName, uri)));
            }
            catch (ArgumentException)
            {
                // Invalid legacy settings are ignored and can be added again through the validated UI.
            }
        }

        var selected = PatchSources.FirstOrDefault(source =>
            string.Equals(source.Id, settings.SelectedPatchSourceId, StringComparison.OrdinalIgnoreCase));
        SelectedPatchSource = selected ?? PatchSources[0];
    }

    private Task SaveSettingsAsync() => _settingsStore.SaveAsync(new AppSettings
    {
        DataFolder = _dataFolder,
        SelectedPatchSourceId = SelectedPatchSource.Id,
        PatchSources = PatchSources
            .Where(source => !source.IsOfficial)
            .Select(source => new CustomPatchSourceSettings(
                source.Id,
                source.DisplayName,
                source.BaseUrl))
            .ToList()
    });

    private void ApplyResults(IReadOnlyList<PatchScanResult> results)
    {
        var byId = results.ToDictionary(result => result.Patch.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var item in Patches)
        {
            item.ApplyScanResult(byId[item.Definition.Id]);
            item.SetPatchSource(SelectedPatchSource);
        }

        RaiseSummary();
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(IsInstallationInvalid));
        UpdateAllCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
        ApplyPatchFilter();
    }

    private void ApplyPatchFilter()
    {
        var matches = Patches
            .Where(patch =>
                (_selectedFilter switch
                {
                    "Installed" => patch.IsInstalled,
                    "Not installed" => patch.IsNotInstalled,
                    "Updates" => patch.IsUpdateAvailable,
                    _ => true
                })
                && (string.IsNullOrWhiteSpace(_searchText)
                    || patch.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                    || patch.Description.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                    || patch.Category.Contains(_searchText, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(patch => patch.IsEnabled);

        VisiblePatches.Clear();
        foreach (var patch in matches)
        {
            VisiblePatches.Add(patch);
        }
    }
}

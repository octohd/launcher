using OctoHD.App.Mvvm;
using OctoHD.Core.Models;

namespace OctoHD.App.ViewModels;

public sealed class PatchItemViewModel : ObservableObject
{
    private readonly Func<PatchItemViewModel, Task> _install;
    private readonly Func<PatchItemViewModel, Task> _toggle;
    private PatchScanResult _scanResult;
    private bool _isBusy;
    private double _progress;
    private string? _operationText;
    private string? _operationError;
    private string _selectedSourceId = PatchSourceDefinition.ProjectReforgedId;
    private string _selectedSourceName = "Project Reforged";
    private bool _selectedSourceIsOfficial = true;

    public PatchItemViewModel(
        PatchDefinition definition,
        string dependencyText,
        Func<PatchItemViewModel, Task> install,
        Func<PatchItemViewModel, Task> toggle)
    {
        Definition = definition;
        DependencyText = dependencyText;
        _install = install;
        _toggle = toggle;
        _scanResult = new PatchScanResult(definition, PatchStatus.Checking);
        InstallCommand = new AsyncRelayCommand(() => _install(this), () => CanInstall);
        ToggleCommand = new AsyncRelayCommand(() => _toggle(this), () => CanToggle);
    }

    public PatchDefinition Definition { get; }

    public AsyncRelayCommand InstallCommand { get; }

    public AsyncRelayCommand ToggleCommand { get; }

    public string Title => Definition.DisplayName;

    public string Description => Definition.Description;

    public string Category => Definition.Category;

    public string VersionText => $"v{Definition.Version}";

    public string VariantText => Definition.VariantName ?? (Definition.IsCore ? "CORE" : "OPTIONAL");

    public bool IsHeavy => Definition.IsHeavy;

    public string SizeText => _selectedSourceIsOfficial
        ? FormatBytes(Definition.ExpectedSize)
        : "Source size";

    public string DependencyText { get; }

    public bool HasDependencies => !string.IsNullOrEmpty(DependencyText);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseStateProperties();
            }
        }
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string OperationText => _operationText ?? string.Empty;

    public bool HasOperationText => !string.IsNullOrWhiteSpace(_operationText);

    public string StatusText => _operationError ?? _scanResult.Status switch
    {
        PatchStatus.NotInstalled => "Not installed",
        PatchStatus.Active => "Enabled",
        PatchStatus.Disabled => "Disabled",
        PatchStatus.UpdateAvailableActive => "Update available · enabled",
        PatchStatus.UpdateAvailableDisabled => "Update available · disabled",
        PatchStatus.Conflict => "File conflict",
        PatchStatus.ForeignFile => "Unknown file detected",
        PatchStatus.Corrupt => "File damaged",
        PatchStatus.Checking => "Checking…",
        PatchStatus.Busy => "Processing…",
        PatchStatus.Error => "Error",
        _ => "Unknown"
    };

    public string StatusBadgeText => _operationError ?? StatusText.ToUpperInvariant();

    public string StatusBackground => StatusPalette.Background;

    public string StatusBorderBrush => StatusPalette.Border;

    public string StatusForeground => StatusPalette.Foreground;

    public string StatusGlyph => StatusPalette.Glyph;

    public string DetailForeground => _operationError is not null || _scanResult.Status is
        PatchStatus.Conflict or PatchStatus.ForeignFile or PatchStatus.Corrupt or PatchStatus.Error
            ? "#F28B79"
            : "#8FA4B3";

    public bool IsEnabled => _scanResult.IsActive;

    public bool IsInstalled => _scanResult.Status is not PatchStatus.NotInstalled and not PatchStatus.Checking;

    public bool IsNotInstalled => _scanResult.Status is PatchStatus.NotInstalled;

    public string SourceLabel => _selectedSourceName.ToUpperInvariant();

    public bool IsInstalledFromSelectedSource => !IsInstalled
        || string.Equals(
            _scanResult.InstalledSourceId ?? PatchSourceDefinition.ProjectReforgedId,
            _selectedSourceId,
            StringComparison.OrdinalIgnoreCase);

    public bool CanToggle => !IsBusy && _scanResult.CanToggle;

    public bool CanInstall => !IsBusy && (IsInstallOrUpdateAvailable
        || IsInstalled && (!_selectedSourceIsOfficial || !IsInstalledFromSelectedSource));

    public bool ShowInstallButton => IsInstallOrUpdateAvailable
        || IsInstalled && (!_selectedSourceIsOfficial || !IsInstalledFromSelectedSource);

    public string ActionLabel => _scanResult.Status is PatchStatus.UpdateAvailableActive or PatchStatus.UpdateAvailableDisabled
        ? "UPDATE"
        : IsInstalled
            ? "REINSTALL"
            : Definition.VariantName is null ? "INSTALL" : $"INSTALL {Definition.VariantName.ToUpperInvariant()}";

    public string DetailMessage => _operationError ?? _scanResult.Message ?? string.Empty;

    public bool HasDetailMessage => !string.IsNullOrWhiteSpace(DetailMessage);

    public bool IsUpdateAvailable => _scanResult.Status is
        PatchStatus.UpdateAvailableActive or PatchStatus.UpdateAvailableDisabled;

    public void ApplyScanResult(PatchScanResult scanResult)
    {
        _scanResult = scanResult;
        _operationError = null;
        _operationText = null;
        Progress = 0;
        IsBusy = false;
        RaiseStateProperties();
    }

    public void SetPatchSource(PatchSourceItemViewModel source)
    {
        _selectedSourceId = source.Id;
        _selectedSourceName = source.DisplayName;
        _selectedSourceIsOfficial = source.IsOfficial;
        RaiseStateProperties();
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(IsInstalledFromSelectedSource));
    }

    public void BeginOperation(string text)
    {
        _operationError = null;
        _operationText = text;
        Progress = 0;
        IsBusy = true;
        RaiseStateProperties();
    }

    public void ApplyProgress(PatchOperationProgress progress, string? context = null)
    {
        Progress = progress.Percentage;
        var prefix = string.IsNullOrWhiteSpace(context) ? string.Empty : $"{context} · ";
        _operationText = progress.Phase == "Download"
            ? $"{prefix}{progress.Percentage:N0}% · {FormatBytes((long)progress.BytesPerSecond)}/s"
            : $"{prefix}{progress.Phase}";
        OnPropertyChanged(nameof(OperationText));
        OnPropertyChanged(nameof(HasOperationText));
    }

    public void EndWithError(string message)
    {
        _operationText = null;
        _operationError = message;
        IsBusy = false;
        RaiseStateProperties();
    }

    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(StatusBackground));
        OnPropertyChanged(nameof(StatusBorderBrush));
        OnPropertyChanged(nameof(StatusForeground));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(DetailForeground));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(IsNotInstalled));
        OnPropertyChanged(nameof(IsInstalledFromSelectedSource));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(ShowInstallButton));
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(DetailMessage));
        OnPropertyChanged(nameof(HasDetailMessage));
        OnPropertyChanged(nameof(OperationText));
        OnPropertyChanged(nameof(HasOperationText));
        OnPropertyChanged(nameof(IsUpdateAvailable));
        InstallCommand.NotifyCanExecuteChanged();
        ToggleCommand.NotifyCanExecuteChanged();
    }

    private bool IsInstallOrUpdateAvailable => _scanResult.Status is
        PatchStatus.NotInstalled
        or PatchStatus.UpdateAvailableActive
        or PatchStatus.UpdateAvailableDisabled;

    private StatusBadgePalette StatusPalette => _operationError is not null
        ? StatusBadgePalette.Error
        : _scanResult.Status switch
        {
            PatchStatus.Active => StatusBadgePalette.Success,
            PatchStatus.Disabled => StatusBadgePalette.Neutral,
            PatchStatus.UpdateAvailableActive or PatchStatus.UpdateAvailableDisabled => StatusBadgePalette.Warning,
            PatchStatus.Conflict or PatchStatus.ForeignFile or PatchStatus.Corrupt or PatchStatus.Error =>
                StatusBadgePalette.Error,
            PatchStatus.Busy or PatchStatus.Checking => StatusBadgePalette.Info,
            PatchStatus.NotInstalled => StatusBadgePalette.NotInstalled,
            _ => StatusBadgePalette.NotInstalled
        };

    private readonly record struct StatusBadgePalette(
        string Background,
        string Border,
        string Foreground,
        string Glyph)
    {
        public static StatusBadgePalette Success { get; } =
            new("#2A1E7D50", "#6553B77D", "#8DE5AD", "✓");

        public static StatusBadgePalette Neutral { get; } =
            new("#2A4C5861", "#6576848E", "#BEC8CF", "–");

        public static StatusBadgePalette Warning { get; } =
            new("#2A8A641F", "#65C69235", "#F0C875", "↓");

        public static StatusBadgePalette Error { get; } =
            new("#2A8B3E39", "#65D16C62", "#F3A199", "×");

        public static StatusBadgePalette Info { get; } =
            new("#2A245E7A", "#65539ABD", "#8DD7F1", "…");

        public static StatusBadgePalette NotInstalled { get; } =
            new("#2A303B44", "#65596772", "#ABB8C1", "○");
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)Math.Max(bytes, 0);
        var index = 0;
        while (size >= 1024 && index < suffixes.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size:N1} {suffixes[index]}";
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using OctoHD.App.ViewModels;

namespace OctoHD.App.Views;

public sealed partial class MainWindow : Window
{
    private Bitmap? _cursorBitmap;
    private Cursor? _fantasyCursor;
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureFantasyCursor();
        Opened += MainWindow_OnOpened;
        Closed += MainWindow_OnClosed;
    }

    private void ConfigureFantasyCursor()
    {
        try
        {
            using var cursorStream = AssetLoader.Open(
                new Uri("avares://OctoHD/Assets/Cursors/octohd-fantasy-cursor.png"));
            _cursorBitmap = new Bitmap(cursorStream);
            _fantasyCursor = new Cursor(_cursorBitmap, new PixelPoint(2, 2));
            Cursor = _fantasyCursor;
        }
        catch
        {
            _fantasyCursor = new Cursor(StandardCursorType.Arrow);
            Cursor = _fantasyCursor;
        }
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        _fantasyCursor?.Dispose();
        _cursorBitmap?.Dispose();
    }

    private async void MainWindow_OnOpened(object? sender, EventArgs e)
    {
        if (_initialized || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _initialized = true;
        await viewModel.InitializeAsync();
    }

    private async void SelectFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !StorageProvider.CanPickFolder)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the OctoWoW folder or its Data folder",
            AllowMultiple = false
        });
        if (folders.Count == 0)
        {
            return;
        }

        using var folder = folders[0];
        var path = folder.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await viewModel.SetDataFolderAsync(path);
        }
    }

    private async void PatchSource_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is ComboBox { SelectedItem: PatchSourceItemViewModel selectedSource })
        {
            try
            {
                viewModel.SelectedPatchSource = selectedSource;
                await viewModel.PersistSelectedPatchSourceAsync();
            }
            catch (Exception exception)
            {
                viewModel.ReportSettingsError(exception.Message);
            }
        }
    }
}

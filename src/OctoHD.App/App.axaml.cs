using System.Net;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OctoHD.App.Infrastructure;
using OctoHD.App.ViewModels;
using OctoHD.App.Views;
using OctoHD.Core.Catalog;
using OctoHD.Core.Persistence;
using OctoHD.Core.Services;
using OctoHD.Core.Updates;

namespace OctoHD.App;

public sealed partial class App : Application
{
    private HttpClient? _httpClient;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var catalog = new EmbeddedPatchCatalog();
            var stateStore = new JsonPatchStateStore();
            var scanner = new PatchScanner(catalog, stateStore);
            var dependencyService = new PatchDependencyService(catalog);
            var hashService = new FileHashService();
            _httpClient = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All
            })
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            var manager = new PatchManager(
                catalog,
                stateStore,
                scanner,
                dependencyService,
                hashService,
                _httpClient);
            var viewModel = new MainWindowViewModel(
                catalog,
                scanner,
                manager,
                new DataFolderValidator(),
                new AppSettingsStore(),
                new GameLauncher(),
                SelfUpdateService.Create(_httpClient));

            viewModel.RestartRequested += () => desktop.Shutdown();

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.Exit += (_, _) => _httpClient?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

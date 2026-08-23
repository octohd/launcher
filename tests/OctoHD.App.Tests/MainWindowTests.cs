using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using OctoHD.App.Infrastructure;
using OctoHD.App.Views;

namespace OctoHD.App.Tests;

public sealed class MainWindowTests
{
    [AvaloniaTheory]
    [InlineData(1.0, 1240, 760)]
    [InlineData(1.5, 1860, 1140)]
    [InlineData(2.0, 2480, 1520)]
    public void Main_window_renders_at_the_platform_scale(
        double scaling,
        int expectedPixelWidth,
        int expectedPixelHeight)
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            window.SetRenderScaling(scaling);

            using var frame = window.CaptureRenderedFrame();

            Assert.NotNull(frame);
            Assert.Equal(scaling, window.RenderScaling);
            Assert.Equal(new Size(1240, 760), window.ClientSize);
            Assert.Equal(new PixelSize(expectedPixelWidth, expectedPixelHeight), frame.PixelSize);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Main_window_loads_the_application_shell()
    {
        var window = new MainWindow();
        try
        {
            window.Show();

            Assert.Equal("OctoHD Launcher", window.Title);
            Assert.NotNull(window.Content);

            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            var rescanButton = Assert.Single(buttons, button => Equals(button.Content, "RESCAN"));
            var playButton = Assert.Single(buttons, button => Equals(button.Content, "PLAY"));

            Assert.Equal(VerticalAlignment.Center, rescanButton.VerticalContentAlignment);
            Assert.Equal(
                OperatingSystem.IsMacOS() ? new Thickness(16, 10, 16, 8) : new Thickness(16, 9),
                rescanButton.Padding);
            Assert.Equal(new Thickness(16, 9), playButton.Padding);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Changelog_button_shows_embedded_release_notes_and_closes_the_overlay()
    {
        using var installation = new TemporaryInstallation();
        var viewModel = AppTestFactory.ViewModel(
            new TestCatalog(AppTestFactory.Patch()),
            new StubScanner(),
            installation.SettingsPath);
        var window = new MainWindow
        {
            DataContext = viewModel
        };

        try
        {
            window.Show();
            var mainView = Assert.IsType<Grid>(window.FindControl<Control>("MainApplicationView"));
            var overlay = Assert.IsType<Grid>(window.FindControl<Control>("ChangelogOverlay"));
            var openButton = Assert.IsType<Button>(window.FindControl<Control>("OpenChangelogButton"));
            var closeButton = Assert.IsType<Button>(window.FindControl<Control>("CloseChangelogButton"));
            var entries = Assert.IsType<ItemsControl>(window.FindControl<Control>("ChangelogEntries"));

            Assert.False(overlay.IsVisible);
            Assert.True(mainView.IsEnabled);
            Assert.True(closeButton.IsCancel);

            var openCommand = openButton.Command ?? throw new InvalidOperationException("Open command is not bound.");
            Assert.Same(viewModel.OpenChangelogCommand, openCommand);
            openCommand.Execute(null);
            window.UpdateLayout();

            Assert.True(overlay.IsVisible);
            Assert.False(mainView.IsEnabled);
            Assert.Same(viewModel.ChangelogEntries, entries.ItemsSource);
            var firstEntry = Assert.IsType<ChangelogEntry>(viewModel.ChangelogEntries[0]);
            var texts = overlay.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Contains(texts, text => Equals(text.Text, firstEntry.Version));
            Assert.Contains(texts, text => Equals(text.Text, firstEntry.Description));

            var closeCommand = closeButton.Command ?? throw new InvalidOperationException("Close command is not bound.");
            Assert.Same(viewModel.CloseChangelogCommand, closeCommand);
            closeCommand.Execute(null);

            Assert.False(overlay.IsVisible);
            Assert.True(mainView.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }
}

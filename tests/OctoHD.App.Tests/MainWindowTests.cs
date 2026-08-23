using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using OctoHD.App.Controls;
using OctoHD.App.Infrastructure;
using OctoHD.App.Views;
using OctoHD.Core.Models;

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
            window.UpdateLayout();
            var particleLayer = Assert.IsType<ParticleBackground>(
                window.FindControl<Control>("ParticleBackgroundLayer"));

            using var frame = window.CaptureRenderedFrame();

            Assert.NotNull(frame);
            Assert.Equal(scaling, window.RenderScaling);
            Assert.Equal(new Size(1240, 760), window.ClientSize);
            Assert.Equal(new Size(1240, 760), particleLayer.Bounds.Size);
            Assert.Equal(scaling, TopLevel.GetTopLevel(particleLayer)?.RenderScaling);
            Assert.Equal(new PixelSize(expectedPixelWidth, expectedPixelHeight), frame.PixelSize);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Particle_background_is_a_non_interactive_layer_with_a_bounded_lifecycle()
    {
        var window = new MainWindow();
        ParticleBackground? particleLayer = null;
        try
        {
            window.Show();
            window.Activate();
            window.UpdateLayout();
            var root = Assert.IsType<Grid>(window.Content);
            particleLayer = Assert.IsType<ParticleBackground>(
                window.FindControl<Control>("ParticleBackgroundLayer"));
            var mainView = Assert.IsType<Grid>(window.FindControl<Control>("MainApplicationView"));
            var overlay = Assert.IsType<Grid>(window.FindControl<Control>("ChangelogOverlay"));

            Assert.IsType<Image>(root.Children[0]);
            Assert.IsType<Border>(root.Children[1]);
            Assert.Same(particleLayer, root.Children[2]);
            Assert.Same(mainView, root.Children[3]);
            Assert.Same(overlay, root.Children[4]);
            Assert.Equal(2, particleLayer.ZIndex);
            Assert.Equal(10, mainView.ZIndex);
            Assert.Equal(100, overlay.ZIndex);
            Assert.False(particleLayer.IsHitTestVisible);
            Assert.False(particleLayer.Focusable);
            Assert.True(particleLayer.ClipToBounds);
            Assert.Equal(window.ClientSize, particleLayer.Bounds.Size);
            Assert.Equal(48, particleLayer.Particles.Count);
            Assert.True(particleLayer.IsAnimationEnabled);

            particleLayer.IsAnimationEnabled = false;
            Assert.False(particleLayer.IsAnimationRunning);
            particleLayer.IsAnimationEnabled = true;
            Assert.True(particleLayer.IsAnimationEnabled);
        }
        finally
        {
            window.Close();
        }

        Assert.NotNull(particleLayer);
        Assert.False(particleLayer.IsAnimationRunning);
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
    public async Task Patch_library_defaults_to_cards_and_switches_to_list()
    {
        using var installation = new TemporaryInstallation();
        installation.AddGameExecutable();
        var longPatchId = $"patch-{new string('x', 64)}";
        var patch = AppTestFactory.Patch(
            longPatchId,
            new string('C', 64),
            variantName: new string('V', 64));
        var scanner = new StubScanner
        {
            Results = [new PatchScanResult(patch, PatchStatus.NotInstalled)]
        };
        var viewModel = AppTestFactory.ViewModel(
            new TestCatalog(patch),
            scanner,
            installation.SettingsPath);
        await viewModel.InitializeAsync();
        await viewModel.SetDataFolderAsync(installation.RootPath);
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1060,
            Height = 640
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            var cardButton = Assert.IsType<Button>(window.FindControl<Control>("PatchCardViewButton"));
            var listButton = Assert.IsType<Button>(window.FindControl<Control>("PatchListViewButton"));
            var cardView = Assert.IsType<ItemsControl>(window.FindControl<Control>("PatchCardView"));
            var listView = Assert.IsType<ItemsControl>(window.FindControl<Control>("PatchListView"));

            Assert.Contains("active", cardButton.Classes);
            Assert.DoesNotContain("active", listButton.Classes);
            Assert.Equal("Selected", AutomationProperties.GetItemStatus(cardButton));
            Assert.Equal("Not selected", AutomationProperties.GetItemStatus(listButton));
            Assert.True(cardView.IsVisible);
            Assert.False(listView.IsVisible);
            Assert.Same(viewModel.VisiblePatches, cardView.ItemsSource);
            Assert.Same(viewModel.VisiblePatches, listView.ItemsSource);
            var cardStatusBadge = Assert.Single(
                cardView.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("statusBadge"));
            Assert.Equal(new Thickness(8, 4), cardStatusBadge.Padding);
            Assert.Equal(new Thickness(1), cardStatusBadge.BorderThickness);
            Assert.Equal(
                Color.Parse("#2A303B44"),
                Assert.IsAssignableFrom<ISolidColorBrush>(cardStatusBadge.Background).Color);
            Assert.Equal(
                Color.Parse("#65596772"),
                Assert.IsAssignableFrom<ISolidColorBrush>(cardStatusBadge.BorderBrush).Color);
            var cardStatusTexts = cardStatusBadge.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Contains(cardStatusTexts, text => Equals(text.Text, "○"));
            Assert.Contains(cardStatusTexts, text => Equals(text.Text, "NOT INSTALLED"));
            await viewModel.PersistSelectedPatchSourceAsync();

            listButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            await AppTestFactory.WaitUntilAsync(
                () => SettingsContainViewMode(installation.SettingsPath, isListView: true));

            Assert.True(viewModel.IsListView);
            Assert.DoesNotContain("active", cardButton.Classes);
            Assert.Contains("active", listButton.Classes);
            Assert.Equal("Not selected", AutomationProperties.GetItemStatus(cardButton));
            Assert.Equal("Selected", AutomationProperties.GetItemStatus(listButton));
            Assert.False(cardView.IsVisible);
            Assert.True(listView.IsVisible);
            var listStatusBadge = Assert.Single(
                listView.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("statusBadge"));
            Assert.Equal(new Thickness(8, 4), listStatusBadge.Padding);
            Assert.Equal(new Thickness(1), listStatusBadge.BorderThickness);
            var listStatusTexts = listStatusBadge.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Contains(listStatusTexts, text => Equals(text.Text, "○"));
            Assert.Contains(listStatusTexts, text => Equals(text.Text, "NOT INSTALLED"));
            Assert.Single(cardButton.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
            Assert.Equal(2, listButton.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Count());
            var title = Assert.Single(
                listView.GetVisualDescendants().OfType<TextBlock>(),
                text => Equals(text.Text, patch.DisplayName));
            var description = Assert.Single(
                listView.GetVisualDescendants().OfType<TextBlock>(),
                text => Equals(text.Text, patch.Description));
            Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
            Assert.Equal(TextTrimming.CharacterEllipsis, description.TextTrimming);
            var patchItem = Assert.Single(viewModel.VisiblePatches);
            var actionButton = Assert.Single(
                listView.GetVisualDescendants().OfType<Button>(),
                button => ReferenceEquals(button.Command, patchItem.InstallCommand));
            var enableToggle = Assert.Single(listView.GetVisualDescendants().OfType<ToggleSwitch>());
            Assert.Same(patchItem.InstallCommand, actionButton.Command);
            Assert.Same(patchItem.ToggleCommand, enableToggle.Command);
            Assert.Equal(122, actionButton.MaxWidth);
            var actionText = Assert.IsType<TextBlock>(actionButton.Content);
            Assert.Equal(TextTrimming.CharacterEllipsis, actionText.TextTrimming);

            cardButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            await AppTestFactory.WaitUntilAsync(
                () => SettingsContainViewMode(installation.SettingsPath, isListView: false));

            Assert.False(viewModel.IsListView);
            Assert.Contains("active", cardButton.Classes);
            Assert.DoesNotContain("active", listButton.Classes);
            Assert.Equal("Selected", AutomationProperties.GetItemStatus(cardButton));
            Assert.Equal("Not selected", AutomationProperties.GetItemStatus(listButton));
            Assert.True(cardView.IsVisible);
            Assert.False(listView.IsVisible);

            await viewModel.PersistSelectedPatchSourceAsync();
        }
        finally
        {
            window.Close();
        }
    }

    private static bool SettingsContainViewMode(string settingsPath, bool isListView)
    {
        try
        {
            var expected = $"\"isListView\": {isListView.ToString().ToLowerInvariant()}";
            return File.Exists(settingsPath)
                   && File.ReadAllText(settingsPath).Contains(expected, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
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
            window.Activate();
            var mainView = Assert.IsType<Grid>(window.FindControl<Control>("MainApplicationView"));
            var overlay = Assert.IsType<Grid>(window.FindControl<Control>("ChangelogOverlay"));
            var openButton = Assert.IsType<Button>(window.FindControl<Control>("OpenChangelogButton"));
            var closeButton = Assert.IsType<Button>(window.FindControl<Control>("CloseChangelogButton"));
            var entries = Assert.IsType<ItemsControl>(window.FindControl<Control>("ChangelogEntries"));
            var particleLayer = Assert.IsType<ParticleBackground>(
                window.FindControl<Control>("ParticleBackgroundLayer"));

            Assert.False(overlay.IsVisible);
            Assert.True(mainView.IsEnabled);
            Assert.True(closeButton.IsCancel);
            Assert.False(particleLayer.IsPaused);

            var openCommand = openButton.Command ?? throw new InvalidOperationException("Open command is not bound.");
            Assert.Same(viewModel.OpenChangelogCommand, openCommand);
            openCommand.Execute(null);
            window.UpdateLayout();

            Assert.True(overlay.IsVisible);
            Assert.False(mainView.IsEnabled);
            Assert.True(particleLayer.IsPaused);
            Assert.False(particleLayer.IsAnimationRunning);
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
            Assert.False(particleLayer.IsPaused);
        }
        finally
        {
            window.Close();
        }
    }
}

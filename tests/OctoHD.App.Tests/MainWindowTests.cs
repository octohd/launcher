using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using OctoHD.App.Views;

namespace OctoHD.App.Tests;

public sealed class MainWindowTests
{
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
}

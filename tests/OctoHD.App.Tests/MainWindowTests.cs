using Avalonia.Headless.XUnit;
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
        }
        finally
        {
            window.Close();
        }
    }
}

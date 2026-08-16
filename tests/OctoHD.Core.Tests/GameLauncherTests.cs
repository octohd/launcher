using OctoHD.Core.Services;

namespace OctoHD.Core.Tests;

public sealed class GameLauncherTests
{
    [Fact]
    public void Finds_portable_octo_launcher_next_to_data_folder()
    {
        using var folder = new TemporaryDataFolder();
        var installationDirectory = Directory.GetParent(folder.DataPath)!.FullName;
        var launcherPath = Path.Combine(installationDirectory, "OctoLauncher.exe");
        File.WriteAllBytes(launcherPath, []);

        var result = new GameLauncher().FindExecutable(folder.DataPath);

        Assert.Equal(launcherPath, result);
    }

    [Fact]
    public void Does_not_treat_game_client_as_octo_launcher()
    {
        using var folder = new TemporaryDataFolder();
        var installationDirectory = Directory.GetParent(folder.DataPath)!.FullName;
        var gameClientPath = Path.Combine(installationDirectory, "WoW.exe");
        File.WriteAllBytes(gameClientPath, []);

        var result = new GameLauncher().FindExecutable(folder.DataPath);

        Assert.NotEqual(gameClientPath, result);
    }
}

using System.Diagnostics;

namespace OctoHD.Core.Services;

public sealed class GameLauncher
{
    private const string LauncherFileName = "OctoLauncher.exe";

    public string? FindExecutable(string dataFolder)
    {
        var installDirectory = Directory.GetParent(Path.GetFullPath(dataFolder))?.FullName;
        if (installDirectory is null)
        {
            return null;
        }

        return GetCandidatePaths(installDirectory).FirstOrDefault(File.Exists);
    }

    public void Launch(string dataFolder)
    {
        var executable = FindExecutable(dataFolder)
            ?? throw new PatchOperationException(
                "OctoLauncher.exe was not found. Install the official OctoWoW launcher or place its portable executable in the OctoWoW folder.");
        if (!OperatingSystem.IsWindows())
        {
            throw new PatchOperationException("Launching OctoLauncher.exe through Wine or CrossOver requires an explicit launcher configuration.");
        }

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable),
            UseShellExecute = true
        });
        if (process is null)
        {
            throw new PatchOperationException("OctoLauncher could not be opened.");
        }
    }

    private static IEnumerable<string> GetCandidatePaths(string installDirectory)
    {
        yield return Path.Combine(installDirectory, LauncherFileName);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "OctoLauncher", LauncherFileName);
            yield return Path.Combine(localAppData, "OctoLauncher", LauncherFileName);
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "OctoLauncher", LauncherFileName);
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "OctoLauncher", LauncherFileName);
        }
    }
}

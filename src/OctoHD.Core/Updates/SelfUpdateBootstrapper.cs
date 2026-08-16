using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace OctoHD.Core.Updates;

public static class SelfUpdateBootstrapper
{
    private const string ApplyArgument = "--octohd-apply-update";
    private const string SkipOnceArgument = "--octohd-skip-update-once";
    private const string UpdatedArgument = "--octohd-updated";

    public static string UpdatesDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OctoHD",
        "updates");

    internal static string PendingPath => Path.Combine(UpdatesDirectory, "pending-v1.json");

    public static bool TryHandleStartup(string[] args)
    {
        CleanupOldHelpers();
        if (args.Length >= 4 && string.Equals(args[0], ApplyArgument, StringComparison.Ordinal))
        {
            ApplyUpdateAsHelper(args);
            return true;
        }

        if (args.Contains(SkipOnceArgument, StringComparer.Ordinal))
        {
            return false;
        }

        if (!File.Exists(PendingPath))
        {
            return false;
        }

        try
        {
            var pending = LoadPendingAsync().GetAwaiter().GetResult();
            if (pending is null)
            {
                return false;
            }

            var entryAssembly = Assembly.GetEntryAssembly() ?? typeof(SelfUpdateBootstrapper).Assembly;
            var informationalVersion = entryAssembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                .Split('+')[0];
            if (SemanticVersion.TryParse(pending.Version, out var pendingVersion)
                && SemanticVersion.TryParse(informationalVersion, out var runningVersion)
                && runningVersion.CompareTo(pendingVersion) >= 0)
            {
                DeletePendingFiles(pending);
                return false;
            }

            return TryStartPendingUpdate(out _);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryStartPendingUpdate(out string? error)
    {
        error = null;
        try
        {
            var pending = LoadPendingAsync().GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("No downloaded update is pending.");
            ValidatePending(pending, validateCurrentTarget: true);
            var processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The current executable path is unavailable.");
            Directory.CreateDirectory(UpdatesDirectory);
            var helperExtension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
            var helperPath = Path.Combine(UpdatesDirectory, $"helper-{Guid.NewGuid():N}{helperExtension}");
            File.Copy(processPath, helperPath, true);
            EnsureExecutable(helperPath);

            var startInfo = new ProcessStartInfo(helperPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = UpdatesDirectory
            };
            startInfo.ArgumentList.Add(ApplyArgument);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add(PendingPath);
            startInfo.ArgumentList.Add(pending.TargetPath);
            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The update helper could not be started.");
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    internal static async Task<PendingUpdateDocument?> LoadPendingAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(PendingPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(PendingPath);
        var document = await JsonSerializer.DeserializeAsync(
            stream,
            SelfUpdateJsonContext.Default.PendingUpdateDocument,
            cancellationToken).ConfigureAwait(false);
        return document?.SchemaVersion == 1 ? document : null;
    }

    internal static async Task SavePendingAsync(
        PendingUpdateDocument document,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(UpdatesDirectory);
        var temporaryPath = Path.Combine(UpdatesDirectory, $"pending-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    SelfUpdateJsonContext.Default.PendingUpdateDocument,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, PendingPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static string ResolveCurrentTargetPath()
    {
        if (OperatingSystem.IsLinux()
            && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage)
        {
            return Path.GetFullPath(appImage);
        }

        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path is unavailable.");
        if (OperatingSystem.IsMacOS())
        {
            var current = new DirectoryInfo(Path.GetDirectoryName(processPath)!);
            while (current is not null)
            {
                if (current.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("The macOS app bundle could not be located.");
        }

        return Path.GetFullPath(processPath);
    }

    private static void ApplyUpdateAsHelper(string[] args)
    {
        _ = int.TryParse(args[1], out var parentProcessId);
        var pendingPath = Path.GetFullPath(args[2]);
        var expectedTarget = Path.GetFullPath(args[3]);
        PendingUpdateDocument? pending = null;
        try
        {
            WaitForParent(parentProcessId);
            if (!string.Equals(pendingPath, PendingPath, PathComparison))
            {
                throw new InvalidOperationException("The pending update path is invalid.");
            }

            pending = LoadPendingAsync().GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("The pending update metadata is missing.");
            ValidatePending(pending, validateCurrentTarget: false);
            if (!string.Equals(Path.GetFullPath(pending.TargetPath), expectedTarget, PathComparison))
            {
                throw new InvalidOperationException("The pending update target changed unexpectedly.");
            }

            var matchesHash = SelfUpdateService
                .MatchesHashAsync(pending.PayloadPath, pending.Sha256, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (!matchesHash)
            {
                throw new InvalidOperationException("The pending update payload failed SHA-256 verification.");
            }

            File.Delete(PendingPath);

            if (string.Equals(pending.PackageKind, "macos-zip", StringComparison.Ordinal))
            {
                ApplyMacBundle(pending);
            }
            else if (string.Equals(pending.PackageKind, "file", StringComparison.Ordinal))
            {
                ApplyFilePackage(pending);
            }
            else
            {
                throw new InvalidOperationException("The pending update package type is unsupported.");
            }

            DeletePendingFiles(pending);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(UpdatesDirectory);
            File.WriteAllText(Path.Combine(UpdatesDirectory, "last-error.txt"), exception.Message);
            if (pending is not null)
            {
                if (!File.Exists(PendingPath))
                {
                    SavePendingAsync(pending).GetAwaiter().GetResult();
                }

                TryLaunch(pending.TargetPath, skipUpdateOnce: true);
            }
        }
    }

    private static void ApplyFilePackage(PendingUpdateDocument pending)
    {
        var targetPath = Path.GetFullPath(pending.TargetPath);
        var newPath = $"{targetPath}.octohd-new";
        var backupPath = $"{targetPath}.octohd-backup";
        File.Copy(pending.PayloadPath, newPath, true);
        EnsureExecutable(newPath);
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        var targetMoved = false;
        try
        {
            if (File.Exists(targetPath))
            {
                File.Move(targetPath, backupPath, true);
                targetMoved = true;
            }

            File.Move(newPath, targetPath, true);
            TryLaunch(targetPath, skipUpdateOnce: false);
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
        catch
        {
            if (targetMoved && File.Exists(backupPath))
            {
                File.Move(backupPath, targetPath, true);
            }

            throw;
        }
        finally
        {
            if (File.Exists(newPath))
            {
                File.Delete(newPath);
            }
        }
    }

    private static void ApplyMacBundle(PendingUpdateDocument pending)
    {
        var targetPath = Path.GetFullPath(pending.TargetPath);
        var parentDirectory = Directory.GetParent(targetPath)?.FullName
            ?? throw new InvalidOperationException("The macOS app parent directory is unavailable.");
        var stagingDirectory = Path.Combine(parentDirectory, $".octohd-update-{Guid.NewGuid():N}");
        var backupPath = $"{targetPath}.octohd-backup";
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            RunTool("/usr/bin/ditto", "-x", "-k", pending.PayloadPath, stagingDirectory);
            var extractedBundle = Directory.GetDirectories(stagingDirectory, "*.app").SingleOrDefault()
                ?? throw new InvalidOperationException("The update ZIP does not contain exactly one app bundle.");
            var executable = Path.Combine(extractedBundle, "Contents", "MacOS", "OctoHD");
            if (!File.Exists(executable))
            {
                throw new InvalidOperationException("The update app bundle has no OctoHD executable.");
            }

            EnsureExecutable(executable);
            RunTool("/usr/bin/codesign", "--verify", "--deep", "--strict", extractedBundle);
            if (Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath, true);
            }

            Directory.Move(targetPath, backupPath);
            try
            {
                Directory.Move(extractedBundle, targetPath);
                TryLaunch(targetPath, skipUpdateOnce: false);
                Directory.Delete(backupPath, true);
            }
            catch
            {
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, true);
                }

                Directory.Move(backupPath, targetPath);
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, true);
            }
        }
    }

    private static void TryLaunch(string targetPath, bool skipUpdateOnce)
    {
        ProcessStartInfo startInfo;
        if (OperatingSystem.IsMacOS() && Directory.Exists(targetPath))
        {
            startInfo = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add(targetPath);
            if (skipUpdateOnce)
            {
                startInfo.ArgumentList.Add("--args");
                startInfo.ArgumentList.Add(SkipOnceArgument);
            }
        }
        else
        {
            startInfo = new ProcessStartInfo(targetPath) { UseShellExecute = false };
            startInfo.ArgumentList.Add(skipUpdateOnce ? SkipOnceArgument : UpdatedArgument);
        }

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The updated OctoHD application could not be started.");
    }

    private static void ValidatePending(PendingUpdateDocument pending, bool validateCurrentTarget)
    {
        if (pending.SchemaVersion != 1
            || !SemanticVersion.TryParse(pending.Version, out _)
            || !File.Exists(pending.PayloadPath))
        {
            throw new InvalidOperationException("The pending update metadata is invalid.");
        }

        var payloadPath = Path.GetFullPath(pending.PayloadPath);
        var updateRoot = Path.GetFullPath(UpdatesDirectory) + Path.DirectorySeparatorChar;
        var expectedPackageKind = OperatingSystem.IsMacOS() ? "macos-zip" : "file";
        if (!payloadPath.StartsWith(updateRoot, PathComparison)
            || !string.Equals(pending.PackageKind, expectedPackageKind, StringComparison.Ordinal)
            || validateCurrentTarget
            && !string.Equals(Path.GetFullPath(pending.TargetPath), ResolveCurrentTargetPath(), PathComparison))
        {
            throw new InvalidOperationException("The pending update paths are outside their approved locations.");
        }
    }

    private static void WaitForParent(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using var parent = Process.GetProcessById(processId);
            if (!parent.WaitForExit(60_000))
            {
                throw new TimeoutException("OctoHD did not exit in time for the update.");
            }
        }
        catch (ArgumentException)
        {
            // The parent already exited.
        }
    }

    private static void RunTool(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"The update verifier '{executable}' could not be started.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The update verifier '{Path.GetFileName(executable)}' failed: {error.Trim()}");
        }
    }

    private static void EnsureExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute);
        }
    }

    private static void DeletePendingFiles(PendingUpdateDocument pending)
    {
        if (File.Exists(PendingPath)) File.Delete(PendingPath);
        if (File.Exists(pending.PayloadPath)) File.Delete(pending.PayloadPath);
        var errorPath = Path.Combine(UpdatesDirectory, "last-error.txt");
        if (File.Exists(errorPath)) File.Delete(errorPath);
    }

    private static void CleanupOldHelpers()
    {
        if (!Directory.Exists(UpdatesDirectory))
        {
            return;
        }

        foreach (var helper in Directory.EnumerateFiles(UpdatesDirectory, "helper-*"))
        {
            try
            {
                if (!string.Equals(helper, Environment.ProcessPath, PathComparison))
                {
                    File.Delete(helper);
                }
            }
            catch (IOException)
            {
                // A previous helper can still be finishing its handoff.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup is best effort only.
            }
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

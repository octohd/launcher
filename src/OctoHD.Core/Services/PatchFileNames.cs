using System.Text.RegularExpressions;

namespace OctoHD.Core;

public static partial class PatchFileNames
{
    [GeneratedRegex("^patch-[A-Z]\\.mpq$", RegexOptions.CultureInvariant)]
    private static partial Regex ManagedNameRegex();

    public static string Disabled(string targetFileName)
    {
        ValidateManagedFileName(targetFileName);
        return $"__octohd_{targetFileName}";
    }

    public static void ValidateManagedFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || Path.GetFileName(fileName) != fileName
            || !ManagedNameRegex().IsMatch(fileName))
        {
            throw new ArgumentException($"'{fileName}' is not a valid managed MPQ file name.", nameof(fileName));
        }
    }

    public static string ResolveInsideDataFolder(string dataFolder, string fileName)
    {
        ValidateManagedFileName(fileName);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataFolder));
        var candidate = Path.GetFullPath(Path.Combine(root, fileName));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, comparison))
        {
            throw new InvalidOperationException("The managed patch path escaped the selected Data directory.");
        }

        return candidate;
    }
}

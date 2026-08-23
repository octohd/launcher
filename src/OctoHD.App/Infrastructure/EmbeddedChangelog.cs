using System.Text;

namespace OctoHD.App.Infrastructure;

public sealed record ChangelogEntry(string Version, string Description);

internal static class EmbeddedChangelog
{
    private const string ResourceName = "OctoHD.App.Resources.CHANGELOG.md";

    public static IReadOnlyList<ChangelogEntry> Load()
    {
        using var stream = typeof(EmbeddedChangelog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"The embedded resource '{ResourceName}' is missing.");
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd());
    }

    public static IReadOnlyList<ChangelogEntry> LoadOrFallback()
    {
        try
        {
            return Load();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return
            [
                new ChangelogEntry(
                    "Unavailable",
                    "The release history could not be loaded from this build.")
            ];
        }
    }

    public static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var entries = new List<ChangelogEntry>();
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(markdown);
        var inChangelog = false;
        var changelogHeadingLevel = 0;
        char? fenceMarker = null;
        var lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (TryGetFenceMarker(line, out var marker))
            {
                if (fenceMarker is null)
                {
                    fenceMarker = marker;
                }
                else if (fenceMarker == marker)
                {
                    fenceMarker = null;
                }

                continue;
            }

            if (fenceMarker is not null)
            {
                continue;
            }

            if (TryGetHeading(line, out var headingLevel, out var headingText))
            {
                if (!inChangelog)
                {
                    if (headingLevel is 1 or 2
                        && string.Equals(headingText, "Changelog", StringComparison.OrdinalIgnoreCase))
                    {
                        inChangelog = true;
                        changelogHeadingLevel = headingLevel;
                    }

                    continue;
                }

                if (headingLevel <= changelogHeadingLevel)
                {
                    break;
                }

                continue;
            }

            if (!inChangelog || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                var entry = ParseEntry(trimmed, lineNumber);
                if (!versions.Add(entry.Version))
                {
                    throw new InvalidDataException(
                        $"The changelog contains duplicate version '{entry.Version}' on line {lineNumber}.");
                }

                entries.Add(entry);
                continue;
            }

            if (entries.Count > 0 && char.IsWhiteSpace(line[0]))
            {
                entries[^1] = entries[^1] with
                {
                    Description = $"{entries[^1].Description} {trimmed}"
                };
            }
        }

        if (!inChangelog)
        {
            throw new InvalidDataException("The changelog does not contain a '# Changelog' heading.");
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException("The changelog does not contain any entries.");
        }

        return entries;
    }

    private static ChangelogEntry ParseEntry(string line, int lineNumber)
    {
        const string prefix = "- **";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw MalformedEntry(lineNumber);
        }

        var closingMarker = line.IndexOf("**", prefix.Length, StringComparison.Ordinal);
        if (closingMarker < 0)
        {
            throw MalformedEntry(lineNumber);
        }

        var version = line[prefix.Length..closingMarker].Trim();
        var remainder = line[(closingMarker + 2)..].TrimStart();
        if (version.Length < 2
            || version[0] is not ('v' or 'V')
            || version.Any(char.IsWhiteSpace)
            || !TryRemoveSeparator(remainder, out var description)
            || string.IsNullOrWhiteSpace(description))
        {
            throw MalformedEntry(lineNumber);
        }

        return new ChangelogEntry(version, description.Trim());
    }

    private static bool TryRemoveSeparator(string value, out string description)
    {
        if (value.StartsWith('—') || value.StartsWith('–') || value.StartsWith('-'))
        {
            description = value[1..].TrimStart();
            return true;
        }

        description = string.Empty;
        return false;
    }

    private static bool TryGetFenceMarker(string line, out char marker)
    {
        var trimmed = line.TrimStart();
        marker = trimmed.Length == 0 ? '\0' : trimmed[0];
        return marker is '`' or '~'
            && trimmed.Length >= 3
            && trimmed[1] == marker
            && trimmed[2] == marker;
    }

    private static bool TryGetHeading(string line, out int level, out string text)
    {
        var trimmed = line.Trim();
        level = 0;
        while (level < trimmed.Length && trimmed[level] == '#')
        {
            level++;
        }

        if (level is < 1 or > 6 || level >= trimmed.Length || !char.IsWhiteSpace(trimmed[level]))
        {
            text = string.Empty;
            return false;
        }

        text = trimmed[level..].Trim();
        text = text.TrimEnd('#').TrimEnd();
        return true;
    }

    private static InvalidDataException MalformedEntry(int lineNumber) =>
        new($"Malformed changelog entry on line {lineNumber}. Expected '- **v1.2.3** — Description'.");
}

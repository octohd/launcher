using System.Reflection;
using OctoHD.App.Infrastructure;

namespace OctoHD.App.Tests;

public sealed class EmbeddedChangelogTests
{
    [Fact]
    public void Parse_extracts_only_changelog_entries_and_preserves_order()
    {
        const string markdown = """
            # Project documentation

            ```markdown
            # Changelog
            - **v9.9.9** — Fenced example.
            ```

            # Changelog

            - **v2.0.0-beta.1** — New feature.
            - **v1.4.0** — Older change.

            ### Details

            # Appendix

            - **v0.0.0** — Outside the changelog.
            """;

        var entries = EmbeddedChangelog.Parse(markdown);

        Assert.Collection(
            entries,
            entry => Assert.Equal(
                new ChangelogEntry("v2.0.0-beta.1", "New feature."),
                entry),
            entry => Assert.Equal(
                new ChangelogEntry("v1.4.0", "Older change."),
                entry));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Parse_supports_line_endings_and_continuation_lines(string newLine)
    {
        var markdown = string.Join(
            newLine,
            "# Changelog",
            string.Empty,
            "- **v1.2.0** - A description",
            "  that continues on another line.",
            "- **v1.1.0** – An older change.");

        var entries = EmbeddedChangelog.Parse(markdown);

        Assert.Equal("A description that continues on another line.", entries[0].Description);
        Assert.Equal("An older change.", entries[1].Description);
    }

    [Theory]
    [InlineData("# Project\n\nNo release notes here.", "does not contain")]
    [InlineData("# Changelog\n\n# Next", "does not contain any entries")]
    [InlineData("# Changelog\n- x", "Malformed changelog entry")]
    [InlineData("# Changelog\n- v1.0.0 - Missing emphasis", "Malformed changelog entry")]
    public void Parse_rejects_missing_empty_or_malformed_changelogs(
        string markdown,
        string expectedMessage)
    {
        var exception = Assert.Throws<InvalidDataException>(() => EmbeddedChangelog.Parse(markdown));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_duplicate_versions()
    {
        const string markdown = """
            # Changelog
            - **v1.0.0** — First entry.
            - **V1.0.0** — Duplicate entry.
            """;

        var exception = Assert.Throws<InvalidDataException>(() => EmbeddedChangelog.Parse(markdown));

        Assert.Contains("duplicate version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Embedded_changelog_file_starts_with_the_current_app_version()
    {
        var informationalVersion = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+')[0];

        var entries = EmbeddedChangelog.Load();

        Assert.NotEmpty(entries);
        Assert.Equal($"v{informationalVersion}", entries[0].Version);
        Assert.All(entries, entry =>
        {
            Assert.NotEmpty(entry.Version);
            Assert.NotEmpty(entry.Description);
        });
    }
}

namespace OctoHD.Core.Models;

public sealed record PatchSourceDefinition
{
    public const string ProjectReforgedId = "project-reforged";

    public static PatchSourceDefinition ProjectReforged { get; } = new(
        ProjectReforgedId,
        "Project Reforged",
        new Uri("https://pub-0f05631d243e4046993fc02ca7be9542.r2.dev/patches/"),
        true);

    public PatchSourceDefinition(string id, string displayName, Uri baseUri, bool isOfficial = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(baseUri);

        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Patch source URLs must use absolute HTTPS URLs.", nameof(baseUri));
        }

        if (!string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new ArgumentException(
                "Patch source URLs cannot contain credentials, query strings, or fragments.",
                nameof(baseUri));
        }

        Id = id.Trim();
        DisplayName = displayName.Trim();
        BaseUri = EnsureTrailingSlash(baseUri);
        IsOfficial = isOfficial;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public Uri BaseUri { get; }

    public bool IsOfficial { get; }

    public Uri Resolve(PatchDefinition patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (IsOfficial)
        {
            return patch.DownloadUri;
        }

        var resolved = new Uri(BaseUri, Uri.EscapeDataString(patch.SourceFileName));
        if (!string.Equals(resolved.Scheme, BaseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(resolved.Host, BaseUri.Host, StringComparison.OrdinalIgnoreCase)
            || resolved.Port != BaseUri.Port
            || !resolved.AbsolutePath.StartsWith(BaseUri.AbsolutePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The patch URL escaped its configured source.");
        }

        return resolved;
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
                ? uri.AbsolutePath
                : $"{uri.AbsolutePath}/"
        };
        return builder.Uri;
    }
}

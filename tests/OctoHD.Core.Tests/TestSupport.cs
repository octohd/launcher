using System.Net;
using System.Net.Http.Headers;
using OctoHD.Core.Catalog;
using OctoHD.Core.Models;

namespace OctoHD.Core.Tests;

internal sealed class TemporaryDataFolder : IDisposable
{
    private readonly string _root;

    public TemporaryDataFolder()
    {
        _root = Path.Combine(Path.GetTempPath(), "OctoHD.Core.Tests", Guid.NewGuid().ToString("N"));
        DataPath = Path.Combine(_root, "Data");
        Directory.CreateDirectory(DataPath);
    }

    public string DataPath { get; }

    public void WriteMpq(string fileName, int length = 8)
    {
        if (length < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var content = new byte[length];
        content[0] = (byte)'M';
        content[1] = (byte)'P';
        content[2] = (byte)'Q';
        content[3] = 0x1A;
        File.WriteAllBytes(Path.Combine(DataPath, fileName), content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}

internal sealed class TestCatalog(params PatchDefinition[] patches) : IPatchCatalog
{
    public IReadOnlyList<PatchDefinition> Patches { get; } = patches;

    public PatchDefinition GetById(string patchId) =>
        Patches.Single(patch => string.Equals(patch.Id, patchId, StringComparison.OrdinalIgnoreCase));
}

internal sealed class StaticResponseHandler(byte[] content, string etag = "\"test-etag\"") : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    public Uri? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        LastRequestUri = request.RequestUri;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
            RequestMessage = request
        };
        response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        return Task.FromResult(response);
    }
}

internal static class TestPatches
{
    public static PatchDefinition Create(
        string id = "test-a",
        string source = "patch-A.mpq",
        string target = "patch-B.mpq",
        long size = 8,
        string version = "1.0.0",
        string etag = "\"test-etag\"",
        string[]? dependencies = null,
        string? variantGroup = null,
        string? variantName = null) =>
        new(
            id,
            id,
            "Test patch",
            "Tests",
            source,
            target,
            new Uri($"https://pub-0f05631d243e4046993fc02ca7be9542.r2.dev/patches/{source}"),
            version,
            size,
            etag,
            null,
            variantGroup,
            variantName,
            dependencies ?? [],
            [],
            false,
            false);

    public static byte[] MpqBytes(int length = 8)
    {
        var content = new byte[length];
        content[0] = (byte)'M';
        content[1] = (byte)'P';
        content[2] = (byte)'Q';
        content[3] = 0x1A;
        return content;
    }
}

using OctoHD.Core.Services;

namespace OctoHD.Core.Tests;

public sealed class DataFolderValidatorTests
{
    [Fact]
    public async Task Accepts_writable_data_directory()
    {
        using var folder = new TemporaryDataFolder();

        var result = await new DataFolderValidator().ValidateAsync(folder.DataPath);

        Assert.True(result.IsValid);
        Assert.Equal(Path.GetFullPath(folder.DataPath), result.NormalizedPath);
    }

    [Fact]
    public async Task Rejects_non_data_directory()
    {
        var path = Path.Combine(Path.GetTempPath(), "OctoHD.Core.Tests", Guid.NewGuid().ToString("N"), "Wrong");
        Directory.CreateDirectory(path);
        try
        {
            var result = await new DataFolderValidator().ValidateAsync(path);
            Assert.False(result.IsValid);
        }
        finally
        {
            Directory.Delete(Directory.GetParent(path)!.FullName, true);
        }
    }

    [Fact]
    public async Task Accepts_octowow_installation_directory_and_resolves_data_child()
    {
        using var folder = new TemporaryDataFolder();
        var installationPath = Directory.GetParent(folder.DataPath)!.FullName;

        var result = await new DataFolderValidator().ValidateAsync(installationPath);

        Assert.True(result.IsValid);
        Assert.Equal(Path.GetFullPath(folder.DataPath), result.NormalizedPath);
    }
}

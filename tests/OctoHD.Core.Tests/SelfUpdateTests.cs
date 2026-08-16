using System.Runtime.InteropServices;
using OctoHD.Core.Updates;

namespace OctoHD.Core.Tests;

public sealed class SelfUpdateTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.2", 1)]
    [InlineData("1.2.3", "1.2.3-beta.2", 1)]
    [InlineData("1.2.3-beta.2", "1.2.3-beta.10", -1)]
    [InlineData("1.2.3+build.8", "1.2.3", 0)]
    public void Semantic_versions_are_compared_correctly(string left, string right, int expectedSign)
    {
        Assert.True(SemanticVersion.TryParse(left, out var leftVersion));
        Assert.True(SemanticVersion.TryParse(right, out var rightVersion));

        Assert.Equal(expectedSign, Math.Sign(leftVersion.CompareTo(rightVersion)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    public void Invalid_semantic_versions_are_rejected(string value) =>
        Assert.False(SemanticVersion.TryParse(value, out _));

    [Fact]
    public void Package_name_matches_the_running_platform()
    {
        var assetName = SelfUpdateService.ResolveAssetName("2.4.0");
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException()
        };
        var platform = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsLinux() ? "linux" : "macos";

        Assert.Equal(
            $"OctoHD-2.4.0-{platform}-{architecture}{(OperatingSystem.IsWindows() ? ".exe" : OperatingSystem.IsLinux() ? ".AppImage" : ".zip")}",
            assetName);
    }
}

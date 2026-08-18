using OctoHD.App.ViewModels;
using OctoHD.Core.Models;

namespace OctoHD.App.Tests;

public sealed class PatchItemViewModelTests
{
    [Fact]
    public void Definition_and_initial_state_are_exposed_for_the_view()
    {
        var patch = AppTestFactory.Patch(
            variantName: "Ultra",
            dependencies: ["base"],
            isHeavy: true);
        var item = CreateItem(patch, "Requires: Base");

        Assert.Equal(patch.DisplayName, item.Title);
        Assert.Equal(patch.Description, item.Description);
        Assert.Equal(patch.Category, item.Category);
        Assert.Equal("v1.2.3", item.VersionText);
        Assert.Equal("Ultra", item.VariantText);
        Assert.True(item.IsHeavy);
        Assert.StartsWith("1", item.SizeText);
        Assert.EndsWith(" KB", item.SizeText);
        Assert.True(item.HasDependencies);
        Assert.Equal("Requires: Base", item.DependencyText);
        Assert.Equal("Checking…", item.StatusText);
        Assert.Equal("#245E7A", item.StatusBackground);
        Assert.Equal("INSTALL ULTRA", item.ActionLabel);
        Assert.False(item.IsInstalled);
        Assert.False(item.CanInstall);
        Assert.False(item.CanToggle);
    }

    [Theory]
    [InlineData(PatchStatus.NotInstalled, "Not installed", "#303B44")]
    [InlineData(PatchStatus.Active, "Enabled", "#246B46")]
    [InlineData(PatchStatus.Disabled, "Disabled", "#4C5861")]
    [InlineData(PatchStatus.UpdateAvailableActive, "Update available · enabled", "#8A641F")]
    [InlineData(PatchStatus.UpdateAvailableDisabled, "Update available · disabled", "#8A641F")]
    [InlineData(PatchStatus.Conflict, "File conflict", "#7A2929")]
    [InlineData(PatchStatus.ForeignFile, "Unknown file detected", "#7A2929")]
    [InlineData(PatchStatus.Corrupt, "File damaged", "#7A2929")]
    [InlineData(PatchStatus.Checking, "Checking…", "#245E7A")]
    [InlineData(PatchStatus.Busy, "Processing…", "#245E7A")]
    [InlineData(PatchStatus.Error, "Error", "#7A2929")]
    public void Scan_status_controls_labels_and_colors(
        PatchStatus status,
        string expectedText,
        string expectedBackground)
    {
        var item = CreateItem();

        item.ApplyScanResult(new PatchScanResult(item.Definition, status, Message: "Detail"));

        Assert.Equal(expectedText, item.StatusText);
        Assert.Equal(expectedBackground, item.StatusBackground);
        Assert.Equal("Detail", item.DetailMessage);
        Assert.True(item.HasDetailMessage);
        Assert.Equal(
            status is PatchStatus.Conflict or PatchStatus.ForeignFile or PatchStatus.Corrupt or PatchStatus.Error
                ? "#F28B79"
                : "#8FA4B3",
            item.DetailForeground);
    }

    [Fact]
    public void Installed_patch_can_be_reinstalled_from_a_custom_source()
    {
        var item = CreateItem();
        item.ApplyScanResult(new PatchScanResult(
            item.Definition,
            PatchStatus.Active,
            InstalledSourceId: PatchSourceDefinition.ProjectReforgedId));
        var source = new PatchSourceItemViewModel(new PatchSourceDefinition(
            "custom-source",
            "Community Bucket",
            new Uri("https://cdn.example.test/patches")));

        item.SetPatchSource(source);

        Assert.Equal("COMMUNITY BUCKET", item.SourceLabel);
        Assert.Equal("Source size", item.SizeText);
        Assert.False(item.IsInstalledFromSelectedSource);
        Assert.True(item.IsInstalled);
        Assert.True(item.IsEnabled);
        Assert.True(item.CanInstall);
        Assert.True(item.CanToggle);
        Assert.True(item.ShowInstallButton);
        Assert.Equal("REINSTALL", item.ActionLabel);
    }

    [Fact]
    public void Operations_publish_progress_and_errors()
    {
        var item = CreateItem();
        var changes = new HashSet<string?>();
        item.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        item.ApplyScanResult(new PatchScanResult(item.Definition, PatchStatus.UpdateAvailableDisabled));

        item.BeginOperation("Preparing…");
        Assert.True(item.IsBusy);
        Assert.Equal("Preparing…", item.OperationText);
        Assert.True(item.HasOperationText);
        Assert.False(item.CanInstall);
        Assert.False(item.CanToggle);

        item.ApplyProgress(new PatchOperationProgress(item.Definition.Id, "Download", 512, 1024, 2048));
        Assert.Equal(50, item.Progress);
        Assert.Contains("50%", item.OperationText);
        Assert.Contains("KB/s", item.OperationText);

        item.ApplyProgress(new PatchOperationProgress(item.Definition.Id, "Verifying", 0, null, 0));
        Assert.Equal("Verifying", item.OperationText);

        item.EndWithError("Network unavailable");
        Assert.False(item.IsBusy);
        Assert.Equal("Network unavailable", item.StatusText);
        Assert.Equal("Network unavailable", item.DetailMessage);
        Assert.Equal("#7A2929", item.StatusBackground);
        Assert.Equal("#F28B79", item.DetailForeground);
        Assert.Contains(nameof(PatchItemViewModel.StatusText), changes);
        Assert.Contains(nameof(PatchItemViewModel.Progress), changes);

        item.ApplyScanResult(new PatchScanResult(item.Definition, PatchStatus.Active));
        Assert.Equal(0, item.Progress);
        Assert.False(item.HasOperationText);
        Assert.False(item.HasDetailMessage);
    }

    [Fact]
    public void Update_status_uses_update_action()
    {
        var item = CreateItem(AppTestFactory.Patch(isCore: true));

        item.ApplyScanResult(new PatchScanResult(item.Definition, PatchStatus.UpdateAvailableActive));

        Assert.Equal("CORE", item.VariantText);
        Assert.True(item.IsUpdateAvailable);
        Assert.True(item.CanInstall);
        Assert.True(item.ShowInstallButton);
        Assert.Equal("UPDATE", item.ActionLabel);
    }

    [Fact]
    public void Patch_source_view_model_describes_official_and_custom_sources()
    {
        var official = new PatchSourceItemViewModel(PatchSourceDefinition.ProjectReforged);
        var custom = new PatchSourceItemViewModel(new PatchSourceDefinition(
            "custom",
            "Custom",
            new Uri("https://example.test/bucket")));

        Assert.Equal(PatchSourceDefinition.ProjectReforgedId, official.Id);
        Assert.True(official.IsOfficial);
        Assert.Equal("Direct and catalog verified", official.DetailText);
        Assert.Equal(official.DisplayName, official.ToString());
        Assert.False(custom.IsOfficial);
        Assert.Equal("https://example.test/bucket/", custom.BaseUrl);
        Assert.Equal("Custom HTTPS bucket · MPQ checked", custom.DetailText);
        Assert.Same(custom.Definition, custom.Definition);
    }

    private static PatchItemViewModel CreateItem(
        PatchDefinition? patch = null,
        string dependencyText = "") =>
        new(patch ?? AppTestFactory.Patch(), dependencyText, _ => Task.CompletedTask, _ => Task.CompletedTask);
}

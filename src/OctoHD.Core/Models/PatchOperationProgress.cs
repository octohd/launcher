namespace OctoHD.Core.Models;

public sealed record PatchOperationProgress(
    string PatchId,
    string Phase,
    long BytesReceived,
    long? TotalBytes,
    double BytesPerSecond)
{
    public double Percentage => TotalBytes is > 0
        ? Math.Clamp(BytesReceived * 100d / TotalBytes.Value, 0d, 100d)
        : 0d;
}

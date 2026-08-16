namespace OctoHD.Core.Services;

public static class MpqValidator
{
    public static async Task<bool> HasValidSignatureAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < 4)
        {
            return false;
        }

        var signature = new byte[4];
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var read = await stream.ReadAsync(signature, cancellationToken).ConfigureAwait(false);
        return read == 4
            && signature[0] == (byte)'M'
            && signature[1] == (byte)'P'
            && signature[2] == (byte)'Q'
            && signature[3] is 0x1A or 0x1B;
    }
}

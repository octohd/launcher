namespace OctoHD.Core.Services;

public sealed class PatchOperationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

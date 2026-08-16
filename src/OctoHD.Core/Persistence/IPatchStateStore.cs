namespace OctoHD.Core.Persistence;

public interface IPatchStateStore
{
    Task<LocalStateDocument> LoadAsync(string dataFolder, CancellationToken cancellationToken = default);

    Task SaveAsync(string dataFolder, LocalStateDocument state, CancellationToken cancellationToken = default);
}

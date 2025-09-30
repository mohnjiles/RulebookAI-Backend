namespace ShadowrunAi.Core.Abstractions;

public interface IStorageService
{
    Task<string> SaveAsync(string sessionId, Stream content, string fileName, CancellationToken cancellationToken = default);

    Task<Stream?> GetAsync(string sessionId, string fileName, CancellationToken cancellationToken = default);

    Task<Stream?> GetByBlobNameAsync(string blobName, CancellationToken cancellationToken = default);

    Task DeleteAsync(string sessionId, string fileName, CancellationToken cancellationToken = default);
}


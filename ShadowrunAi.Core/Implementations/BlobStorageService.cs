using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using ShadowrunAi.Core.Abstractions;

namespace ShadowrunAi.Core.Implementations;

public class BlobStorageService(BlobServiceClient blobServiceClient, string containerName) : IStorageService
{
    private readonly BlobContainerClient _containerClient = blobServiceClient.GetBlobContainerClient(containerName);

    public async Task<string> SaveAsync(string sessionId, Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobName = $"{sessionId}/{Guid.NewGuid()}-{fileName}";
        var blobClient = _containerClient.GetBlockBlobClient(blobName);
        await blobClient.UploadAsync(content, cancellationToken: cancellationToken);
        return blobName;
    }

    public async Task<Stream?> GetAsync(string sessionId, string fileName, CancellationToken cancellationToken = default)
    {
        var blobClient = _containerClient.GetBlobClient($"{sessionId}/{fileName}");
        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var memoryStream = new MemoryStream();
        await blobClient.DownloadToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task<Stream?> GetByBlobNameAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var blobClient = _containerClient.GetBlobClient(blobName);
        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var memoryStream = new MemoryStream();
        await blobClient.DownloadToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task DeleteAsync(string sessionId, string fileName, CancellationToken cancellationToken = default)
    {
        var blobClient = _containerClient.GetBlobClient($"{sessionId}/{fileName}");
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }
}


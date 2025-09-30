using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using ShadowrunAi.Core.Abstractions;
using ShadowrunAi.Core.Models;

namespace ShadowrunAi.Core.Implementations;

public class CosmosDbService(
    CosmosClient cosmosClient,
    string databaseId,
    string containerId,
    ILogger<CosmosDbService> logger)
    : IDataService
{
    private readonly Container _container = cosmosClient.GetContainer(databaseId, containerId);

    public async Task<ChatSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<ChatSession>(sessionId.ToString(), new PartitionKey(sessionId.ToString()), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            logger.LogError(ex, "Failed to read session {SessionId} from Cosmos DB", sessionId);
            throw;
        }
    }

    public async Task UpsertSessionAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        session.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await _container.UpsertItemAsync(session, new PartitionKey(session.Id.ToString()), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            logger.LogError(ex, "Failed to upsert session {SessionId}", session.Id);
            throw;
        }
    }

    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.DeleteItemAsync<ChatSession>(sessionId.ToString(), new PartitionKey(sessionId.ToString()), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("Attempted to delete missing session {SessionId}", sessionId);
        }
        catch (CosmosException ex)
        {
            logger.LogError(ex, "Failed to delete session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<IReadOnlyList<ChatSessionSummary>> ListSessionsAsync(string accountId, Guid? currentSessionId = null, CancellationToken cancellationToken = default)
    {
        // Only return sessions owned by this account. Seed/shared sessions (null accountId) are excluded from UI lists.
        var query = new QueryDefinition("SELECT c.id, c.title, c.createdAt, c.updatedAt, ARRAY_LENGTH(c.turns) AS messageCount, c.providerCacheId, c.providerFileId FROM c WHERE c.accountId = @accountId")
            .WithParameter("@accountId", accountId);

        var iterator = _container.GetItemQueryIterator<ChatSessionSummary>(query);
        var results = new List<ChatSessionSummary>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response.Resource);
        }

        if (currentSessionId.HasValue && results.All(r => r.Id != currentSessionId.Value))
        {
            var current = await GetSessionAsync(currentSessionId.Value, cancellationToken);
            if (current is not null)
            {
                results.Add(new ChatSessionSummary
                {
                    Id = current.Id,
                    Title = current.Title,
                    CreatedAt = current.CreatedAt,
                    UpdatedAt = current.UpdatedAt,
                    MessageCount = current.Turns.Count,
                    ProviderCacheId = current.ProviderCacheId,
                    ProviderFileId = current.ProviderFileId
                });
            }
        }

        return results
            .OrderByDescending(r => r.UpdatedAt)
            .ToList();
    }
}


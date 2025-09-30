using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShadowrunAi.Core.Options;

namespace ShadowrunAi.Core.Implementations;

public class CosmosInitializer(
    CosmosClient cosmosClient,
    IOptions<CosmosOptions> cosmosOptions,
    ILogger<CosmosInitializer> logger)
    : IHostedService
{
    private readonly CosmosOptions _options = cosmosOptions.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var databaseResponse = await cosmosClient.CreateDatabaseIfNotExistsAsync(_options.DatabaseId, cancellationToken: cancellationToken);

            await databaseResponse.Database.CreateContainerIfNotExistsAsync(
                _options.ContainerId,
                _options.PartitionKeyPath);

            logger.LogInformation(
                "Cosmos DB initialized. Database: {DatabaseId}, Container: {ContainerId}, PartitionKey: {PartitionKey}",
                _options.DatabaseId,
                _options.ContainerId,
                _options.PartitionKeyPath);
        }
        catch (CosmosException ex)
        {
            logger.LogError(ex, "Failed to initialize Cosmos resources");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}



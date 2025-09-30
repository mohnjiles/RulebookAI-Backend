using Microsoft.Extensions.Configuration;
using Azure.Storage.Blobs;
using System.Net.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Refit;
using ShadowrunAi.Core.Abstractions;
using ShadowrunAi.Core.Implementations;
using ShadowrunAi.Core.Options;

namespace ShadowrunAi.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShadowrunCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.Configure<CosmosOptions>(configuration.GetSection(CosmosOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        services
            .AddRefitClient<IGeminiApi>()
            .ConfigureHttpClient((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUri);
            })
            .AddPolicyHandler((provider, _) =>
            {
                var options = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .WaitAndRetryAsync(options.RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
            });

        services.AddSingleton<IGenerativeAiService, GeminiService>();

        services.AddSingleton<IDataService>(provider =>
        {
            var cosmosOptions = provider.GetRequiredService<IOptions<CosmosOptions>>().Value;
            var client = provider.GetRequiredService<CosmosClient>();
            var logger = provider.GetRequiredService<ILogger<CosmosDbService>>();
            return new CosmosDbService(client, cosmosOptions.DatabaseId, cosmosOptions.ContainerId, logger);
        });

        services.AddSingleton<IStorageService>(provider =>
        {
            var storageOptions = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
            var blobServiceClient = provider.GetRequiredService<BlobServiceClient>();
            return new BlobStorageService(blobServiceClient, storageOptions.ContainerName);
        });

        services.AddHostedService<CosmosInitializer>();

        return services;
    }
}


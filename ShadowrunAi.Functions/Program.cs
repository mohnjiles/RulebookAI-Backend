using Azure.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShadowrunAi.Core.Extensions;
using ShadowrunAi.Functions.Models;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.UseFunctionsAuthorization();
builder.Services.AddFunctionsAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtFunctionsBearer(options =>
{
    options.Authority = builder.Configuration["OpenId:Authority"] ?? throw new ArgumentException("OpenId:Authority");
    options.Audience = builder.Configuration["OpenId:ClientId"] ?? throw new ArgumentException("OpenId:ClientId");
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.RoleClaimType = "role";
    options.MapInboundClaims = false;
});
builder.Services.AddFunctionsAuthorization();

builder.Services.AddAutoMapper(typeof(Program));
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddHttpClient();

builder.Services.AddOptions<OpenIdSettings>().Configure<IConfiguration>((settings, config) =>
    config.GetSection("OpenId").Bind(settings));

builder.Services.AddAzureClients(clientBuilder =>
{
    var storageSection = builder.Configuration.GetSection("Storage");
    var storageConnection = builder.Configuration.GetConnectionString("Storage")
        ?? storageSection["ConnectionString"];

    if (!string.IsNullOrWhiteSpace(storageConnection))
    {
        clientBuilder.AddBlobServiceClient(storageConnection);
    }
    else if (Uri.TryCreate(storageSection["ServiceUri"], UriKind.Absolute, out var blobServiceUri))
    {
        clientBuilder.AddBlobServiceClient(blobServiceUri).WithCredential(new DefaultAzureCredential());
    }
    else
    {
        throw new InvalidOperationException("Storage configuration is missing a connection string or service URI.");
    }

    var cosmosConnection = builder.Configuration.GetConnectionString("Cosmos")
        ?? builder.Configuration["Cosmos:ConnectionString"];

    var cosmosOptions = builder.Configuration.GetSection("Cosmos");
    clientBuilder.AddClient<CosmosClient, CosmosClientOptions>((options, _, _) =>
    {
        // Ensure camelCase JSON so documents include "id", "createdAt", etc.
        options.SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        };

        if (!string.IsNullOrWhiteSpace(cosmosConnection))
        {
            return new CosmosClient(cosmosConnection, options);
        }

        var accountEndpoint = cosmosOptions["AccountEndpoint"];
        if (Uri.TryCreate(accountEndpoint, UriKind.Absolute, out var endpointUri))
        {
            return new CosmosClient(endpointUri.ToString(), new DefaultAzureCredential(), options);
        }

        throw new InvalidOperationException("Cosmos configuration is missing a connection string or account endpoint.");
    });
});

builder.Services.AddShadowrunCore(builder.Configuration);

builder.Build().Run();

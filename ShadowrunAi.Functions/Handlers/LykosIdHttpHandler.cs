using System.Net.Http.Headers;
using Duende.IdentityModel.Client;
using Microsoft.Extensions.Options;
using ShadowrunAi.Functions.Models;

namespace ShadowrunAi.Functions.Handlers;

public class LykosIdHttpHandler(IOptions<OpenIdSettings> openIdOptions, IHttpClientFactory httpClientFactory)
    : DelegatingHandler
{
    private readonly OpenIdSettings openIdSettings = openIdOptions.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var tokenRequest = new ClientCredentialsTokenRequest
        {
            Address = $"{openIdSettings.Authority}/connect/token",
            ClientId = openIdSettings.ClientId,
            ClientSecret = openIdSettings.ClientSecret,
            Scope = "api"
        };

        var httpClient = httpClientFactory.CreateClient();

        var tokenResponse =
            await httpClient.RequestClientCredentialsTokenAsync(tokenRequest, cancellationToken: cancellationToken);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);

        return await base.SendAsync(request, cancellationToken);
    }
}



using System.Security.Claims;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore;

namespace ShadowrunAi.Functions.Http;

public abstract class FunctionBase
{
    protected internal static string? GetAccountIdFromClaims(HttpRequestData req)
    {
        // Prefer ClaimsPrincipal from ASP.NET Core HttpContext when available
        ClaimsPrincipal? principal = null;
        try
        {
            var httpContext = req.FunctionContext.GetHttpContext();
            principal = httpContext?.User;
        }
        catch
        {
            // ignore and fall back
        }

        principal ??= req.FunctionContext.Features.Get<ClaimsPrincipal>();
        if (principal?.Identity is not { IsAuthenticated: true }) return null;

        // Prefer OpenID Connect subject, but fall back to other common identifiers
        var id = principal.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
                 ?? principal.Claims.FirstOrDefault(c => c.Type == "oid")?.Value
                 ?? principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                 ?? principal.Claims.FirstOrDefault(c => c.Type == "nameidentifier")?.Value
                 ?? principal.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value
                 ?? principal.Claims.FirstOrDefault(c => c.Type == "email")?.Value
                 ?? principal.Claims.FirstOrDefault(c => c.Type == "upn")?.Value;

        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    protected internal static bool IsBackendService(HttpRequestData req)
    {
        ClaimsPrincipal? principal = null;
        try
        {
            var httpContext = req.FunctionContext.GetHttpContext();
            principal = httpContext?.User;
        }
        catch
        {
        }

        principal ??= req.FunctionContext.Features.Get<ClaimsPrincipal>();
        if (principal?.Identity is not { IsAuthenticated: true }) return false;

        var clientOriginClaim = principal.Claims.FirstOrDefault(c => c.Type == "client_origin_type");
        if (clientOriginClaim is null)
            return false;

        return clientOriginClaim.Value == "backend_service";
    }
}



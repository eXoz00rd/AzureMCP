using AzureDevOpsServer.Mcp.Http;
using Microsoft.AspNetCore.Builder;

namespace AzureDevOpsServer.Mcp.Configuration;

public static class HttpServerConfiguration
{
    public const string HealthPath = "/healthz";

    public static void MapAzureDevOpsHttpEndpoints(this WebApplication app, AzureDevOpsServerOptions options)
    {
        if (!options.HttpPath.StartsWith('/'))
        {
            throw new InvalidOperationException($"{AzureDevOpsServerOptions.HttpPathVariable} must start with '/'.");
        }

        // Routing runs first, so the guard protects whichever endpoint it picked, however the request spelled the path.
        // The health endpoint carries no guard metadata, so probes reach it without a token.
        app.UseRouting();
        app.UseMiddleware<HttpAccessMiddleware>(options);

        app.MapHealthChecks(HealthPath);
        app.MapMcp(options.HttpPath).WithMetadata(new HttpAccessGuardMetadata());
    }
}

using AzureDevOpsServer.Mcp.Http;
using Microsoft.AspNetCore.Builder;

namespace AzureDevOpsServer.Mcp.Configuration;

public static class HttpServerConfiguration
{
    public static IEndpointConventionBuilder MapAzureDevOpsMcp(this WebApplication app, AzureDevOpsServerOptions options)
    {
        if (!options.HttpPath.StartsWith('/'))
        {
            throw new InvalidOperationException($"{AzureDevOpsServerOptions.HttpPathVariable} must start with '/'.");
        }

        // Routing runs first, so the guard protects whichever endpoint it picked, however the request spelled the path.
        app.UseRouting();
        app.UseMiddleware<HttpAccessMiddleware>(options);

        return app.MapMcp(options.HttpPath).WithMetadata(new HttpAccessGuardMetadata());
    }
}

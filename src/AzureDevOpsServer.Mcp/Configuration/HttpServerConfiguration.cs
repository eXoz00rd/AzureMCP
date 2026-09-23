using AzureDevOpsServer.Mcp.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AzureDevOpsServer.Mcp.Configuration;

public static class HttpServerConfiguration
{
    public static IEndpointConventionBuilder MapAzureDevOpsMcp(this WebApplication app, AzureDevOpsServerOptions options)
    {
        if (!options.HttpPath.StartsWith('/'))
        {
            throw new InvalidOperationException($"{AzureDevOpsServerOptions.HttpPathVariable} must start with '/'.");
        }

        var path = new PathString(options.HttpPath);

        // Only the MCP endpoint is guarded, so endpoints such as health probes can be reached without the token.
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(path),
            branch => branch.UseMiddleware<HttpAccessMiddleware>(options)
        );

        return app.MapMcp(options.HttpPath);
    }
}

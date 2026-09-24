using AzureDevOpsServer.Mcp.Http;
using AzureDevOpsServer.Mcp.OpenWebUi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using ModelContextProtocol.Protocol;

namespace AzureDevOpsServer.Mcp.Configuration;

public static class HttpServerConfiguration
{
    public const string HealthPath = "/healthz";
    public const string PluginPath = "/openwebui/azure_devops.py";

    public static void MapAzureDevOpsHttpEndpoints(this WebApplication app, AzureDevOpsServerOptions options)
    {
        if (!options.HttpPath.StartsWith('/'))
        {
            throw new InvalidOperationException($"{AzureDevOpsServerOptions.HttpPathVariable} must start with '/'.");
        }

        if (string.Equals(options.HttpPath.TrimEnd('/'), HealthPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{AzureDevOpsServerOptions.HttpPathVariable} must not be {HealthPath}, which serves the health probe."
            );
        }

        if (options.HttpServePlugin && string.Equals(options.HttpPath.TrimEnd('/'), PluginPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{AzureDevOpsServerOptions.HttpPathVariable} must not be {PluginPath}, which serves the Open WebUI plugin. " +
                $"Choose another path or set {AzureDevOpsServerOptions.HttpServePluginVariable}=false."
            );
        }

        // Routing runs first, so the guard protects whichever endpoint it picked, however the request spelled the path.
        // The health and plugin endpoints carry no guard metadata, so they are reachable without a token.
        app.UseRouting();
        app.UseMiddleware<HttpAccessMiddleware>(options);

        app.MapHealthChecks(HealthPath);
        app.MapMcp(options.HttpPath).WithMetadata(new HttpAccessGuardMetadata());

        if (options.HttpServePlugin)
        {
            MapPlugin(app, options);
        }
    }

    // Open WebUI fetches an imported link from its backend without any credential, and the plugin holds no secret:
    // its server URL is the address the caller used, and the token and PATs are entered in Open WebUI afterwards.
    private static void MapPlugin(WebApplication app, AzureDevOpsServerOptions options)
    {
        var tools = new Lazy<IReadOnlyList<Tool>>(() => PluginGenerator.RegisteredTools(app.Services));

        app.MapGet(
            PluginPath,
            context =>
            {
                var settings = PluginSettings.For(options.Toolsets, options.ReadOnly, CallerFacingMcpUrl(context.Request, options.HttpPath));

                context.Response.ContentType = "text/x-python; charset=utf-8";
                return context.Response.WriteAsync(PluginGenerator.Generate(tools.Value, settings), context.RequestAborted);
            }
        );
    }

    // Behind a TLS-terminating proxy the request arrives as plain HTTP, and a plugin pointing there would send the token and PATs
    // unencrypted. Trusting the forwarded headers is safe here because they only shape the answer to the caller who sent them.
    private static string CallerFacingMcpUrl(HttpRequest request, string httpPath)
    {
        var forwardedProto = FirstForwardedValue(request, "X-Forwarded-Proto");
        var scheme = forwardedProto is "http" or "https" ? forwardedProto : request.Scheme;
        var forwardedHost = FirstForwardedValue(request, "X-Forwarded-Host");
        var host = string.IsNullOrEmpty(forwardedHost) ? request.Host : new HostString(forwardedHost);

        return host.HasValue ? UriHelper.BuildAbsolute(scheme, host, request.PathBase, httpPath) : string.Empty;
    }

    private static string? FirstForwardedValue(HttpRequest request, string header)
    {
        return request.Headers[header].ToString().Split(',')[0].Trim().ToLowerInvariant() is { Length: > 0 } value ? value : null;
    }
}

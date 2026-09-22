using AzureDevOpsServer.Mcp.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace AzureDevOpsServer.Mcp.Configuration;

public static class AzureDevOpsHttpClientConfiguration
{
    public static IHttpClientBuilder AddAzureDevOpsHandlers(
        this IHttpClientBuilder builder,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        builder.Services.TryAddTransient<AzureDevOpsAuthenticationHandler>();
        builder.Services.TryAddTransient<ConnectionDiagnosticsHandler>();

        // Later handlers run inside earlier ones, so authentication is resolved again on every retry attempt.
        builder.AddAzureDevOpsResilience(configureResilience);
        builder.AddHttpMessageHandler<AzureDevOpsAuthenticationHandler>();
        builder.AddHttpMessageHandler<ConnectionDiagnosticsHandler>();

        return builder;
    }
}

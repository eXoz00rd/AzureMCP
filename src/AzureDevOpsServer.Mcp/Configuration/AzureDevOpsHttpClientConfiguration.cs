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

        // Later handlers run inside earlier ones: diagnostics explain a failure only once the retries are spent,
        // while authentication sits inside them so the credential is resolved again on every attempt.
        builder.AddHttpMessageHandler<ConnectionDiagnosticsHandler>();
        builder.AddAzureDevOpsResilience(configureResilience);
        builder.AddHttpMessageHandler<AzureDevOpsAuthenticationHandler>();

        return builder;
    }
}

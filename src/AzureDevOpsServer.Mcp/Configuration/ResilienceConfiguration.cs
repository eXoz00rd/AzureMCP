using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace AzureDevOpsServer.Mcp.Configuration;

public static class ResilienceConfiguration
{
    // Azure DevOps Server can accept a write while the response is lost to a transient failure.
    // Retrying POST/PATCH/PUT/DELETE in that case can create duplicate work items, comments,
    // releases, or queued builds, so only safe (idempotent) methods are retried automatically.
    public static IHttpStandardResiliencePipelineBuilder AddAzureDevOpsResilience(
        this IHttpClientBuilder builder,
        Action<HttpStandardResilienceOptions>? configure = null)
    {
        return builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.DisableForUnsafeHttpMethods();
            configure?.Invoke(options);
        });
    }
}

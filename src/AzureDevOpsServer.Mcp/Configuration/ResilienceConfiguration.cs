using System.Security.Authentication;
using AzureDevOpsServer.Mcp.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace AzureDevOpsServer.Mcp.Configuration;

public static class ResilienceConfiguration
{
    // Azure DevOps Server can accept a write while the response is lost to a transient failure.
    // Retrying POST/PATCH/PUT/DELETE in that case can create duplicate work items, comments,
    // releases, or queued builds, so only safe methods are retried automatically.
    public static IHttpStandardResiliencePipelineBuilder AddAzureDevOpsResilience(
        this IHttpClientBuilder builder,
        Action<HttpStandardResilienceOptions>? configure = null)
    {
        return builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.DisableForUnsafeHttpMethods();
            configure?.Invoke(options);

            // A certificate the client does not trust is rejected the same way on every attempt,
            // so retrying it would only postpone the explanation of what has to be fixed.
            var shouldRetry = options.Retry.ShouldHandle;
            options.Retry.ShouldHandle = arguments =>
                ExceptionChain.Inner<AuthenticationException>(arguments.Outcome.Exception) is not null
                    ? PredicateResult.False()
                    : shouldRetry(arguments);
        });
    }
}

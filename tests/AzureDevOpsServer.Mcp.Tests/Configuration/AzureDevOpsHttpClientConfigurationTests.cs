using System.Net;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Configuration;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Configuration;

public sealed class AzureDevOpsHttpClientConfigurationTests
{
    [Fact]
    public async Task RetriedRequest_ResolvesCredentialOnEveryAttempt()
    {
        var credentials = new StubCredentialProvider("attempt-1", "attempt-2", "attempt-3");
        var stub = new StubHttpMessageHandler([
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK)
        ]);
        var services = new ServiceCollection();
        services.AddSingleton<IAzureDevOpsCredentialProvider>(credentials);
        services
            .AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => stub)
            .AddAzureDevOpsHandlers(options =>
            {
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
                options.Retry.MaxRetryAttempts = 2;
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");

        using var response = await client.GetAsync(
            "https://devops.example.local/_apis/projects",
            TestContext.Current.CancellationToken
        );

        // A handler outside the resilience pipeline would resolve once and send "attempt-1" three times.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new string?[]
            {
                StubCredentialProvider.ExpectedAuthorization("attempt-1"),
                StubCredentialProvider.ExpectedAuthorization("attempt-2"),
                StubCredentialProvider.ExpectedAuthorization("attempt-3")
            },
            stub.AuthorizationHeaders
        );
    }
}

using System.Net;
using AzureDevOpsServer.Mcp.Configuration;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Configuration;

public sealed class ResilienceConfigurationTests
{
    private static ServiceProvider CreateProvider(StubHttpMessageHandler stub)
    {
        var services = new ServiceCollection();
        services
            .AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => stub)
            .AddAzureDevOpsResilience(options =>
            {
                // Keep the test fast and pin the retry count so it doesn't drift with the
                // library's default; the predicate under test is unaffected by either setting.
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
                options.Retry.MaxRetryAttempts = 2;
            });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SendAsync_GetWithTransientFailure_IsRetriedUntilSuccess()
    {
        var stub = new StubHttpMessageHandler([
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK)
        ]);
        using var provider = CreateProvider(stub);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");

        using var response = await client.GetAsync(
            "https://devops.example.local/_apis/projects",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, stub.Requests.Count);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task SendAsync_UnsafeMethodWithTransientFailure_IsNotRetried(string method)
    {
        var stub = new StubHttpMessageHandler([new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)]);
        using var provider = CreateProvider(stub);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            "https://devops.example.local/_apis/projects"
        );

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(stub.Requests);
    }
}

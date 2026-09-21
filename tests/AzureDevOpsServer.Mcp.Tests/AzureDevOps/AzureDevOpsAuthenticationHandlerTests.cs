using System.Net;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class AzureDevOpsAuthenticationHandlerTests
{
    private static HttpClient CreateClient(StubCredentialProvider credentials, StubHttpMessageHandler inner)
    {
        var handler = new AzureDevOpsAuthenticationHandler(credentials)
        {
            InnerHandler = inner
        };
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://devops.example.local/DefaultCollection/")
        };
    }

    [Fact]
    public async Task SendAsync_SetsBasicAuthorizationFromProvider()
    {
        var credentials = new StubCredentialProvider("user-pat");
        var inner = new StubHttpMessageHandler([new HttpResponseMessage(HttpStatusCode.OK)]);
        using var client = CreateClient(credentials, inner);

        using var response = await client.GetAsync("_apis/projects", TestContext.Current.CancellationToken);

        Assert.Equal(StubCredentialProvider.ExpectedAuthorization("user-pat"), Assert.Single(inner.AuthorizationHeaders));
    }

    [Fact]
    public async Task SendAsync_ResolvesCredentialForEachRequest()
    {
        var credentials = new StubCredentialProvider("first-pat", "second-pat");
        var inner = new StubHttpMessageHandler([
            new HttpResponseMessage(HttpStatusCode.OK),
            new HttpResponseMessage(HttpStatusCode.OK)
        ]);
        using var client = CreateClient(credentials, inner);

        using var first = await client.GetAsync("_apis/projects", TestContext.Current.CancellationToken);
        using var second = await client.GetAsync("_apis/projects", TestContext.Current.CancellationToken);

        Assert.Equal(
            new string?[]
            {
                StubCredentialProvider.ExpectedAuthorization("first-pat"),
                StubCredentialProvider.ExpectedAuthorization("second-pat")
            },
            inner.AuthorizationHeaders
        );
    }
}

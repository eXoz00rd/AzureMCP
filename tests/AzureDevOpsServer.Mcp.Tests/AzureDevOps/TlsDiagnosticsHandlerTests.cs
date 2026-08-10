using System.Net;
using System.Security.Authentication;
using AzureDevOpsServer.Mcp.AzureDevOps;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class TlsDiagnosticsHandlerTests
{
    private static HttpClient CreateClient(HttpMessageHandler innerHandler)
    {
        var handler = new TlsDiagnosticsHandler
        {
            InnerHandler = innerHandler
        };
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://devops.example.local/DefaultCollection/")
        };
    }

    [Fact]
    public async Task SendAsync_WithCertificateFailure_ThrowsActionableMessage()
    {
        var inner = new HttpRequestException(
            "The SSL connection could not be established.",
            new AuthenticationException("The remote certificate is invalid according to the validation procedure.")
        );
        using var client = CreateClient(new ThrowingHandler(inner));

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetAsync("_apis/projects", TestContext.Current.CancellationToken));

        Assert.Contains("devops.example.local", exception.Message);
        Assert.Contains("internal certificate authority", exception.Message);
        Assert.Contains("remote certificate is invalid", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WithUnrelatedFailure_LetsExceptionThrough()
    {
        var inner = new HttpRequestException("Connection refused.");
        using var client = CreateClient(new ThrowingHandler(inner));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("_apis/projects", TestContext.Current.CancellationToken));

        Assert.Equal("Connection refused.", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WithSuccess_PassesResponseThrough()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var client = CreateClient(new StaticHandler(response));

        using var result = await client.GetAsync("_apis/projects", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }

    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StaticHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }
}

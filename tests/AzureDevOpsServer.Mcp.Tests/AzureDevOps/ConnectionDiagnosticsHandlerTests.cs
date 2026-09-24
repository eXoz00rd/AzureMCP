using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using AzureDevOpsServer.Mcp.AzureDevOps;
using Polly.Timeout;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class ConnectionDiagnosticsHandlerTests
{
    private static HttpClient CreateClient(HttpMessageHandler innerHandler)
    {
        var handler = new ConnectionDiagnosticsHandler
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

    [Fact]
    public async Task SendAsync_WithUnreachableHost_ThrowsActionableMessage()
    {
        var inner = new HttpRequestException(
            "No connection could be made because the target machine actively refused it.",
            new SocketException(10061)
        );
        using var client = CreateClient(new ThrowingHandler(inner));

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetAsync("_apis/projects", TestContext.Current.CancellationToken));

        Assert.Contains("devops.example.local", exception.Message);
        Assert.Contains("could not be reached", exception.Message);
        Assert.Contains("the port is reachable", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WithReadThatTimesOut_ThrowsActionableMessage()
    {
        using var client = CreateClient(new ThrowingHandler(new TimeoutRejectedException(TimeSpan.FromSeconds(10))));

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetAsync("_apis/projects", TestContext.Current.CancellationToken));

        Assert.Contains("https://devops.example.local did not answer within 10 seconds", exception.Message);
        Assert.Contains("firewall or proxy", exception.Message);
        Assert.DoesNotContain("may still have been applied", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WithWriteThatTimesOut_WarnsItMayHaveBeenApplied()
    {
        using var client = CreateClient(new ThrowingHandler(new TimeoutRejectedException(TimeSpan.FromSeconds(10))));
        using var content = new StringContent("[]");

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.PostAsync("_apis/wit/workitems/$Bug", content, TestContext.Current.CancellationToken));

        Assert.Contains("may still have been applied", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WithWebPageInsteadOfApiData_ThrowsActionableMessage()
    {
        using var response = HtmlResponse(HttpStatusCode.OK);
        using var client = CreateClient(new StaticHandler(response));

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetAsync("_apis/projects?api-version=7.0", TestContext.Current.CancellationToken));

        Assert.Contains("answered https://devops.example.local/DefaultCollection/_apis/projects with a web page", exception.Message);
        Assert.Contains("ADOS_COLLECTION_URL", exception.Message);
        Assert.DoesNotContain("redirecting", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WithRedirectToWebPage_NamesWhereItLandedWithoutItsQuery()
    {
        using var response = HtmlResponse(HttpStatusCode.OK);
        using var signIn = new HttpRequestMessage(HttpMethod.Get, "https://devops.example.local/_signin?ReturnUrl=%2FDefaultCollection");
        response.RequestMessage = signIn;
        using var client = CreateClient(new StaticHandler(response));

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetAsync("_apis/projects", TestContext.Current.CancellationToken));

        Assert.Contains("after redirecting it to https://devops.example.local/_signin.", exception.Message);
        Assert.DoesNotContain("ReturnUrl", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.NonAuthoritativeInformation)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task SendAsync_WithWebPageTheClientExplains_PassesResponseThrough(HttpStatusCode status)
    {
        using var response = HtmlResponse(status);
        using var client = CreateClient(new StaticHandler(response));

        using var result = await client.GetAsync("_apis/projects", TestContext.Current.CancellationToken);

        Assert.Equal(status, result.StatusCode);
    }

    private static HttpResponseMessage HtmlResponse(HttpStatusCode status)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent("<!DOCTYPE html><html><body>Sign in</body></html>", System.Text.Encoding.UTF8, "text/html")
        };
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

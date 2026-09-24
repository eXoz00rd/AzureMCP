using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Configuration;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Configuration;

public sealed class AzureDevOpsHttpClientConfigurationTests
{
    [Fact]
    public async Task UnreachableHost_IsRetriedBeforeItIsReported()
    {
        var primary = new CountingThrowingHandler(
            new HttpRequestException("Connection refused.", new SocketException(111))
        );
        var services = new ServiceCollection();
        services.AddSingleton<IAzureDevOpsCredentialProvider>(new StubCredentialProvider("pat"));
        services
            .AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => primary)
            .AddAzureDevOpsHandlers(options =>
            {
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
                options.Retry.MaxRetryAttempts = 2;
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetAsync(
                "https://devops.example.local/_apis/projects",
                TestContext.Current.CancellationToken
            )
        );

        // Diagnostics run outside the pipeline, so a transient failure is still retried and only the last one is explained.
        Assert.Equal(3, primary.Attempts);
        Assert.Contains("could not be reached", exception.Message);
    }

    [Fact]
    public async Task UntrustedCertificate_IsReportedWithoutRetrying()
    {
        var primary = new CountingThrowingHandler(
            new HttpRequestException(
                "The SSL connection could not be established.",
                new AuthenticationException("The remote certificate is invalid.")
            )
        );
        var services = new ServiceCollection();
        services.AddSingleton<IAzureDevOpsCredentialProvider>(new StubCredentialProvider("pat"));
        services
            .AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => primary)
            .AddAzureDevOpsHandlers(options =>
            {
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
                options.Retry.MaxRetryAttempts = 2;
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetAsync(
                "https://devops.example.local/_apis/projects",
                TestContext.Current.CancellationToken
            )
        );

        // The same certificate is rejected on every attempt, so retrying would only delay the explanation.
        Assert.Equal(1, primary.Attempts);
        Assert.Contains("TLS connection", exception.Message);
    }

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

    [Fact]
    public async Task SilentServer_IsRetriedAndThenReportedAsATimeout()
    {
        var primary = new SilentHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IAzureDevOpsCredentialProvider>(new StubCredentialProvider("pat"));
        services
            .AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => primary)
            .AddAzureDevOpsHandlers(options =>
            {
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
                options.Retry.MaxRetryAttempts = 2;
                options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(100);
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetAsync("https://devops.example.local/_apis/projects", TestContext.Current.CancellationToken)
        );

        // The timeout surfaces through the resilience pipeline as the exception diagnostics explain.
        Assert.Equal(3, primary.Attempts);
        Assert.Contains("https://devops.example.local did not answer within", exception.Message);
    }

    [Fact]
    public async Task WebPageAnswer_IsReportedWithoutRetrying()
    {
        var primary = new WebPageHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IAzureDevOpsCredentialProvider>(new StubCredentialProvider("pat"));
        services
            .AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => primary)
            .AddAzureDevOpsHandlers(options => options.Retry.Delay = TimeSpan.Zero);
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetAsync("https://devops.example.local/_apis/projects", TestContext.Current.CancellationToken)
        );

        Assert.Equal(1, primary.Attempts);
        Assert.Contains("with a web page instead of API data", exception.Message);
    }

    private sealed class SilentHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
        }
    }

    private sealed class WebPageHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>Sign in</body></html>", System.Text.Encoding.UTF8, "text/html"),
                RequestMessage = request
            });
        }
    }

    private sealed class CountingThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public CountingThrowingHandler(Exception exception)
        {
            _exception = exception;
        }

        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            throw _exception;
        }
    }
}

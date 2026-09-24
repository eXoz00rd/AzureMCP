using System.Net;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Http;

public sealed class HttpAccessMiddlewareTests
{
    private const string Token = "shared-test-token";

    [Fact]
    public async Task Request_WithoutToken_IsRejectedWithBearerChallenge()
    {
        await using var server = await ProbeServer.StartAsync(new AzureDevOpsServerOptions { HttpToken = Token });

        using var response = await server.PostToolCallAsync(authorization: null, origin: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal(0, server.Calls.Count);
    }

    [Fact]
    public async Task Request_WithWrongToken_IsRejectedBeforeAnyToolRuns()
    {
        await using var server = await ProbeServer.StartAsync(new AzureDevOpsServerOptions { HttpToken = Token });

        using var response = await server.PostToolCallAsync("Bearer not-the-token", origin: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, server.Calls.Count);

        // The same tool is reachable once the token is right, so the zero above is the guard's doing.
        await using var client = await server.ConnectAsync(Token);
        await client.CallToolAsync(CountingTool.Name, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, server.Calls.Count);
    }

    [Fact]
    public async Task Client_WithToken_CompletesHandshakeAndCallsTool()
    {
        await using var server = await ProbeServer.StartAsync(new AzureDevOpsServerOptions { HttpToken = Token });

        await using var client = await server.ConnectAsync(Token);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var result = await client.CallToolAsync(CountingTool.Name, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, tool => tool.Name == CountingTool.Name);
        Assert.True(result.IsError is null or false);
        Assert.Equal(1, server.Calls.Count);
    }

    [Fact]
    public async Task Request_FromDisallowedOrigin_IsForbidden()
    {
        await using var server = await ProbeServer.StartAsync(
            new AzureDevOpsServerOptions { HttpToken = Token, HttpAllowedOrigins = "https://chat.example.local" }
        );

        using var response = await server.PostToolCallAsync($"Bearer {Token}", "https://attacker.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, server.Calls.Count);
    }

    [Fact]
    public async Task Request_FromAllowedOrigin_PassesTheGuard()
    {
        await using var server = await ProbeServer.StartAsync(
            new AzureDevOpsServerOptions { HttpToken = Token, HttpAllowedOrigins = "https://chat.example.local/" }
        );

        using var response = await server.PostToolCallAsync($"Bearer {Token}", "https://chat.example.local");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Client_OnAnonymousEndpoint_NeedsNoToken()
    {
        await using var server = await ProbeServer.StartAsync(new AzureDevOpsServerOptions { HttpAllowAnonymous = true });

        await using var client = await server.ConnectAsync(token: null);
        await client.CallToolAsync(CountingTool.Name, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, server.Calls.Count);
    }

    [Theory]
    [InlineData("/mcp/", "/mcp")]
    [InlineData("/mcp", "/mcp/")]
    [InlineData("/mcp", "/MCP")]
    public async Task Request_ThatRoutingSendsToMcp_IsGuardedWhateverItsSpelling(string configuredPath, string requestPath)
    {
        await using var server = await ProbeServer.StartAsync(new AzureDevOpsServerOptions { HttpToken = Token, HttpPath = configuredPath });

        using var response = await server.PostToolCallAsync(authorization: null, origin: null, requestPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, server.Calls.Count);
    }

    [Fact]
    public async Task HealthEndpoint_NeedsNoTokenEvenWhenMcpIsServedAtTheRoot()
    {
        await using var server = await ProbeServer.StartAsync(new AzureDevOpsServerOptions { HttpToken = Token, HttpPath = "/" });
        using var http = new HttpClient();

        using var health = await http.GetAsync(
            new Uri(server.Endpoint, HttpServerConfiguration.HealthPath),
            TestContext.Current.CancellationToken
        );
        using var mcp = await server.PostToolCallAsync(authorization: null, origin: null);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/HEALTHZ/")]
    public async Task McpPath_ThatWouldShareTheHealthProbeRoute_IsRejected(string httpPath)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ProbeServer.StartAsync(new AzureDevOpsServerOptions { HttpToken = Token, HttpPath = httpPath })
        );

        Assert.Contains(AzureDevOpsServerOptions.HttpPathVariable, exception.Message);
        Assert.Contains(HttpServerConfiguration.HealthPath, exception.Message);
    }

    private sealed class ProbeServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _http = new();

        private ProbeServer(WebApplication app, Uri endpoint)
        {
            _app = app;
            Endpoint = endpoint;
            Calls = app.Services.GetRequiredService<CallCounter>();
        }

        public Uri Endpoint { get; }

        public CallCounter Calls { get; }

        public static async Task<ProbeServer> StartAsync(AzureDevOpsServerOptions options)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton<CallCounter>();
            ToolRegistration.AddTools(builder.Services, [typeof(CountingTool)], readOnly: false);
            builder.Services.AddHealthChecks();
            builder.Services.AddMcpServer().WithHttpTransport(transport => transport.Stateless = true);

            var app = builder.Build();
            app.MapAzureDevOpsHttpEndpoints(options);
            await app.StartAsync(TestContext.Current.CancellationToken);

            return new ProbeServer(app, new Uri(app.Urls.First() + options.HttpPath));
        }

        public Task<HttpResponseMessage> PostToolCallAsync(string? authorization, string? origin, string? path = null)
        {
            var target = path is null ? Endpoint : new Uri(Endpoint, path);
            var request = new HttpRequestMessage(HttpMethod.Post, target)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(
                        new
                        {
                            jsonrpc = "2.0",
                            id = 1,
                            method = "tools/call",
                            @params = new { name = CountingTool.Name, arguments = new { } }
                        }
                    ),
                    Encoding.UTF8,
                    "application/json"
                )
            };
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            if (authorization is not null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
            }

            if (origin is not null)
            {
                request.Headers.TryAddWithoutValidation("Origin", origin);
            }

            return _http.SendAsync(request, TestContext.Current.CancellationToken);
        }

        public Task<McpClient> ConnectAsync(string? token)
        {
            var options = new HttpClientTransportOptions
            {
                Endpoint = Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = token is null ?
                    null :
                    new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
            };

            return McpClient.CreateAsync(new HttpClientTransport(options), cancellationToken: TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            await _app.StopAsync(TestContext.Current.CancellationToken);
            await _app.DisposeAsync();
        }
    }

    public sealed class CallCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Increment()
        {
            Interlocked.Increment(ref _count);
        }
    }

    public sealed class CountingTool
    {
        public const string Name = "count_call";

        private readonly CallCounter _counter;

        public CountingTool(CallCounter counter)
        {
            _counter = counter;
        }

        [McpServerTool(Name = Name)]
        public string Count()
        {
            _counter.Increment();
            return "counted";
        }
    }
}

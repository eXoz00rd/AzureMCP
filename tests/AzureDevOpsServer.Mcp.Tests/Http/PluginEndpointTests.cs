using System.Net;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Http;

public sealed class PluginEndpointTests
{
    private const string Token = "shared-test-token";

    [Fact]
    public async Task Plugin_IsServedWithoutTokenAndPointsAtTheMcpEndpoint()
    {
        await using var server = await PluginServer.StartAsync(new AzureDevOpsServerOptions { HttpToken = Token });

        using var response = await server.GetPluginAsync();
        var plugin = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/x-python", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"default=\"{server.McpEndpoint}\",", plugin);
        Assert.Contains("    async def list_projects(\n", plugin);
        Assert.DoesNotContain(Token, plugin);
        Assert.DoesNotContain("%%", plugin);
    }

    [Fact]
    public async Task Plugin_BehindATlsTerminatingProxy_PointsAtTheAddressTheCallerUsed()
    {
        await using var server = await PluginServer.StartAsync(new AzureDevOpsServerOptions { HttpToken = Token });

        using var response = await server.GetPluginAsync(
            ("X-Forwarded-Proto", "https, http"),
            ("X-Forwarded-Host", "azuremcp.example.local")
        );
        var plugin = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("default=\"https://azuremcp.example.local/mcp\",", plugin);
    }

    [Fact]
    public async Task Plugin_WithAnUnknownForwardedScheme_KeepsTheRequestScheme()
    {
        await using var server = await PluginServer.StartAsync(new AzureDevOpsServerOptions { HttpToken = Token });

        using var response = await server.GetPluginAsync(("X-Forwarded-Proto", "javascript"));
        var plugin = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains($"default=\"{server.McpEndpoint}\",", plugin);
    }

    [Fact]
    public async Task Plugin_OffersExactlyTheToolsTheServerLists()
    {
        await using var server = await PluginServer.StartAsync(
            new AzureDevOpsServerOptions { HttpToken = Token, Toolsets = "workitems", ReadOnly = true }
        );

        using var response = await server.GetPluginAsync();
        var plugin = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        await using var client = await server.ConnectAsync();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var offered = plugin.Split('\n')
                            .Where(line => line.StartsWith("    async def ", StringComparison.Ordinal))
                            .Select(line => line["    async def ".Length..].TrimEnd('('))
                            .Where(name => !name.StartsWith('_'))
                            .Order(StringComparer.Ordinal);
        Assert.Equal(tools.Select(tool => tool.Name).Order(StringComparer.Ordinal), offered);
        Assert.Contains("Toolsets: workitems (read-only).", plugin);
        Assert.DoesNotContain("    async def update_work_item(\n", plugin);
    }

    [Fact]
    public async Task Plugin_WhenDisabled_IsNotServed()
    {
        await using var server = await PluginServer.StartAsync(
            new AzureDevOpsServerOptions { HttpToken = Token, HttpServePlugin = false }
        );

        using var response = await server.GetPluginAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/openwebui/azure_devops.py")]
    [InlineData("/OPENWEBUI/azure_devops.py/")]
    public async Task McpPath_ThatWouldShareThePluginRoute_IsRejected(string httpPath)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PluginServer.StartAsync(new AzureDevOpsServerOptions { HttpToken = Token, HttpPath = httpPath })
        );

        Assert.Contains(AzureDevOpsServerOptions.HttpPathVariable, exception.Message);
        Assert.Contains(AzureDevOpsServerOptions.HttpServePluginVariable, exception.Message);
    }

    [Fact]
    public async Task McpPath_OnThePluginRoute_IsAllowedOnceThePluginIsNotServed()
    {
        await using var server = await PluginServer.StartAsync(
            new AzureDevOpsServerOptions { HttpToken = Token, HttpPath = HttpServerConfiguration.PluginPath, HttpServePlugin = false }
        );
        await using var client = await server.ConnectAsync();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(tools);
    }

    private sealed class PluginServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _http = new();

        private PluginServer(WebApplication app, Uri baseAddress, string httpPath)
        {
            _app = app;
            BaseAddress = baseAddress;
            McpEndpoint = new Uri(baseAddress, httpPath);
        }

        public Uri BaseAddress { get; }

        public Uri McpEndpoint { get; }

        public static async Task<PluginServer> StartAsync(AzureDevOpsServerOptions options)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            ToolRegistration.AddTools(builder.Services, options);
            builder.Services.AddHealthChecks();
            builder.Services.AddMcpServer().WithHttpTransport(transport => transport.Stateless = true);

            var app = builder.Build();
            app.MapAzureDevOpsHttpEndpoints(options);
            await app.StartAsync(TestContext.Current.CancellationToken);

            return new PluginServer(app, new Uri(app.Urls.First()), options.HttpPath);
        }

        public Task<HttpResponseMessage> GetPluginAsync(params (string Name, string Value)[] headers)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress, HttpServerConfiguration.PluginPath));
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            return _http.SendAsync(request, TestContext.Current.CancellationToken);
        }

        public Task<McpClient> ConnectAsync()
        {
            var options = new HttpClientTransportOptions
            {
                Endpoint = McpEndpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {Token}" }
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
}

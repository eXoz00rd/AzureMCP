using System.IO.Pipelines;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Configuration;

public sealed class ToolScopeTests
{
    [Fact]
    public async Task ToolInstances_ResolveDependenciesFromEachRequestScope()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        var cancellationToken = cancellation.Token;

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddScoped<ScopeProbe>();
        ToolRegistration.AddTools(builder.Services, [typeof(ScopeProbeTool)], readOnly: false);
        builder.Services
               .AddMcpServer()
               .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());

        using var host = builder.Build();
        await host.StartAsync(cancellationToken);

        string first;
        string second;
        await using (var client = await McpClient.CreateAsync(
                         new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
                         cancellationToken: cancellationToken
                     ))
        {
            first = await CallScopeProbeAsync(client, cancellationToken);
            second = await CallScopeProbeAsync(client, cancellationToken);
        }

        await host.StopAsync(cancellationToken);

        // Resolving from the root provider would hand every call the same scoped instance.
        Assert.NotEqual(first, second);
    }

    private static async Task<string> CallScopeProbeAsync(McpClient client, CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync("scope_probe", cancellationToken: cancellationToken);
        Assert.True(result.IsError is null or false, "The scope_probe call failed.");
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }

    public sealed class ScopeProbe
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public sealed class ScopeProbeTool
    {
        private readonly ScopeProbe _probe;

        public ScopeProbeTool(ScopeProbe probe)
        {
            _probe = probe;
        }

        [McpServerTool(Name = "scope_probe", ReadOnly = true)]
        public string GetScopeId()
        {
            return _probe.Id.ToString();
        }
    }
}

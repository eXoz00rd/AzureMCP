using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Configuration;

public sealed class ServerConfigurationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("workitems,repositories", false)]
    [InlineData("builds,releases", true)]
    public void AddAzureDevOpsMcpServer_RegistersSelectedToolsPromptsAndInstructions(string? toolsets, bool readOnly)
    {
        var options = new AzureDevOpsServerOptions { Toolsets = toolsets, ReadOnly = readOnly };
        var expectedToolCount = ToolRegistration.AddTools(new ServiceCollection(), options);
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.AddAzureDevOpsMcpServer(options);
        using var host = builder.Build();

        Assert.Equal(expectedToolCount, host.Services.GetServices<McpServerTool>().Count());
        Assert.NotEmpty(host.Services.GetServices<McpServerPrompt>());
        Assert.Equal(
            ServerInstructions.Build(options, expectedToolCount),
            host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value.ServerInstructions
        );
    }
}

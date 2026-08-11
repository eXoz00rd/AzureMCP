using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Configuration;

public sealed class ToolRegistrationTests
{
    private static IReadOnlyList<McpServerTool> Register(AzureDevOpsServerOptions options)
    {
        var services = new ServiceCollection();
        ToolRegistration.AddTools(services, options);
        using var provider = services.BuildServiceProvider();
        return [.. provider.GetServices<McpServerTool>()];
    }

    [Fact]
    public void AddTools_WithDefaults_RegistersEveryTool()
    {
        var tools = Register(new AzureDevOpsServerOptions());

        Assert.Contains(tools, tool => tool.ProtocolTool.Name == "query_work_items");
        Assert.Contains(tools, tool => tool.ProtocolTool.Name == "update_work_item");
        Assert.Contains(tools, tool => tool.ProtocolTool.Name == "create_or_update_wiki_page");
    }

    [Fact]
    public void AddTools_WithReadOnly_RegistersNoWriteTools()
    {
        var tools = Register(
            new AzureDevOpsServerOptions
            {
                ReadOnly = true
            }
        );

        Assert.All(tools, tool => Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.DoesNotContain(tools, tool => tool.ProtocolTool.Name == "update_work_item");
        Assert.DoesNotContain(tools, tool => tool.ProtocolTool.Name == "create_or_update_wiki_page");
        Assert.Contains(tools, tool => tool.ProtocolTool.Name == "get_work_item");
    }

    [Fact]
    public void AddTools_WithToolsetSelection_RegistersOnlyThoseAreas()
    {
        var tools = Register(
            new AzureDevOpsServerOptions
            {
                Toolsets = "builds"
            }
        );

        var names = tools.Select(tool => tool.ProtocolTool.Name).ToList();
        Assert.Contains("server_info", names);
        Assert.Contains("queue_build", names);
        Assert.DoesNotContain("get_work_item", names);
        Assert.DoesNotContain("list_wikis", names);
    }

    [Fact]
    public void AddTools_WithToolsetSelectionAndReadOnly_CombinesBothFilters()
    {
        var tools = Register(
            new AzureDevOpsServerOptions
            {
                Toolsets = "builds",
                ReadOnly = true
            }
        );

        var names = tools.Select(tool => tool.ProtocolTool.Name).ToList();
        Assert.Contains("list_builds", names);
        Assert.DoesNotContain("queue_build", names);
    }

    [Fact]
    public void AddTools_PublishesOutputSchemasForStructuredResults()
    {
        var tools = Register(new AzureDevOpsServerOptions());

        var listProjects = Assert.Single(tools, tool => tool.ProtocolTool.Name == "list_projects");
        Assert.NotNull(listProjects.ProtocolTool.OutputSchema);
    }
}

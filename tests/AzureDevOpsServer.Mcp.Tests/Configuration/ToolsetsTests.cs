using AzureDevOpsServer.Mcp.Configuration;
using AzureDevOpsServer.Mcp.Tools;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Configuration;

public sealed class ToolsetsTests
{
    [Fact]
    public void Resolve_WithoutSelection_ReturnsEveryToolset()
    {
        var types = Toolsets.Resolve(null);

        Assert.Equal(Toolsets.Names.Count, types.Count);
        Assert.Contains(typeof(WikiTools), types);
        Assert.Contains(typeof(ServerInfoTool), types);
    }

    [Fact]
    public void Resolve_WithSelection_KeepsServerToolsetAndSelectedOnes()
    {
        var types = Toolsets.Resolve("workitems, pullrequests");

        Assert.Equal(3, types.Count);
        Assert.Contains(typeof(ServerInfoTool), types);
        Assert.Contains(typeof(WorkItemTools), types);
        Assert.Contains(typeof(PullRequestTools), types);
        Assert.DoesNotContain(typeof(BuildTools), types);
    }

    [Fact]
    public void Resolve_IsCaseInsensitiveAndIgnoresDuplicates()
    {
        var types = Toolsets.Resolve("WIKI,wiki,server");

        Assert.Equal(2, types.Count);
        Assert.Contains(typeof(WikiTools), types);
    }

    [Fact]
    public void Resolve_WithUnknownToolset_ThrowsListingValidNames()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Toolsets.Resolve("workitems,sprints"));

        Assert.Contains("sprints", exception.Message);
        Assert.Contains("pullrequests", exception.Message);
        Assert.Contains(AzureDevOpsServerOptions.ToolsetsVariable, exception.Message);
    }
}

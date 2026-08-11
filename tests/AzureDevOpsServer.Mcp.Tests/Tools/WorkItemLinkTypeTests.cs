using AzureDevOpsServer.Mcp.Tools;
using ModelContextProtocol;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Tools;

public sealed class WorkItemLinkTypeTests
{
    [Theory]
    [InlineData("parent", "System.LinkTypes.Hierarchy-Reverse")]
    [InlineData("CHILD", "System.LinkTypes.Hierarchy-Forward")]
    [InlineData("related", "System.LinkTypes.Related")]
    [InlineData("duplicate", "System.LinkTypes.Duplicate-Forward")]
    [InlineData("predecessor", "System.LinkTypes.Dependency-Reverse")]
    [InlineData("successor", "System.LinkTypes.Dependency-Forward")]
    public void ParseLinkType_MapsFriendlyNames(string input, string expected)
    {
        Assert.Equal(expected, WorkItemTools.ParseLinkType(input));
    }

    [Theory]
    [InlineData("ArtifactLink")]
    [InlineData("System.LinkTypes.Remote.Related")]
    public void ParseLinkType_PassesRawRelationNamesThrough(string input)
    {
        Assert.Equal(input, WorkItemTools.ParseLinkType(input));
    }

    [Theory]
    [InlineData("blocks")]
    [InlineData("")]
    public void ParseLinkType_WithUnknownName_Throws(string input)
    {
        var exception = Assert.Throws<McpException>(() => WorkItemTools.ParseLinkType(input));

        Assert.Contains("parent", exception.Message);
    }
}

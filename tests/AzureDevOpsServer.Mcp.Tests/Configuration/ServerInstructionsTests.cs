using AzureDevOpsServer.Mcp.Configuration;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Configuration;

public sealed class ServerInstructionsTests
{
    [Fact]
    public void Build_WithDefaultProject_MentionsItAndTheToolCount()
    {
        var instructions = ServerInstructions.Build(
            new AzureDevOpsServerOptions
            {
                CollectionUrl = "https://devops.example.local/DefaultCollection",
                DefaultProject = "Alpha"
            },
            59
        );

        Assert.Contains("'Alpha'", instructions);
        Assert.Contains("59 tools", instructions);
        Assert.Contains("get_build_timeline", instructions);
    }

    [Fact]
    public void Build_WithoutDefaultProject_AsksForAProjectArgument()
    {
        var instructions = ServerInstructions.Build(new AzureDevOpsServerOptions(), 10);

        Assert.Contains("No default project is configured", instructions);
    }

    [Fact]
    public void Build_WithReadOnly_StatesTheRestrictionAndDropsWriteGuidance()
    {
        var instructions = ServerInstructions.Build(
            new AzureDevOpsServerOptions
            {
                ReadOnly = true
            },
            40
        );

        Assert.Contains("read-only mode", instructions);
        Assert.DoesNotContain("Do not vote on pull requests", instructions);
    }

    [Fact]
    public void Build_WithWriteAccess_WarnsAboutSideEffects()
    {
        var instructions = ServerInstructions.Build(new AzureDevOpsServerOptions(), 59);

        Assert.Contains("Do not vote on pull requests", instructions);
        Assert.Contains("replaces the whole page content", instructions);
    }
}

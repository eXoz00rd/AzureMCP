using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tools;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Tools;

public sealed class BuildToolsTests : ToolTestsBase
{
    [Fact]
    public async Task ListBuildsAsync_WithoutTop_UsesTwenty()
    {
        using var response = EmptyList();
        var harness = CreateHarness("FallbackProject", response);
        var tools = new BuildTools(harness.Client, harness.Options);

        await tools.ListBuildsAsync(null, null, null, TestContext.Current.CancellationToken);

        Assert.Contains("/FallbackProject/_apis/build/builds", harness.RequestUri);
        Assert.Contains("$top=20", harness.RequestUri);
        Assert.DoesNotContain("definitions=", harness.RequestUri);
    }

    [Fact]
    public async Task ListBuildDefinitionsAsync_WithoutAnyProject_Throws()
    {
        var harness = CreateHarness(null);
        var tools = new BuildTools(harness.Client, harness.Options);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => tools.ListBuildDefinitionsAsync(null, TestContext.Current.CancellationToken));

        Assert.Contains("ADOS_DEFAULT_PROJECT", exception.Message);
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task GetBuildLogAsync_WithoutMaxChars_UsesDefaultLimit()
    {
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 45_000))
        };
        var harness = CreateHarness("FallbackProject", response);
        var tools = new BuildTools(harness.Client, harness.Options);

        var log = await tools.GetBuildLogAsync(500, 7, null, null, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(ResponseLimits.DefaultMaxChars, log.Content.Length);
        Assert.True(log.Truncated);
        Assert.Equal(45_000, log.TotalChars);
    }
}

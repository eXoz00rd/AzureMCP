using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tools;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Tools;

public sealed class LimitValidationTests : ToolTestsBase
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ResponseLimits.MaxTop + 1)]
    public async Task ListBranchesAsync_WithInvalidTop_FailsBeforeSendingRequest(int top)
    {
        var harness = CreateHarness("FallbackProject");
        var tools = new RepositoryTools(harness.Client, harness.Options);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => tools.ListBranchesAsync("WebApp", top, null, TestContext.Current.CancellationToken)
        );

        Assert.Empty(harness.Handler.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ResponseLimits.MaxMaxChars + 1)]
    public async Task GetFileContentAsync_WithInvalidMaxChars_FailsBeforeSendingRequest(int maxChars)
    {
        var harness = CreateHarness("FallbackProject");
        var tools = new RepositoryTools(harness.Client, harness.Options);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => tools.GetFileContentAsync(
                "WebApp",
                "/README.md",
                null,
                maxChars,
                null,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Empty(harness.Handler.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ResponseLimits.MaxMaxItems + 1)]
    public async Task ListRepositoryItemsAsync_WithInvalidMaxItems_FailsBeforeSendingRequest(int maxItems)
    {
        var harness = CreateHarness("FallbackProject");
        var tools = new RepositoryTools(harness.Client, harness.Options);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => tools.ListRepositoryItemsAsync(
                "WebApp",
                null,
                null,
                null,
                maxItems,
                null,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Empty(harness.Handler.Requests);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ResponseLimits.MaxDepth + 1)]
    public async Task ListQueriesAsync_WithInvalidDepth_FailsBeforeSendingRequest(int depth)
    {
        var harness = CreateHarness("FallbackProject");
        var tools = new QueryTools(harness.Client, harness.Options);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => tools.ListQueriesAsync(depth, null, TestContext.Current.CancellationToken)
        );

        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task ListBranchesAsync_AtRangeBoundaries_SendsRequest()
    {
        using var minimum = EmptyList();
        var harness = CreateHarness("FallbackProject", minimum);
        var tools = new RepositoryTools(harness.Client, harness.Options);

        await tools.ListBranchesAsync("WebApp", ResponseLimits.MinTop, null, TestContext.Current.CancellationToken);

        Assert.Contains($"$top={ResponseLimits.MinTop}", harness.RequestUri);
    }

    [Fact]
    public async Task ListBranchesAsync_AtMaximumTop_SendsRequest()
    {
        using var maximum = EmptyList();
        var harness = CreateHarness("FallbackProject", maximum);
        var tools = new RepositoryTools(harness.Client, harness.Options);

        await tools.ListBranchesAsync("WebApp", ResponseLimits.MaxTop, null, TestContext.Current.CancellationToken);

        Assert.Contains($"$top={ResponseLimits.MaxTop}", harness.RequestUri);
    }
}

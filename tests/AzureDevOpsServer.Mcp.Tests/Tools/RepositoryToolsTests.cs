using AzureDevOpsServer.Mcp.Tools;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Tools;

public sealed class RepositoryToolsTests : ToolTestsBase
{
    [Fact]
    public async Task ListBranchesAsync_WithoutTop_UsesDefaultListTop()
    {
        using var response = EmptyList();
        var harness = CreateHarness("FallbackProject", response);
        var tools = new RepositoryTools(harness.Client, harness.Options);

        await tools.ListBranchesAsync("WebApp", null, null, TestContext.Current.CancellationToken);

        Assert.Contains("/FallbackProject/_apis/git/repositories/WebApp/refs", harness.RequestUri);
        Assert.Contains("$top=100", harness.RequestUri);
    }

    [Fact]
    public async Task ListCommitsAsync_WithoutTop_UsesTwenty()
    {
        using var response = EmptyList();
        var harness = CreateHarness("FallbackProject", response);
        var tools = new RepositoryTools(harness.Client, harness.Options);

        await tools.ListCommitsAsync("WebApp", null, null, null, null, TestContext.Current.CancellationToken);

        Assert.Contains("searchCriteria.$top=20", harness.RequestUri);
    }

    [Fact]
    public async Task GetFileContentAsync_WithoutMaxChars_UsesDefaultLimit()
    {
        using var response = JsonResponse($"{{ \"path\": \"/big.txt\", \"content\": \"{new string('a', 40_000)}\" }}");
        var harness = CreateHarness("FallbackProject", response);
        var tools = new RepositoryTools(harness.Client, harness.Options);

        var file = await tools.GetFileContentAsync(
            "WebApp",
            "/big.txt",
            null,
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(30_000, file.Content!.Length);
        Assert.True(file.Truncated);
        Assert.Equal(40_000, file.TotalChars);
    }

    [Fact]
    public async Task ListRepositoryItemsAsync_WithoutRecursive_RequestsOneLevel()
    {
        using var response = EmptyList();
        var harness = CreateHarness(null, response);
        var tools = new RepositoryTools(harness.Client, harness.Options);

        await tools.ListRepositoryItemsAsync(
            "WebApp",
            null,
            null,
            null,
            null,
            "Explicit",
            TestContext.Current.CancellationToken);

        Assert.Contains("recursionLevel=oneLevel", harness.RequestUri);
        Assert.Contains("/Explicit/_apis/git/repositories/WebApp/items", harness.RequestUri);
    }
}

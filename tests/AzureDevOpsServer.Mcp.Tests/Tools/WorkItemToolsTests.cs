using System.Text.Json;
using AzureDevOpsServer.Mcp.Tools;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Tools;

public sealed class WorkItemToolsTests : ToolTestsBase
{
    private const string WorkItemJson =
        """{ "id": 1, "rev": 1, "fields": { "System.Title": "Title" }, "url": "https://devops.example.local/_apis/wit/workItems/1" }""";

    [Fact]
    public async Task CreateWorkItemAsync_MergesTitleWithExtraFields()
    {
        using var response = JsonResponse(WorkItemJson);
        var harness = CreateHarness("FallbackProject", response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        await tools.CreateWorkItemAsync(
            "Bug",
            "Login fails",
            new Dictionary<string, string> { ["System.Description"] = "Steps to reproduce" },
            null,
            TestContext.Current.CancellationToken);

        Assert.Contains("/FallbackProject/_apis/wit/workitems/$Bug", harness.RequestUri);
        using var body = JsonDocument.Parse(harness.Handler.RequestBodies[0]);
        var paths = body.RootElement.EnumerateArray()
                        .Select(operation => operation.GetProperty("path").GetString())
                        .ToList();
        Assert.Contains("/fields/System.Title", paths);
        Assert.Contains("/fields/System.Description", paths);
    }

    [Fact]
    public async Task AddWorkItemCommentAsync_WritesSystemHistory()
    {
        using var response = JsonResponse(WorkItemJson);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        await tools.AddWorkItemCommentAsync(42, "Looks good", TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(harness.Handler.RequestBodies[0]);
        var operation = Assert.Single(body.RootElement.EnumerateArray());
        Assert.True(operation.GetProperty("path").ValueEquals("/fields/System.History"));
        Assert.True(operation.GetProperty("value").ValueEquals("Looks good"));
    }

    [Fact]
    public async Task QueryWorkItemsAsync_WithoutProject_UsesDefaultProject()
    {
        using var response = JsonResponse("""{ "queryType": "flat", "workItems": [] }""");
        var harness = CreateHarness("FallbackProject", response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        await tools.QueryWorkItemsAsync(
            "SELECT [System.Id] FROM WorkItems",
            null,
            TestContext.Current.CancellationToken);

        Assert.Contains("/FallbackProject/_apis/wit/wiql", harness.RequestUri);
    }
}

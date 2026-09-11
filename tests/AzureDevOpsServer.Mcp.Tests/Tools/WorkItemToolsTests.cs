using System.Text.Json;
using AzureDevOpsServer.Mcp.Tools;
using ModelContextProtocol;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Tools;

public sealed class WorkItemToolsTests : ToolTestsBase
{
    private const string WorkItemJson =
        """{ "id": 1, "rev": 1, "fields": { "System.Title": "Title" }, "url": "https://devops.example.local/_apis/wit/workItems/1" }""";

    [Fact]
    public async Task GetWorkItemAsync_DefaultFormat_ReturnsHtmlUnchanged()
    {
        const string json =
            """{ "id": 1, "rev": 1, "fields": { "System.Title": "Title", "System.Description": "<p>Hello <b>world</b></p>" }, "url": "https://devops.example.local/_apis/wit/workItems/1" }""";
        using var response = JsonResponse(json);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var workItem = await tools.GetWorkItemAsync(1, null, false, null, TestContext.Current.CancellationToken);

        Assert.Equal("<p>Hello <b>world</b></p>", workItem.Fields["System.Description"].GetString());
    }

    [Fact]
    public async Task GetWorkItemAsync_TextFormat_ConvertsHtmlFieldsAndLeavesPlainFieldsUntouched()
    {
        const string json =
            """{ "id": 1, "rev": 1, "fields": { "System.Title": "List<Item> import", "System.Description": "<p>Hello <b>world</b></p>" }, "url": "https://devops.example.local/_apis/wit/workItems/1" }""";
        using var response = JsonResponse(json);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var workItem = await tools.GetWorkItemAsync(1, null, false, "text", TestContext.Current.CancellationToken);

        Assert.Equal("Hello world", workItem.Fields["System.Description"].GetString());
        Assert.Equal("List<Item> import", workItem.Fields["System.Title"].GetString());
    }

    [Fact]
    public async Task GetWorkItemAsync_WithInvalidDescriptionFormat_ThrowsBeforeRequest()
    {
        using var response = JsonResponse(WorkItemJson);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var exception = await Assert.ThrowsAsync<McpException>(() => tools.GetWorkItemAsync(
                1,
                null,
                false,
                "markdown",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("descriptionFormat", exception.Message);
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task GetWorkItemAsync_WithBlankDescriptionFormat_ThrowsInsteadOfDefaulting()
    {
        using var response = JsonResponse(WorkItemJson);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var exception = await Assert.ThrowsAsync<McpException>(() => tools.GetWorkItemAsync(
                1,
                null,
                false,
                "   ",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("descriptionFormat", exception.Message);
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task GetWorkItemsAsync_TextFormat_ConvertsEachItem()
    {
        const string json =
            """{ "count": 1, "value": [ { "id": 1, "rev": 1, "fields": { "System.Description": "<ul><li>One</li><li>Two</li></ul>" }, "url": "https://devops.example.local/_apis/wit/workItems/1" } ] }""";
        using var response = JsonResponse(json);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var workItems = await tools.GetWorkItemsAsync(
            [1],
            null,
            false,
            "text",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("- One\n- Two", Assert.Single(workItems).Fields["System.Description"].GetString());
    }

    [Fact]
    public async Task GetWorkItemRevisionsAsync_TextFormat_ConvertsEachRevision()
    {
        const string json =
            """{ "count": 1, "value": [ { "id": 1, "rev": 1, "fields": { "System.Description": "<p>Rev text</p>" }, "url": "https://devops.example.local/_apis/wit/workItems/1" } ] }""";
        using var response = JsonResponse(json);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var revisions = await tools.GetWorkItemRevisionsAsync(
            1,
            null,
            "text",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("Rev text", Assert.Single(revisions).Fields["System.Description"].GetString());
    }

    [Fact]
    public async Task CreateWorkItemAsync_MergesTitleWithExtraFields()
    {
        using var response = JsonResponse(WorkItemJson);
        var harness = CreateHarness("FallbackProject", response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var result = await tools.CreateWorkItemAsync(
            "Bug",
            "Login fails",
            new Dictionary<string, string> { ["System.Description"] = "Steps to reproduce" },
            null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, result.Id);
        Assert.Equal(1, result.Rev);
        Assert.True(result.Success);
        Assert.Contains("System.Title", result.ChangedFields);
        Assert.Contains("System.Description", result.ChangedFields);
        Assert.Contains("/FallbackProject/_apis/wit/workitems/$Bug", harness.RequestUri);
        using var body = JsonDocument.Parse(harness.Handler.RequestBodies[0]);
        var paths = body.RootElement
                        .EnumerateArray()
                        .Select(operation => operation.GetProperty("path").GetString())
                        .ToList();
        Assert.Contains("/fields/System.Title", paths);
        Assert.Contains("/fields/System.Description", paths);
    }

    [Fact]
    public async Task UpdateWorkItemAsync_ReturnsMinimalResultWithChangedFields()
    {
        using var response = JsonResponse(WorkItemJson);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var result = await tools.UpdateWorkItemAsync(
            1,
            new Dictionary<string, string> { ["System.State"] = "Active" },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, result.Id);
        Assert.Equal(1, result.Rev);
        Assert.True(result.Success);
        Assert.Equal(["System.State"], result.ChangedFields);
    }

    [Fact]
    public async Task LinkWorkItemAsync_ReturnsMinimalResultWithRelation()
    {
        using var response = JsonResponse(WorkItemJson);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var result = await tools.LinkWorkItemAsync(
            1,
            "related",
            2,
            null,
            null,
            null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, result.Id);
        Assert.Equal(1, result.Rev);
        Assert.True(result.Success);
        Assert.Equal(["System.LinkTypes.Related"], result.ChangedFields);
    }

    [Fact]
    public async Task AddWorkItemAttachmentAsync_ReturnsMinimalResult()
    {
        using var uploadResponse = JsonResponse(
            """{ "id": "1a2b3c4d-5e6f-4a8b-9c0d-1e2f3a4b5c6d", "url": "https://devops.example.local/_apis/wit/attachments/1a2b3c4d-5e6f-4a8b-9c0d-1e2f3a4b5c6d" }"""
        );
        using var relationResponse = JsonResponse(WorkItemJson);
        var harness = CreateHarness("FallbackProject", uploadResponse, relationResponse);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var result = await tools.AddWorkItemAttachmentAsync(
            1,
            "build-log.txt",
            "log contents",
            null,
            null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, result.Id);
        Assert.Equal(1, result.Rev);
        Assert.True(result.Success);
        Assert.Equal(["AttachedFile"], result.ChangedFields);
    }

    [Fact]
    public async Task AddWorkItemCommentAsync_PostsToCommentsEndpointAndReturnsId()
    {
        const string commentJson = """{ "commentId": 7, "text": "Looks good" }""";
        using var response = JsonResponse(commentJson);
        var harness = CreateHarness("FallbackProject", response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var comment = await tools.AddWorkItemCommentAsync(
            42,
            "Looks good",
            null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(7, comment.Id);
        Assert.Contains("FallbackProject/_apis/wit/workItems/42/comments", harness.RequestUri);
        using var body = JsonDocument.Parse(harness.Handler.RequestBodies[0]);
        Assert.True(body.RootElement.GetProperty("text").ValueEquals("Looks good"));
    }

    [Fact]
    public async Task GetWorkItemCommentAsync_ReadsCommentById()
    {
        const string commentJson = """{ "id": 7, "text": "Looks good" }""";
        using var response = JsonResponse(commentJson);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var comment = await tools.GetWorkItemCommentAsync(
            42,
            7,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(7, comment.Id);
        Assert.Contains("Alpha/_apis/wit/workItems/42/comments/7", harness.RequestUri);
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
            TestContext.Current.CancellationToken
        );

        Assert.Contains("/FallbackProject/_apis/wit/wiql", harness.RequestUri);
    }

    [Fact]
    public async Task LinkWorkItemAsync_WithVstfsUrl_InfersArtifactLinkName()
    {
        using var response = JsonResponse(WorkItemJson);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        await tools.LinkWorkItemAsync(
            63162,
            "ArtifactLink",
            null,
            "vstfs:///Git/PullRequestId/1a2b3c4d-5e6f-4a8b-9c0d-1e2f3a4b5c6d%2F8f1c0d1e-2b3a-4c5d-9e8f-7a6b5c4d3e2f%2F63162",
            null,
            null,
            TestContext.Current.CancellationToken
        );

        using var body = JsonDocument.Parse(Assert.Single(harness.Handler.RequestBodies));
        var value = Assert.Single(body.RootElement.EnumerateArray()).GetProperty("value");
        Assert.True(value.GetProperty("attributes").GetProperty("name").ValueEquals("Pull Request"));
    }

    [Fact]
    public async Task LinkWorkItemAsync_WithUnrecognisedArtifactUrl_ExplainsMissingName()
    {
        using var response = JsonResponse(WorkItemJson);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var exception = await Assert.ThrowsAsync<McpException>(() => tools.LinkWorkItemAsync(
                63162,
                "ArtifactLink",
                null,
                "https://devops.example.local/DefaultCollection/WebApp/_git/WebApp/pullrequest/63162",
                null,
                null,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("artifactLinkName", exception.Message);
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task LinkWorkItemAsync_WithoutTarget_ExplainsRequiredArguments()
    {
        using var response = JsonResponse(WorkItemJson);
        var harness = CreateHarness(null, response);
        var tools = new WorkItemTools(harness.Client, harness.Options);

        var exception = await Assert.ThrowsAsync<McpException>(() => tools.LinkWorkItemAsync(
                63162,
                "related",
                null,
                null,
                null,
                null,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("targetWorkItemId or targetUrl", exception.Message);
    }
}

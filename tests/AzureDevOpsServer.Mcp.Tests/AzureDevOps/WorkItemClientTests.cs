using System.Net;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class WorkItemClientTests : AzureDevOpsClientTestsBase
{
    [Fact]
    public async Task GetWorkItemCommentsAsync_UsesPreviewApiVersion()
    {
        const string json =
            """
            {
              "totalCount": 1,
              "comments": [
                {
                  "id": 5,
                  "text": "Looks good",
                  "createdBy": { "displayName": "Sebastian", "uniqueName": "sebastian@example.local" },
                  "createdDate": "2026-08-11T10:00:00Z"
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var comments = await client.GetWorkItemCommentsAsync(42, "Alpha", 100, TestContext.Current.CancellationToken);

        Assert.Equal(1, comments.TotalCount);
        var comment = Assert.Single(comments.Comments!);
        Assert.Equal("Looks good", comment.Text);
        Assert.Equal("Sebastian", comment.CreatedBy!.DisplayName);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("Alpha/_apis/wit/workItems/42/comments", requestUri);
        Assert.Contains("api-version=7.0-preview.3", requestUri);
    }

    [Fact]
    public async Task GetWorkItemRevisionsAsync_ReturnsRevisions()
    {
        const string json =
            """
            {
              "count": 2,
              "value": [
                { "id": 42, "rev": 1, "fields": { "System.State": "New" }, "url": "https://devops.example.local/_apis/wit/workItems/42" },
                { "id": 42, "rev": 2, "fields": { "System.State": "Active" }, "url": "https://devops.example.local/_apis/wit/workItems/42" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var revisions = await client.GetWorkItemRevisionsAsync(42, 100, TestContext.Current.CancellationToken);

        Assert.Equal(2, revisions.Count);
        Assert.True(revisions[1].Fields["System.State"].ValueEquals("Active"));
        Assert.Contains("_apis/wit/workItems/42/revisions", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task AddWorkItemRelationAsync_SendsRelationPatch()
    {
        const string json =
            """{ "id": 42, "rev": 5, "fields": {}, "url": "https://devops.example.local/_apis/wit/workItems/42" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        await client.AddWorkItemRelationAsync(
            42,
            "System.LinkTypes.Hierarchy-Reverse",
            "https://devops.example.local/DefaultCollection/_apis/wit/workItems/40",
            null,
            "parent of the fix",
            TestContext.Current.CancellationToken
        );

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("application/json-patch+json", request.Content!.Headers.ContentType!.MediaType);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var operation = Assert.Single(body.RootElement.EnumerateArray());
        Assert.True(operation.GetProperty("path").ValueEquals("/relations/-"));
        var value = operation.GetProperty("value");
        Assert.True(value.GetProperty("rel").ValueEquals("System.LinkTypes.Hierarchy-Reverse"));
        var attributes = value.GetProperty("attributes");
        Assert.True(attributes.GetProperty("comment").ValueEquals("parent of the fix"));
        Assert.False(attributes.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task AddWorkItemRelationAsync_WithArtifactLinkName_SendsNameAttribute()
    {
        const string json =
            """{ "id": 42, "rev": 5, "fields": {}, "url": "https://devops.example.local/_apis/wit/workItems/42" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        await client.AddWorkItemRelationAsync(
            42,
            ArtifactLinks.Relation,
            "vstfs:///Git/PullRequestId/1%2F2%2F63162",
            ArtifactLinks.PullRequestName,
            null,
            TestContext.Current.CancellationToken
        );

        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var value = Assert.Single(body.RootElement.EnumerateArray()).GetProperty("value");
        Assert.True(value.GetProperty("rel").ValueEquals(ArtifactLinks.Relation));
        Assert.True(value.GetProperty("url").ValueEquals("vstfs:///Git/PullRequestId/1%2F2%2F63162"));
        Assert.True(value.GetProperty("attributes").GetProperty("name").ValueEquals(ArtifactLinks.PullRequestName));
    }

    [Fact]
    public async Task AddWorkItemRelationAsync_WithoutAttributes_OmitsAttributes()
    {
        const string json =
            """{ "id": 42, "rev": 5, "fields": {}, "url": "https://devops.example.local/_apis/wit/workItems/42" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        await client.AddWorkItemRelationAsync(
            42,
            "System.LinkTypes.Related",
            "https://devops.example.local/DefaultCollection/_apis/wit/workItems/40",
            null,
            null,
            TestContext.Current.CancellationToken
        );

        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var value = Assert.Single(body.RootElement.EnumerateArray()).GetProperty("value");
        Assert.False(value.TryGetProperty("attributes", out _));
    }

    [Fact]
    public async Task AddWorkItemAttachmentAsync_UploadsThenLinks()
    {
        using var upload = JsonResponse(
            """{ "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb", "url": "https://devops.example.local/DefaultCollection/_apis/wit/attachments/0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb" }"""
        );
        using var link = JsonResponse(
            """{ "id": 42, "rev": 6, "fields": {}, "url": "https://devops.example.local/_apis/wit/workItems/42" }"""
        );
        var client = CreateClient(out var handler, upload, link);

        await client.AddWorkItemAttachmentAsync(
            42,
            "build-log.txt",
            "##[error]CS1002",
            null,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("fileName=build-log.txt", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Patch, handler.Requests[1].Method);
        using var body = JsonDocument.Parse(handler.RequestBodies[1]);
        var value = Assert.Single(body.RootElement.EnumerateArray()).GetProperty("value");
        Assert.True(value.GetProperty("rel").ValueEquals("AttachedFile"));
        Assert.True(value.GetProperty("attributes").GetProperty("comment").ValueEquals("build-log.txt"));
    }

    [Fact]
    public async Task QueryWorkItemsAsync_WithProject_PostsToProjectScope()
    {
        const string json =
            """
            {
              "queryType": "flat",
              "workItems": [
                { "id": 42, "url": "https://devops.example.local/DefaultCollection/_apis/wit/workItems/42" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var result = await client.QueryWorkItemsAsync(
            "SELECT [System.Id] FROM WorkItems",
            "Alpha Project",
            TestContext.Current.CancellationToken
        );

        var reference = Assert.Single(result.WorkItems);
        Assert.Equal(42, reference.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("Alpha%20Project/_apis/wit/wiql?api-version=7.0", request.RequestUri!.AbsoluteUri);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.True(body.RootElement.GetProperty("query").ValueEquals("SELECT [System.Id] FROM WorkItems"));
    }

    [Fact]
    public async Task QueryWorkItemsAsync_WithoutProject_PostsToCollectionScope()
    {
        using var response = JsonResponse("""{ "queryType": "flat", "workItems": [] }""");
        var client = CreateClient(out var handler, response);

        var result = await client.QueryWorkItemsAsync(
            "SELECT [System.Id] FROM WorkItems",
            null,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(result.WorkItems);
        Assert.EndsWith(
            "/DefaultCollection/_apis/wit/wiql?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.ToString()
        );
    }

    [Fact]
    public async Task GetWorkItemAsync_ReturnsWorkItemWithFields()
    {
        const string json =
            """
            {
              "id": 42,
              "rev": 3,
              "fields": {
                "System.Title": "Fix login bug",
                "System.State": "Active",
                "Microsoft.VSTS.Scheduling.StoryPoints": 5
              },
              "url": "https://devops.example.local/DefaultCollection/_apis/wit/workItems/42",
              "relations": [
                {
                  "rel": "System.LinkTypes.Hierarchy-Reverse",
                  "url": "https://devops.example.local/DefaultCollection/_apis/wit/workItems/40",
                  "attributes": { "name": "Parent" }
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var workItem = await client.GetWorkItemAsync(42, null, TestContext.Current.CancellationToken);

        Assert.Equal(42, workItem.Id);
        Assert.Equal(3, workItem.Rev);
        Assert.True(workItem.Fields["System.Title"].ValueEquals("Fix login bug"));
        Assert.Equal(5, workItem.Fields["Microsoft.VSTS.Scheduling.StoryPoints"].GetInt32());
        Assert.Equal("Parent", Assert.Single(workItem.Relations!).Attributes!.Name);
        Assert.EndsWith(
            "_apis/wit/workitems/42?$expand=relations&api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.ToString()
        );
    }


    [Fact]
    public async Task CreateWorkItemAsync_PostsJsonPatchToTypedUrl()
    {
        const string json =
            """
            {
              "id": 100,
              "rev": 1,
              "fields": { "System.Title": "New bug" },
              "url": "https://devops.example.local/DefaultCollection/_apis/wit/workItems/100"
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);
        var fields = new Dictionary<string, string>
        {
            ["System.Title"] = "New bug"
        };

        var workItem = await client.CreateWorkItemAsync("Alpha", "Bug", fields, TestContext.Current.CancellationToken);

        Assert.Equal(100, workItem.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("Alpha/_apis/wit/workitems/$Bug?api-version=7.0", request.RequestUri!.AbsoluteUri);
        Assert.Equal("application/json-patch+json", request.Content!.Headers.ContentType!.MediaType);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var operation = Assert.Single(body.RootElement.EnumerateArray());
        Assert.True(operation.GetProperty("op").ValueEquals("add"));
        Assert.True(operation.GetProperty("path").ValueEquals("/fields/System.Title"));
        Assert.True(operation.GetProperty("value").ValueEquals("New bug"));
    }

    [Fact]
    public async Task CreateWorkItemAsync_WithoutProject_Throws()
    {
        var client = CreateClient(out var handler);
        var fields = new Dictionary<string, string>
        {
            ["System.Title"] = "New bug"
        };

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(()
            => client.CreateWorkItemAsync(null, "Bug", fields, TestContext.Current.CancellationToken)
        );

        Assert.Contains("ADOS_DEFAULT_PROJECT", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UpdateWorkItemAsync_SendsPatchOperations()
    {
        const string json =
            """
            {
              "id": 42,
              "rev": 4,
              "fields": { "System.State": "Resolved" },
              "url": "https://devops.example.local/DefaultCollection/_apis/wit/workItems/42"
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);
        var fields = new Dictionary<string, string>
        {
            ["System.State"] = "Resolved"
        };

        var workItem = await client.UpdateWorkItemAsync(42, fields, TestContext.Current.CancellationToken);

        Assert.Equal(4, workItem.Rev);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.EndsWith("_apis/wit/workitems/42?api-version=7.0", request.RequestUri!.AbsoluteUri);
        Assert.Equal("application/json-patch+json", request.Content!.Headers.ContentType!.MediaType);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var operation = Assert.Single(body.RootElement.EnumerateArray());
        Assert.True(operation.GetProperty("path").ValueEquals("/fields/System.State"));
        Assert.True(operation.GetProperty("value").ValueEquals("Resolved"));
    }

    [Fact]
    public async Task UpdateWorkItemAsync_WithEmptyFields_Throws()
    {
        var client = CreateClient(out var handler);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(()
            => client.UpdateWorkItemAsync(42, new Dictionary<string, string>(), TestContext.Current.CancellationToken)
        );

        Assert.Contains("At least one field", exception.Message);
        Assert.Empty(handler.Requests);
    }


    [Fact]
    public async Task GetWorkItemsAsync_WithIds_RequestsBatch()
    {
        const string json =
            """
            {
              "count": 2,
              "value": [
                { "id": 1, "rev": 1, "fields": { "System.Title": "First" }, "url": "https://devops.example.local/_apis/wit/workItems/1" },
                { "id": 2, "rev": 4, "fields": { "System.Title": "Second" }, "url": "https://devops.example.local/_apis/wit/workItems/2" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var workItems = await client.GetWorkItemsAsync([1, 2], null, TestContext.Current.CancellationToken);

        Assert.Equal(2, workItems.Count);
        Assert.True(workItems[1].Fields["System.Title"].ValueEquals("Second"));
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("ids=1,2", requestUri);
        Assert.Contains("$expand=relations", requestUri);
    }

    [Fact]
    public async Task GetWorkItemsAsync_WithEmptyIds_Throws()
    {
        var client = CreateClient(out var handler);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(()
            => client.GetWorkItemsAsync([], null, TestContext.Current.CancellationToken)
        );

        Assert.Contains("At least one work item id", exception.Message);
        Assert.Empty(handler.Requests);
    }


    [Fact]
    public async Task GetWorkItemAsync_WithFields_RequestsFieldsInsteadOfRelations()
    {
        const string json =
            """{ "id": 42, "rev": 3, "fields": { "System.Title": "Fix login bug" }, "url": "https://devops.example.local/_apis/wit/workItems/42" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        await client.GetWorkItemAsync(42, ["System.Title", "System.State"], TestContext.Current.CancellationToken);

        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("fields=System.Title%2CSystem.State", requestUri);
        Assert.DoesNotContain("$expand", requestUri);
    }
}

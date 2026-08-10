using System.Net;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;
public sealed class QueryClientTests : AzureDevOpsClientTestsBase
{
    [Fact]
    public async Task GetQueriesAsync_ReturnsQueryTree()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb",
                  "name": "Shared Queries",
                  "path": "Shared Queries",
                  "isFolder": true,
                  "children": [
                    {
                      "id": "b3f11a5c-9d24-4c5e-8a4f-2f47c1d0e9aa",
                      "name": "Active Bugs",
                      "path": "Shared Queries/Active Bugs"
                    }
                  ]
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var queries = await client.GetQueriesAsync("Alpha", 2, TestContext.Current.CancellationToken);

        var root = Assert.Single(queries);
        Assert.True(root.IsFolder);
        Assert.Equal("Active Bugs", Assert.Single(root.Children!).Name);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("Alpha/_apis/wit/queries", requestUri);
        Assert.Contains("$depth=2", requestUri);
    }

    [Fact]
    public async Task RunSavedQueryAsync_RequestsWiqlById()
    {
        const string json =
            """
            {
              "queryType": "flat",
              "workItems": [ { "id": 42, "url": "https://devops.example.local/_apis/wit/workItems/42" } ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var result = await client.RunSavedQueryAsync(
            "Alpha",
            "b3f11a5c-9d24-4c5e-8a4f-2f47c1d0e9aa",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(42, Assert.Single(result.WorkItems).Id);
        Assert.EndsWith(
            "Alpha/_apis/wit/wiql/b3f11a5c-9d24-4c5e-8a4f-2f47c1d0e9aa?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetWorkItemTypesAsync_ReturnsTypes()
    {
        const string json =
            """
            {
              "count": 2,
              "value": [
                { "name": "Bug", "description": "A defect" },
                { "name": "User Story", "description": "A story" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var types = await client.GetWorkItemTypesAsync("Alpha", TestContext.Current.CancellationToken);

        Assert.Equal(2, types.Count);
        Assert.Equal("Bug", types[0].Name);
        Assert.EndsWith(
            "Alpha/_apis/wit/workitemtypes?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetWorkItemStatesAsync_ReturnsStatesForType()
    {
        const string json =
            """
            {
              "count": 3,
              "value": [
                { "name": "New", "category": "Proposed" },
                { "name": "Active", "category": "InProgress" },
                { "name": "Closed", "category": "Completed" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var states = await client.GetWorkItemStatesAsync("Alpha", "User Story", TestContext.Current.CancellationToken);

        Assert.Equal(3, states.Count);
        Assert.Equal("InProgress", states[1].Category);
        Assert.EndsWith(
            "Alpha/_apis/wit/workitemtypes/User%20Story/states?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetClassificationNodesAsync_ReturnsIterationTreeWithDates()
    {
        const string json =
            """
            {
              "name": "Alpha",
              "path": "\\Alpha\\Iteration",
              "children": [
                {
                  "name": "Sprint 1",
                  "path": "\\Alpha\\Iteration\\Sprint 1",
                  "attributes": { "startDate": "2026-08-03T00:00:00Z", "finishDate": "2026-08-14T00:00:00Z" }
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var root = await client.GetClassificationNodesAsync(
            "Alpha",
            "Iterations",
            3,
            TestContext.Current.CancellationToken
        );

        var sprint = Assert.Single(root.Children!);
        Assert.Equal("Sprint 1", sprint.Name);
        Assert.NotNull(sprint.Attributes!.StartDate);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("Alpha/_apis/wit/classificationnodes/Iterations", requestUri);
        Assert.Contains("$depth=3", requestUri);
    }

}

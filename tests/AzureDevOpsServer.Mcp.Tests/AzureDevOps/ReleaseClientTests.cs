using System.Net;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;
public sealed class ReleaseClientTests : AzureDevOpsClientTestsBase
{
    [Fact]
    public async Task GetReleaseDefinitionsAsync_ReturnsDefinitions()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [ { "id": 3, "name": "Deploy to production", "path": "\\" } ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var definitions = await client.GetReleaseDefinitionsAsync("Alpha", TestContext.Current.CancellationToken);

        var definition = Assert.Single(definitions);
        Assert.Equal("Deploy to production", definition.Name);
        Assert.EndsWith(
            "Alpha/_apis/release/definitions?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetReleasesAsync_WithDefinitionFilter_AppendsQueryParameters()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "id": 42,
                  "name": "Release-15",
                  "status": "active",
                  "createdOn": "2026-08-11T08:00:00Z",
                  "releaseDefinition": { "id": 3, "name": "Deploy to production" }
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var releases = await client.GetReleasesAsync("Alpha", 3, 20, TestContext.Current.CancellationToken);

        var release = Assert.Single(releases);
        Assert.Equal("Release-15", release.Name);
        Assert.Equal(3, release.ReleaseDefinition!.Id);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("$top=20", requestUri);
        Assert.Contains("definitionId=3", requestUri);
        Assert.Contains("Alpha/_apis/release/releases", requestUri);
    }

    [Fact]
    public async Task GetReleaseAsync_ReturnsEnvironmentStatuses()
    {
        const string json =
            """
            {
              "id": 42,
              "name": "Release-15",
              "status": "active",
              "environments": [
                { "id": 1, "name": "Staging", "status": "succeeded" },
                { "id": 2, "name": "Production", "status": "inProgress" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var release = await client.GetReleaseAsync("Alpha", 42, TestContext.Current.CancellationToken);

        Assert.Equal(2, release.Environments!.Count);
        Assert.Equal("succeeded", release.Environments[0].Status);
        Assert.Equal("Production", release.Environments[1].Name);
        Assert.EndsWith(
            "Alpha/_apis/release/releases/42?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task CreateReleaseAsync_PostsDefinitionAndDescription()
    {
        const string json =
            """{ "id": 43, "name": "Release-16", "status": "active" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var release = await client.CreateReleaseAsync(
            "Alpha",
            3,
            "Hotfix deployment",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(43, release.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith(
            "Alpha/_apis/release/releases?api-version=7.0",
            request.RequestUri!.AbsoluteUri
        );
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.Equal(3, body.RootElement.GetProperty("definitionId").GetInt32());
        Assert.True(body.RootElement.GetProperty("description").ValueEquals("Hotfix deployment"));
    }

}

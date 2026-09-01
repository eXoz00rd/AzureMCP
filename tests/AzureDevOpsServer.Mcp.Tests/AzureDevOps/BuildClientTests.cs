using System.Net;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class BuildClientTests : AzureDevOpsClientTestsBase
{
    [Fact]
    public async Task GetBuildDefinitionsAsync_ReturnsDefinitions()
    {
        const string json =
            """
            {
              "count": 2,
              "value": [
                { "id": 12, "name": "CI", "path": "\\Pipelines" },
                { "id": 13, "name": "Nightly", "path": "\\" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var definitions = await client.GetBuildDefinitionsAsync("Alpha", TestContext.Current.CancellationToken);

        Assert.Equal(2, definitions.Count);
        Assert.Equal("CI", definitions[0].Name);
        Assert.EndsWith(
            "Alpha/_apis/build/definitions?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetBuildDefinitionsAsync_WithoutProject_Throws()
    {
        var client = CreateClient(out var handler);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(()
            => client.GetBuildDefinitionsAsync(null, TestContext.Current.CancellationToken)
        );

        Assert.Contains("ADOS_DEFAULT_PROJECT", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetBuildsAsync_WithDefinitionFilter_AppendsQueryParameters()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "id": 500,
                  "buildNumber": "20260810.1",
                  "status": "completed",
                  "result": "succeeded",
                  "sourceBranch": "refs/heads/main",
                  "definition": { "id": 12, "name": "CI" },
                  "queueTime": "2026-08-10T10:00:00Z",
                  "finishTime": "2026-08-10T10:05:00Z"
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var builds = await client.GetBuildsAsync("Alpha", 12, 20, TestContext.Current.CancellationToken);

        var build = Assert.Single(builds);
        Assert.Equal("20260810.1", build.BuildNumber);
        Assert.Equal("succeeded", build.Result);
        Assert.Equal(12, build.Definition!.Id);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("$top=20", requestUri);
        Assert.Contains("definitions=12", requestUri);
        Assert.Contains("Alpha/_apis/build/builds", requestUri);
    }

    [Fact]
    public async Task QueueBuildAsync_PostsDefinitionAndNormalizedBranch()
    {
        const string json =
            """
            {
              "id": 501,
              "buildNumber": "20260810.2",
              "status": "notStarted",
              "sourceBranch": "refs/heads/develop",
              "definition": { "id": 12, "name": "CI" }
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var build = await client.QueueBuildAsync("Alpha", 12, "develop", TestContext.Current.CancellationToken);

        Assert.Equal(501, build.Id);
        Assert.Null(build.Result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith(
            "Alpha/_apis/build/builds?api-version=7.0",
            request.RequestUri!.AbsoluteUri
        );
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.Equal(12, body.RootElement.GetProperty("definition").GetProperty("id").GetInt32());
        Assert.True(body.RootElement.GetProperty("sourceBranch").ValueEquals("refs/heads/develop"));
    }


    [Fact]
    public async Task GetBuildTimelineAsync_ReturnsRecordsWithIssues()
    {
        const string json =
            """
            {
              "records": [
                {
                  "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb",
                  "type": "Stage",
                  "name": "Build",
                  "state": "completed",
                  "result": "failed",
                  "errorCount": 1,
                  "warningCount": 0,
                  "log": { "id": 7 },
                  "issues": [ { "type": "error", "message": "CS1002: ; expected" } ]
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var records = await client.GetBuildTimelineAsync("Alpha", 500, TestContext.Current.CancellationToken);

        var record = Assert.Single(records);
        Assert.Equal("failed", record.Result);
        Assert.Equal(7, record.Log!.Id);
        Assert.Equal("CS1002: ; expected", Assert.Single(record.Issues!).Message);
        Assert.EndsWith(
            "Alpha/_apis/build/builds/500/timeline?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetBuildLogAsync_WithLineRange_AppendsQueryParameters()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("##[error]CS1002: ; expected\nBuild failed.")
        };
        var client = CreateClient(out var handler, response);

        var log = await client.GetBuildLogAsync(
            "Alpha",
            500,
            7,
            10,
            50,
            ResponseLimits.DefaultMaxChars,
            TestContext.Current.CancellationToken
        );

        Assert.Contains("CS1002", log.Content);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("builds/500/logs/7", requestUri);
        Assert.Contains("startLine=10", requestUri);
        Assert.Contains("endLine=50", requestUri);
    }

    [Fact]
    public async Task GetBuildLogAsync_WithoutLineRange_OmitsQueryParameters()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("full log")
        };
        var client = CreateClient(out var handler, response);

        var log = await client.GetBuildLogAsync(
            "Alpha",
            500,
            7,
            null,
            null,
            ResponseLimits.DefaultMaxChars,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("full log", log.Content);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.DoesNotContain("startLine", requestUri);
        Assert.DoesNotContain("endLine", requestUri);
    }

    [Fact]
    public async Task GetBuildArtifactsAsync_ReturnsArtifacts()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "id": 1,
                  "name": "drop",
                  "resource": {
                    "type": "Container",
                    "downloadUrl": "https://devops.example.local/DefaultCollection/_apis/resources/Containers/10?itemPath=drop&%24format=zip"
                  }
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var artifacts = await client.GetBuildArtifactsAsync("Alpha", 500, TestContext.Current.CancellationToken);

        var artifact = Assert.Single(artifacts);
        Assert.Equal("drop", artifact.Name);
        Assert.Contains("format=zip", artifact.Resource!.DownloadUrl);
        Assert.EndsWith(
            "Alpha/_apis/build/builds/500/artifacts?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }


    [Fact]
    public async Task GetBuildLogAsync_WhenLongerThanLimit_TruncatesAndReportsTotal()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 5000))
        };
        var client = CreateClient(out _, response);

        var log = await client.GetBuildLogAsync(
            "Alpha",
            500,
            7,
            null,
            null,
            100,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(100, log.Content.Length);
        Assert.Equal(5000, log.TotalChars);
        Assert.True(log.Truncated);
    }

    [Fact]
    public async Task GetBuildLogAsync_WithKnownContentLength_ReportsTotalAndTruncates()
    {
        var content = new StringContent(new string('x', 5000));
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        var client = CreateClient(out _, response);
        Assert.NotNull(content.Headers.ContentLength);

        var log = await client.GetBuildLogAsync(
            "Alpha",
            500,
            7,
            null,
            null,
            100,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(100, log.Content.Length);
        Assert.Equal(5000, log.TotalChars);
        Assert.True(log.Truncated);
    }

    [Fact]
    public async Task GetBuildLogAsync_WithUnknownContentLength_ReportsTotalAndTruncates()
    {
        var stream = new GeneratedStream(200_000);
        var content = new StreamContent(stream);
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        var client = CreateClient(out _, response);
        Assert.Null(content.Headers.ContentLength);

        var log = await client.GetBuildLogAsync(
            "Alpha",
            500,
            7,
            null,
            null,
            100,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(100, log.Content.Length);
        Assert.Equal(200_000, log.TotalChars);
        Assert.True(log.Truncated);
    }

    [Fact]
    public async Task GetBuildLogAsync_WhenLogIsFarLargerThanLimit_DoesNotBufferWholeResponse()
    {
        const long length = 20_000_000;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new GeneratedStream(length))
        };
        var client = CreateClient(out _, response);

        var log = await client.GetBuildLogAsync(
            "Alpha",
            500,
            7,
            null,
            null,
            1_000,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1_000, log.Content.Length);
        Assert.Equal(length, log.TotalChars);
        Assert.True(log.Truncated);
    }
}

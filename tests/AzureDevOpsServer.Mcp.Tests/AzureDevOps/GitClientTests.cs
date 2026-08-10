using System.Net;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class GitClientTests : AzureDevOpsClientTestsBase
{
    [Fact]
    public async Task GetRepositoriesAsync_WithoutProject_ListsCollectionRepositories()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "id": "3f9a1c2b-6d7e-4f80-9a1b-2c3d4e5f6a7b",
                  "name": "WebApp",
                  "defaultBranch": "refs/heads/main",
                  "remoteUrl": "https://devops.example.local/DefaultCollection/Alpha/_git/WebApp"
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var repositories = await client.GetRepositoriesAsync(null, TestContext.Current.CancellationToken);

        var repository = Assert.Single(repositories);
        Assert.Equal("WebApp", repository.Name);
        Assert.Equal("refs/heads/main", repository.DefaultBranch);
        Assert.EndsWith(
            "/DefaultCollection/_apis/git/repositories?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetRepositoriesAsync_WithProject_UsesProjectScope()
    {
        using var response = JsonResponse("""{ "count": 0, "value": [] }""");
        var client = CreateClient(out var handler, response);

        var repositories = await client.GetRepositoriesAsync("Alpha Project", TestContext.Current.CancellationToken);

        Assert.Empty(repositories);
        Assert.EndsWith(
            "Alpha%20Project/_apis/git/repositories?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetBranchesAsync_ReturnsHeadRefs()
    {
        const string json =
            """
            {
              "count": 2,
              "value": [
                { "name": "refs/heads/main", "objectId": "a1b2c3d4" },
                { "name": "refs/heads/develop", "objectId": "e5f6a7b8" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var branches = await client.GetBranchesAsync("WebApp", "Alpha", 100, TestContext.Current.CancellationToken);

        Assert.Equal(2, branches.Count);
        Assert.Equal("refs/heads/main", branches[0].Name);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/refs?filter=heads/&$top=100&api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetFileContentAsync_WithBranch_RequestsVersionDescriptor()
    {
        const string json =
            """
            {
              "objectId": "a1b2c3d4",
              "path": "/README.md",
              "content": "# Hello"
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var item = await client.GetFileContentAsync(
            "WebApp",
            "/README.md",
            "develop",
            "Alpha",
            ResponseLimits.DefaultMaxChars,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("/README.md", item.Path);
        Assert.Equal("# Hello", item.Content);
        Assert.False(item.Truncated);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("path=%2FREADME.md", requestUri);
        Assert.Contains("includeContent=true", requestUri);
        Assert.Contains("$format=json", requestUri);
        Assert.Contains("versionDescriptor.version=develop&versionDescriptor.versionType=branch", requestUri);
    }

    [Fact]
    public async Task GetFileContentAsync_WithoutBranch_OmitsVersionDescriptor()
    {
        using var response = JsonResponse("""{ "objectId": "a1b2c3d4", "path": "/README.md", "content": "# Hello" }""");
        var client = CreateClient(out var handler, response);

        await client.GetFileContentAsync(
            "WebApp",
            "/README.md",
            null,
            "Alpha",
            ResponseLimits.DefaultMaxChars,
            TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain("versionDescriptor", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }


    [Fact]
    public async Task GetCommitsAsync_WithBranchAndPath_AppendsSearchCriteria()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "commitId": "abc123def456",
                  "comment": "Fix login bug",
                  "author": { "name": "Sebastian", "email": "sebastian@example.local", "date": "2026-08-11T09:00:00Z" }
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var commits = await client.GetCommitsAsync(
            "WebApp",
            "refs/heads/develop",
            "/src",
            20,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        var commit = Assert.Single(commits);
        Assert.Equal("Fix login bug", commit.Comment);
        Assert.Equal("Sebastian", commit.Author!.Name);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("searchCriteria.$top=20", requestUri);
        Assert.Contains("searchCriteria.itemVersion.version=develop", requestUri);
        Assert.Contains("searchCriteria.itemPath=%2Fsrc", requestUri);
    }

    [Fact]
    public async Task GetCommitAsync_ReturnsMetadataAndChanges()
    {
        const string commitJson =
            """
            {
              "commitId": "abc123def456",
              "comment": "Fix login bug",
              "author": { "name": "Sebastian", "email": "sebastian@example.local", "date": "2026-08-11T09:00:00Z" }
            }
            """;
        const string changesJson =
            """
            {
              "changes": [
                { "changeType": "edit", "item": { "path": "/src/Login.cs" } },
                { "changeType": "add", "item": { "path": "/tests/LoginTests.cs" } }
              ]
            }
            """;
        using var commit = JsonResponse(commitJson);
        using var changes = JsonResponse(changesJson);
        var client = CreateClient(out var handler, commit, changes);

        var details = await client.GetCommitAsync(
            "WebApp",
            "abc123def456",
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("Fix login bug", details.Commit.Comment);
        Assert.Equal(2, details.Changes.Count);
        Assert.Equal("/src/Login.cs", details.Changes[0].Item.Path);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith(
            "commits/abc123def456?api-version=7.0",
            handler.Requests[0].RequestUri!.AbsoluteUri
        );
        Assert.EndsWith(
            "commits/abc123def456/changes?api-version=7.0",
            handler.Requests[1].RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetRepositoryItemsAsync_DefaultsToOneLevel()
    {
        const string json =
            """
            {
              "count": 2,
              "value": [
                { "path": "/src", "isFolder": true, "gitObjectType": "tree" },
                { "path": "/README.md", "gitObjectType": "blob" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var result = await client.GetRepositoryItemsAsync(
            "WebApp",
            null,
            "develop",
            false,
            "Alpha",
            ResponseLimits.DefaultMaxItems,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.Items.Count);
        Assert.False(result.Truncated);
        Assert.True(result.Items[0].IsFolder);
        Assert.Null(result.Items[1].IsFolder);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("scopePath=%2F", requestUri);
        Assert.Contains("recursionLevel=oneLevel", requestUri);
        Assert.Contains("versionDescriptor.version=develop", requestUri);
    }

    [Fact]
    public async Task GetRepositoryItemsAsync_Recursive_UsesFullRecursion()
    {
        using var response = JsonResponse("""{ "count": 0, "value": [] }""");
        var client = CreateClient(out var handler, response);

        await client.GetRepositoryItemsAsync(
            "WebApp",
            "/src",
            null,
            true,
            "Alpha",
            ResponseLimits.DefaultMaxItems,
            TestContext.Current.CancellationToken
        );

        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("scopePath=%2Fsrc", requestUri);
        Assert.Contains("recursionLevel=full", requestUri);
        Assert.DoesNotContain("versionDescriptor", requestUri);
    }

    [Fact]
    public async Task GetBranchDiffAsync_ReturnsAheadBehindAndChanges()
    {
        const string json =
            """
            {
              "aheadCount": 3,
              "behindCount": 1,
              "changes": [
                { "changeType": "edit", "item": { "path": "/src/Program.cs" } }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var diffs = await client.GetBranchDiffAsync(
            "WebApp",
            "main",
            "refs/heads/develop",
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, diffs.AheadCount);
        Assert.Equal(1, diffs.BehindCount);
        Assert.Equal("/src/Program.cs", Assert.Single(diffs.Changes!).Item.Path);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("diffs/commits", requestUri);
        Assert.Contains("baseVersion=main", requestUri);
        Assert.Contains("targetVersion=develop", requestUri);
    }


    [Fact]
    public async Task GetFileContentAsync_WhenLongerThanLimit_TruncatesContent()
    {
        var json = $"{{ \"path\": \"/big.txt\", \"content\": \"{new string('a', 2000)}\" }}";
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        var file = await client.GetFileContentAsync(
            "WebApp",
            "/big.txt",
            null,
            "Alpha",
            50,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(50, file.Content!.Length);
        Assert.Equal(2000, file.TotalChars);
        Assert.True(file.Truncated);
        Assert.False(file.IsBinary);
    }

    [Fact]
    public async Task GetFileContentAsync_WithBinaryContent_ReportsBinaryWithoutContent()
    {
        const string json = """{ "path": "/logo.png", "content": "PNG\u0000\u0000binary" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        var file = await client.GetFileContentAsync(
            "WebApp",
            "/logo.png",
            null,
            "Alpha",
            ResponseLimits.DefaultMaxChars,
            TestContext.Current.CancellationToken
        );

        Assert.True(file.IsBinary);
        Assert.Null(file.Content);
    }

    [Fact]
    public async Task GetRepositoryItemsAsync_WhenMoreThanMaxItems_TruncatesList()
    {
        var entries = string.Join(',', Enumerable.Range(1, 10).Select(i => $"{{ \"path\": \"/file{i}.cs\" }}"));
        using var response = JsonResponse($"{{ \"count\": 10, \"value\": [{entries}] }}");
        var client = CreateClient(out _, response);

        var result = await client.GetRepositoryItemsAsync(
            "WebApp",
            "/src",
            null,
            true,
            "Alpha",
            4,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(4, result.Items.Count);
        Assert.True(result.Truncated);
    }


    [Fact]
    public async Task GetBranchesAsync_AppendsTop()
    {
        using var response = JsonResponse("""{ "count": 0, "value": [] }""");
        var client = CreateClient(out var handler, response);

        await client.GetBranchesAsync("WebApp", "Alpha", 250, TestContext.Current.CancellationToken);

        Assert.Contains("$top=250", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

}

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

        Assert.Equal(2, branches.Items.Count);
        Assert.False(branches.Truncated);
        Assert.Equal("refs/heads/main", branches.Items[0].Name);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/refs?filter=heads/&$top=101&api-version=7.0",
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

        var commit = Assert.Single(commits.Items);
        Assert.False(commits.Truncated);
        Assert.Equal("Fix login bug", commit.Comment);
        Assert.Equal("Sebastian", commit.Author!.Name);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("searchCriteria.$top=21", requestUri);
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
    public async Task GetFileContentAsync_WithJsonEscapes_DecodesThemCorrectly()
    {
        const string json =
            """
            { "path": "/notes.txt", "content": "line1\nline2\ttab\t\"quoted\"\\backslash\/slash" }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        var file = await client.GetFileContentAsync(
            "WebApp",
            "/notes.txt",
            null,
            "Alpha",
            ResponseLimits.DefaultMaxChars,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("line1\nline2\ttab\t\"quoted\"\\backslash/slash", file.Content);
    }

    [Fact]
    public async Task GetFileContentAsync_WithMultiByteUtf8Content_CountsCharsNotBytes()
    {
        // Written directly (not via \u escapes) so the wire bytes are raw multi-byte UTF-8.
        var text = string.Concat(Enumerable.Repeat("ąćęłń", 20));
        var json = $$"""{ "path": "/diacritics.txt", "content": "{{text}}" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        var file = await client.GetFileContentAsync(
            "WebApp",
            "/diacritics.txt",
            null,
            "Alpha",
            40,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(text[..40], file.Content);
        Assert.Equal(text.Length, file.TotalChars);
        Assert.True(file.Truncated);
    }

    [Fact]
    public async Task GetFileContentAsync_WithSurrogatePairContent_KeepsPairIntact()
    {
        // Written directly (not via \u escapes) so the wire bytes are a raw 4-byte UTF-8 sequence.
        const string emoji = "\U0001F600";
        var json = $$"""{ "path": "/emoji.txt", "content": "hi{{emoji}}bye" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        var file = await client.GetFileContentAsync(
            "WebApp",
            "/emoji.txt",
            null,
            "Alpha",
            ResponseLimits.DefaultMaxChars,
            TestContext.Current.CancellationToken
        );

        Assert.Equal($"hi{emoji}bye", file.Content);
        Assert.Equal(7, file.TotalChars);
    }

    [Fact]
    public async Task GetFileContentAsync_WithEscapedSurrogatePair_KeepsPairIntact()
    {
        const string json = """{ "path": "/emoji.txt", "content": "hi\ud83d\ude00bye" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        var file = await client.GetFileContentAsync(
            "WebApp",
            "/emoji.txt",
            null,
            "Alpha",
            ResponseLimits.DefaultMaxChars,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("hi\U0001F600bye", file.Content);
        Assert.Equal(7, file.TotalChars);
    }

    [Fact]
    public async Task GetFileContentAsync_WithUnpairedHighSurrogateEscape_ThrowsParseException()
    {
        const string json = """{ "path": "/emoji.txt", "content": "hi\ud83dbye" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetFileContentAsync(
                "WebApp",
                "/emoji.txt",
                null,
                "Alpha",
                ResponseLimits.DefaultMaxChars,
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task GetFileContentAsync_WithUnpairedLowSurrogateEscape_ThrowsParseException()
    {
        const string json = """{ "path": "/emoji.txt", "content": "hi\ude00bye" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetFileContentAsync(
                "WebApp",
                "/emoji.txt",
                null,
                "Alpha",
                ResponseLimits.DefaultMaxChars,
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task GetFileContentAsync_WhenPathAppearsAfterContent_StillParsesPath()
    {
        const string json =
            """
            {
              "objectId": "a1b2c3d4",
              "contentMetadata": { "encoding": 65001, "extension": ".md" },
              "content": "# Hello",
              "path": "/README.md",
              "url": "https://devops.example.local/_apis/git/repositories/WebApp/items/README.md"
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        var file = await client.GetFileContentAsync(
            "WebApp",
            "/README.md",
            null,
            "Alpha",
            ResponseLimits.DefaultMaxChars,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("/README.md", file.Path);
        Assert.Equal("# Hello", file.Content);
    }

    [Fact]
    public async Task GetFileContentAsync_WhenSurrogatePairStraddlesTruncationLimit_DoesNotEndWithUnpairedSurrogate()
    {
        const string emoji = "\U0001F600";
        var prefix = new string('a', 49);
        var content = $"{prefix}{emoji}{new string('b', 20)}";
        var json = $$"""{ "path": "/x.txt", "content": "{{content}}" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        var file = await client.GetFileContentAsync(
            "WebApp",
            "/x.txt",
            null,
            "Alpha",
            50,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(prefix, file.Content);
        Assert.True(file.Truncated);
    }

    [Fact]
    public async Task GetFileContentAsync_WhenSurrogatePairStraddlesCaptureLimit_DoesNotEndWithUnpairedSurrogate()
    {
        const string emoji = "\U0001F600";
        var prefix = new string('a', 8001);
        var content = $"{prefix}{emoji}{new string('b', 10)}";
        var json = $$"""{ "path": "/x.txt", "content": "{{content}}" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        var file = await client.GetFileContentAsync(
            "WebApp",
            "/x.txt",
            null,
            "Alpha",
            8002,
            TestContext.Current.CancellationToken
        );

        // The lone high surrogate that would otherwise be captured at the boundary is trimmed, so
        // the result stays a strict prefix of the original content instead of skipping ahead to the
        // 'b' that follows the emoji.
        Assert.Equal(prefix, file.Content);
        Assert.True(file.Truncated);
    }

    [Fact]
    public async Task GetFileContentAsync_WithPathLongerThanMaxSupportedLength_ThrowsParseException()
    {
        var longPath = "/" + new string('p', 5000);
        var json = $$"""{ "path": "{{longPath}}", "content": "hi" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetFileContentAsync(
                "WebApp",
                longPath,
                null,
                "Alpha",
                ResponseLimits.DefaultMaxChars,
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task GetFileContentAsync_WhenContentStreamIsFarLargerThanLimit_ReportsFullSizeWithoutBufferingIt()
    {
        const long fillLength = 5_000_000;
        using var stream = new JsonEnvelopeStream("/big.bin", fillLength);
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        var client = CreateClient(out _, response);

        var file = await client.GetFileContentAsync(
            "WebApp",
            "/big.bin",
            null,
            "Alpha",
            1_000,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1_000, file.Content!.Length);
        Assert.Equal(fillLength, file.TotalChars);
        Assert.True(file.Truncated);
        Assert.False(file.IsBinary);
    }

    [Fact]
    public async Task GetFileContentAsync_WhenCancelledMidStream_StopsReading()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var stream = new JsonEnvelopeStream("/big.bin", 50_000_000, cancelSource: cancellation, cancelAfterReads: 3);
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        var client = CreateClient(out _, response);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetFileContentAsync(
                "WebApp",
                "/big.bin",
                null,
                "Alpha",
                ResponseLimits.MaxChars,
                cancellation.Token
            )
        );
    }

    [Fact]
    public async Task GetFileContentAsync_WithJsonEnvelopeStreamCancelSourceButNoCancelAfterReads_DoesNotCancelImmediately()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var stream = new JsonEnvelopeStream("/small.txt", 5, cancelSource: cancellation);
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        var client = CreateClient(out _, response);

        var file = await client.GetFileContentAsync(
            "WebApp",
            "/small.txt",
            null,
            "Alpha",
            ResponseLimits.DefaultMaxChars,
            cancellation.Token
        );

        Assert.Equal("xxxxx", file.Content);
    }

    [Fact]
    public async Task GetFileContentAsync_WithUnescapedControlCharacterInContent_ThrowsParseException()
    {
        var body = Encoding.UTF8.GetBytes("{ \"path\": \"/x\", \"content\": \"line1")
                            .Concat(new byte[] { 0x0A })
                            .Concat(Encoding.UTF8.GetBytes("line2\" }"))
                            .ToArray();
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        var client = CreateClient(out _, response);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetFileContentAsync(
                "WebApp",
                "/x",
                null,
                "Alpha",
                ResponseLimits.DefaultMaxChars,
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task GetFileContentAsync_WithOverlongUtf8Sequence_ThrowsParseException()
    {
        var body = Encoding.UTF8.GetBytes("{ \"path\": \"/x\", \"content\": \"")
                            .Concat(new byte[] { 0xC0, 0x80 })
                            .Concat(Encoding.UTF8.GetBytes("\" }"))
                            .ToArray();
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        var client = CreateClient(out _, response);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetFileContentAsync(
                "WebApp",
                "/x",
                null,
                "Alpha",
                ResponseLimits.DefaultMaxChars,
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task GetFileContentAsync_WithUtf8EncodedSurrogateCodepoint_ThrowsParseException()
    {
        var body = Encoding.UTF8.GetBytes("{ \"path\": \"/x\", \"content\": \"")
                            .Concat(new byte[] { 0xED, 0xA0, 0x80 })
                            .Concat(Encoding.UTF8.GetBytes("\" }"))
                            .ToArray();
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        var client = CreateClient(out _, response);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetFileContentAsync(
                "WebApp",
                "/x",
                null,
                "Alpha",
                ResponseLimits.DefaultMaxChars,
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task GetFileContentAsync_WhenJsonEnvelopeStreamPathNeedsEscaping_StillParsesCorrectly()
    {
        const string path = "C:\\repo\"file.txt";
        using var stream = new JsonEnvelopeStream(path, 10);
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        var client = CreateClient(out _, response);

        var file = await client.GetFileContentAsync(
            "WebApp",
            path,
            null,
            "Alpha",
            ResponseLimits.DefaultMaxChars,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(path, file.Path);
        Assert.Equal(new string('x', 10), file.Content);
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

        Assert.Contains("$top=251", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetBranchesAsync_WhenServerReturnsMoreThanTop_ReportsTruncated()
    {
        var entries = string.Join(',', Enumerable.Range(1, 3).Select(i => $"{{ \"name\": \"refs/heads/branch{i}\", \"objectId\": \"a{i}\" }}"));
        using var response = JsonResponse($"{{ \"count\": 3, \"value\": [{entries}] }}");
        var client = CreateClient(out _, response);

        var branches = await client.GetBranchesAsync("WebApp", "Alpha", 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, branches.Items.Count);
        Assert.True(branches.Truncated);
    }

    [Fact]
    public async Task GetCommitsAsync_WhenServerReturnsMoreThanTop_ReportsTruncated()
    {
        var entries = string.Join(',', Enumerable.Range(1, 3).Select(i => $"{{ \"commitId\": \"commit{i}\", \"comment\": \"Change {i}\" }}"));
        using var response = JsonResponse($"{{ \"count\": 3, \"value\": [{entries}] }}");
        var client = CreateClient(out _, response);

        var commits = await client.GetCommitsAsync(
            "WebApp",
            null,
            null,
            2,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, commits.Items.Count);
        Assert.True(commits.Truncated);
    }

}

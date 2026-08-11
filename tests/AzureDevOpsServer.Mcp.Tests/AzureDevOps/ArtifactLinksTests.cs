using AzureDevOpsServer.Mcp.AzureDevOps;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class ArtifactLinksTests
{
    [Fact]
    public void PullRequestUrl_EncodesSeparators()
    {
        var projectId = Guid.Parse("1a2b3c4d-5e6f-4a8b-9c0d-1e2f3a4b5c6d");
        var repositoryId = Guid.Parse("8f1c0d1e-2b3a-4c5d-9e8f-7a6b5c4d3e2f");

        var url = ArtifactLinks.PullRequestUrl(projectId, repositoryId, 63162);

        Assert.Equal(
            "vstfs:///Git/PullRequestId/1a2b3c4d-5e6f-4a8b-9c0d-1e2f3a4b5c6d%2F8f1c0d1e-2b3a-4c5d-9e8f-7a6b5c4d3e2f%2F63162",
            url
        );
    }

    [Theory]
    [InlineData("vstfs:///Git/PullRequestId/1%2F2%2F3", "Pull Request")]
    [InlineData("vstfs:///Git/Commit/1%2F2%2Fabcdef", "Fixed in Commit")]
    [InlineData("vstfs:///Build/Build/512", "Integrated in build")]
    public void NameFor_RecognisesArtifactUrls(string url, string expected)
    {
        Assert.Equal(expected, ArtifactLinks.NameFor(url));
    }

    [Theory]
    [InlineData("https://devops.example.local/DefaultCollection/WebApp/_git/WebApp/pullrequest/63162")]
    [InlineData("vstfs:///Wiki/WikiPage/1")]
    public void NameFor_WithUnknownUrl_ReturnsNull(string url)
    {
        Assert.Null(ArtifactLinks.NameFor(url));
    }
}

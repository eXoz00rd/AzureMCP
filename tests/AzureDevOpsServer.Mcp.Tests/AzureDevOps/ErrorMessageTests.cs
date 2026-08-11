using System.Net;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class ErrorMessageTests : AzureDevOpsClientTestsBase
{
    [Fact]
    public void ExtractErrorMessage_WithAzureDevOpsError_ReturnsOnlyMessage()
    {
        const string body =
            """
            {
              "$id": "1",
              "innerException": null,
              "message": "TF401019: The Git repository with name or identifier WebApp does not exist.",
              "typeName": "Microsoft.TeamFoundation.Git.Server.GitRepositoryNotFoundException, Microsoft.TeamFoundation.Git.Server",
              "typeKey": "GitRepositoryNotFoundException",
              "errorCode": 0,
              "eventId": 3000
            }
            """;

        var message = AzureDevOpsClient.ExtractErrorMessage(body);

        Assert.Equal("TF401019: The Git repository with name or identifier WebApp does not exist.", message);
    }

    [Fact]
    public void ExtractErrorMessage_WithHtmlBody_FallsBackToRawText()
    {
        var message = AzureDevOpsClient.ExtractErrorMessage("<html>Server Error</html>");

        Assert.Equal("<html>Server Error</html>", message);
    }

    [Fact]
    public void ExtractErrorMessage_WithJsonWithoutMessage_FallsBackToRawText()
    {
        var message = AzureDevOpsClient.ExtractErrorMessage("""{ "typeKey": "SomeException" }""");

        Assert.Contains("typeKey", message);
    }

    [Fact]
    public void ExtractErrorMessage_WithEmptyBody_ExplainsIt()
    {
        Assert.Equal("The response body was empty.", AzureDevOpsClient.ExtractErrorMessage("   "));
    }

    [Fact]
    public void ExtractErrorMessage_WithVeryLongMessage_IsTruncated()
    {
        var body = $"{{ \"message\": \"{new string('x', 900)}\" }}";

        var message = AzureDevOpsClient.ExtractErrorMessage(body);

        Assert.Equal(500, message.Length);
    }

    [Fact]
    public async Task EnsureSuccessAsync_SurfacesParsedMessageToTheAgent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(
                """{ "message": "TF401019: The Git repository with name or identifier Ghost does not exist.", "typeKey": "GitRepositoryNotFoundException" }"""
            )
        };
        var client = CreateClient(out _, response);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetProjectsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("TF401019", exception.Message);
        Assert.Contains("404", exception.Message);
        Assert.DoesNotContain("typeKey", exception.Message);
    }
}

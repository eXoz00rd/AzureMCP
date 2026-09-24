using AzureDevOpsServer.Mcp.AzureDevOps;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class RequestCredentialProviderTests
{
    [Fact]
    public async Task Header_ProvidesTheCallersPersonalAccessToken()
    {
        var provider = ProviderWith(" caller-pat ");

        var personalAccessToken = await provider.GetPersonalAccessTokenAsync(TestContext.Current.CancellationToken);

        Assert.Equal("caller-pat", personalAccessToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingOrBlankHeader_NamesTheHeaderToSend(string? value)
    {
        var provider = ProviderWith(value is null ? StringValues.Empty : new StringValues(value));

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            async () => await provider.GetPersonalAccessTokenAsync(TestContext.Current.CancellationToken)
        );

        Assert.Contains(RequestCredentialProvider.HeaderName, exception.Message);
    }

    [Fact]
    public async Task RepeatedHeader_IsRejectedRatherThanJoined()
    {
        var provider = ProviderWith(new StringValues(["first-pat", "second-pat"]));

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            async () => await provider.GetPersonalAccessTokenAsync(TestContext.Current.CancellationToken)
        );

        Assert.Contains("more than one", exception.Message);
        Assert.DoesNotContain("first-pat", exception.Message);
    }

    [Fact]
    public async Task CallOutsideAnHttpRequest_IsRejected()
    {
        var provider = new RequestCredentialProvider(new HttpContextAccessor());

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            async () => await provider.GetPersonalAccessTokenAsync(TestContext.Current.CancellationToken)
        );
    }

    private static RequestCredentialProvider ProviderWith(StringValues header)
    {
        var context = new DefaultHttpContext();
        if (header.Count > 0)
        {
            context.Request.Headers[RequestCredentialProvider.HeaderName] = header;
        }

        return new RequestCredentialProvider(new HttpContextAccessor { HttpContext = context });
    }
}

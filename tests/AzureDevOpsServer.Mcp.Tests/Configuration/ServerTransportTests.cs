using AzureDevOpsServer.Mcp.Configuration;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Configuration;

public sealed class ServerTransportTests
{
    [Theory]
    [InlineData(null, ServerTransport.Stdio)]
    [InlineData("", ServerTransport.Stdio)]
    [InlineData("stdio", ServerTransport.Stdio)]
    [InlineData("http", ServerTransport.Http)]
    [InlineData(" HTTP ", ServerTransport.Http)]
    public void Resolve_WithKnownValue_ReturnsTransport(string? requested, ServerTransport expected)
    {
        Assert.Equal(expected, ServerTransports.Resolve(requested));
    }

    [Fact]
    public void Resolve_WithUnknownValue_ListsValidValues()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ServerTransports.Resolve("sse"));

        Assert.Contains(AzureDevOpsServerOptions.TransportVariable, exception.Message);
        Assert.Contains("sse", exception.Message);
        Assert.Contains("Valid values are: stdio, http.", exception.Message);
    }

    [Fact]
    public void DefaultHttpUrl_ListensOnLoopbackOnly()
    {
        var uri = new Uri(new AzureDevOpsServerOptions().HttpUrl);

        Assert.True(uri.IsLoopback);
    }
}

using AzureDevOpsServer.Mcp.Configuration;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Configuration;

public sealed class ApiVersionForTests
{
    [Theory]
    [InlineData(ApiArea.Core)]
    [InlineData(ApiArea.WorkItems)]
    [InlineData(ApiArea.Git)]
    [InlineData(ApiArea.Build)]
    [InlineData(ApiArea.Release)]
    [InlineData(ApiArea.Wiki)]
    public void ApiVersionFor_WithoutOverrides_UsesGlobalVersion(ApiArea area)
    {
        var options = new AzureDevOpsServerOptions
        {
            ApiVersion = "6.0"
        };

        Assert.Equal("6.0", options.ApiVersionFor(area));
    }

    [Fact]
    public void ApiVersionFor_WithAreaOverrides_UsesAreaVersion()
    {
        var options = new AzureDevOpsServerOptions
        {
            ApiVersion = "7.0",
            ReleaseApiVersion = "5.0",
            WikiApiVersion = "6.0"
        };

        Assert.Equal("5.0", options.ApiVersionFor(ApiArea.Release));
        Assert.Equal("6.0", options.ApiVersionFor(ApiArea.Wiki));
        Assert.Equal("7.0", options.ApiVersionFor(ApiArea.Git));
    }

    [Fact]
    public void ApiVersionFor_WithBlankOverride_FallsBackToGlobalVersion()
    {
        var options = new AzureDevOpsServerOptions
        {
            ApiVersion = "7.0",
            BuildApiVersion = "   "
        };

        Assert.Equal("7.0", options.ApiVersionFor(ApiArea.Build));
    }
}

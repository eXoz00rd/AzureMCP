using AzureDevOpsServer.Mcp.AzureDevOps;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class ResponseLimitsTests
{
    [Fact]
    public void ResolveTop_WhenValueIsNull_ReturnsDefault()
    {
        Assert.Equal(ResponseLimits.DefaultListTop, ResponseLimits.ResolveTop(null));
        Assert.Equal(ResponseLimits.DefaultBuildCount, ResponseLimits.ResolveTop(null, ResponseLimits.DefaultBuildCount));
    }

    [Theory]
    [InlineData(ResponseLimits.MinTop)]
    [InlineData(ResponseLimits.MaxTop)]
    [InlineData(250)]
    public void ResolveTop_WithinRange_ReturnsValue(int value)
    {
        Assert.Equal(value, ResponseLimits.ResolveTop(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ResponseLimits.MaxTop + 1)]
    [InlineData(int.MaxValue)]
    public void ResolveTop_OutOfRange_Throws(int value)
    {
        var exception = Assert.Throws<AzureDevOpsClientException>(() => ResponseLimits.ResolveTop(value));

        Assert.Contains("'top'", exception.Message);
        Assert.Contains("1", exception.Message);
        Assert.Contains("1000", exception.Message);
        Assert.Contains(value.ToString(), exception.Message);
    }

    [Theory]
    [InlineData(ResponseLimits.MinMaxChars)]
    [InlineData(ResponseLimits.MaxMaxChars)]
    public void ResolveMaxChars_WithinRange_ReturnsValue(int value)
    {
        Assert.Equal(value, ResponseLimits.ResolveMaxChars(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(ResponseLimits.MaxMaxChars + 1)]
    public void ResolveMaxChars_OutOfRange_Throws(int value)
    {
        var exception = Assert.Throws<AzureDevOpsClientException>(() => ResponseLimits.ResolveMaxChars(value));

        Assert.Contains("'maxChars'", exception.Message);
    }

    [Fact]
    public void ResolveMaxChars_WhenValueIsNull_ReturnsDefault()
    {
        Assert.Equal(ResponseLimits.DefaultMaxChars, ResponseLimits.ResolveMaxChars(null));
    }

    [Theory]
    [InlineData(ResponseLimits.MinMaxItems)]
    [InlineData(ResponseLimits.MaxMaxItems)]
    public void ResolveMaxItems_WithinRange_ReturnsValue(int value)
    {
        Assert.Equal(value, ResponseLimits.ResolveMaxItems(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ResponseLimits.MaxMaxItems + 1)]
    public void ResolveMaxItems_OutOfRange_Throws(int value)
    {
        var exception = Assert.Throws<AzureDevOpsClientException>(() => ResponseLimits.ResolveMaxItems(value));

        Assert.Contains("'maxItems'", exception.Message);
    }

    [Fact]
    public void ResolveMaxItems_WhenValueIsNull_ReturnsDefault()
    {
        Assert.Equal(ResponseLimits.DefaultMaxItems, ResponseLimits.ResolveMaxItems(null));
    }

    [Theory]
    [InlineData(ResponseLimits.MinDepth)]
    [InlineData(ResponseLimits.MaxDepth)]
    public void ResolveDepth_WithinRange_ReturnsValue(int value)
    {
        Assert.Equal(value, ResponseLimits.ResolveDepth(value, ResponseLimits.DefaultNodeDepth));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ResponseLimits.MaxDepth + 1)]
    public void ResolveDepth_OutOfRange_Throws(int value)
    {
        var exception = Assert.Throws<AzureDevOpsClientException>(
            () => ResponseLimits.ResolveDepth(value, ResponseLimits.DefaultNodeDepth)
        );

        Assert.Contains("'depth'", exception.Message);
    }

    [Fact]
    public void ResolveDepth_WhenValueIsNull_ReturnsGivenDefault()
    {
        Assert.Equal(
            ResponseLimits.DefaultQueryDepth,
            ResponseLimits.ResolveDepth(null, ResponseLimits.DefaultQueryDepth)
        );
    }
}

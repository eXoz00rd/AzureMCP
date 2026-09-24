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

    [Fact]
    public void Defaults_FitASmallModelContextWindow()
    {
        Assert.Equal(25, ResponseLimits.ResolveTop(null));
        Assert.Equal(8_000, ResponseLimits.ResolveMaxChars(null));
    }

    [Fact]
    public void LoweringDefaults_KeepsTheMaximumsAvailableToCallers()
    {
        Assert.Equal(1_000, ResponseLimits.ResolveTop(1_000));
        Assert.Equal(1_000_000, ResponseLimits.ResolveMaxChars(1_000_000));
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
    [InlineData(ResponseLimits.MinChars)]
    [InlineData(ResponseLimits.MaxChars)]
    public void ResolveMaxChars_WithinRange_ReturnsValue(int value)
    {
        Assert.Equal(value, ResponseLimits.ResolveMaxChars(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(ResponseLimits.MaxChars + 1)]
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
    [InlineData(ResponseLimits.MinItems)]
    [InlineData(ResponseLimits.MaxItems)]
    public void ResolveMaxItems_WithinRange_ReturnsValue(int value)
    {
        Assert.Equal(value, ResponseLimits.ResolveMaxItems(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ResponseLimits.MaxItems + 1)]
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

    [Theory]
    [InlineData(null, null)]
    [InlineData(1, null)]
    [InlineData(null, 1)]
    [InlineData(3, 3)]
    [InlineData(3, int.MaxValue)]
    public void ValidateLineRange_WithValidRange_DoesNotThrow(int? startLine, int? endLine)
    {
        ResponseLimits.ValidateLineRange(startLine, endLine);
    }

    [Theory]
    [InlineData(0, null, "'startLine'")]
    [InlineData(-4, 9, "'startLine'")]
    [InlineData(null, 0, "'endLine'")]
    [InlineData(2, -1, "'endLine'")]
    [InlineData(5, 4, "'endLine' must not be less than 'startLine'")]
    public void ValidateLineRange_WithInvalidRange_Throws(int? startLine, int? endLine, string expectedMessage)
    {
        var exception = Assert.Throws<AzureDevOpsClientException>(
            () => ResponseLimits.ValidateLineRange(startLine, endLine)
        );

        Assert.Contains(expectedMessage, exception.Message);
    }
}

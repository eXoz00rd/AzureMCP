using AzureDevOpsServer.Mcp.AzureDevOps;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class TextWindowTests
{
    [Fact]
    public void Apply_WithoutRange_ReturnsWholeText()
    {
        var result = TextWindow.Apply("one\ntwo\nthree", null, null, 100);

        Assert.Equal("one\ntwo\nthree", result.Text);
        Assert.Equal(13, result.TotalChars);
        Assert.Equal(3, result.TotalLines);
        Assert.False(result.Truncated);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("one", 1)]
    [InlineData("one\n", 1)]
    [InlineData("one\ntwo", 2)]
    [InlineData("one\ntwo\n", 2)]
    [InlineData("one\r\ntwo\r\n", 2)]
    [InlineData("\n", 1)]
    [InlineData("\n\n", 2)]
    public void Apply_CountsALineTerminatorAsEndingALineNotStartingAnother(string text, int expectedLines)
    {
        Assert.Equal(expectedLines, TextWindow.Apply(text, null, null, 100).TotalLines);
    }

    [Theory]
    [InlineData(1, 1, "one\n")]
    [InlineData(2, 3, "two\nthree\n")]
    [InlineData(3, null, "three\nfour")]
    [InlineData(null, 2, "one\ntwo\n")]
    [InlineData(4, 4, "four")]
    [InlineData(4, 99, "four")]
    [InlineData(5, null, "")]
    [InlineData(99, 100, "")]
    public void Apply_WithLineRange_ReturnsThoseLinesWithTheirTerminators(int? startLine, int? endLine, string expected)
    {
        var result = TextWindow.Apply("one\ntwo\nthree\nfour", startLine, endLine, 100);

        Assert.Equal(expected, result.Text);
        Assert.Equal(expected.Length, result.TotalChars);
        Assert.Equal(4, result.TotalLines);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Apply_WhenTheTextEndsWithATerminator_ReturnsNothingPastTheLastLine()
    {
        var result = TextWindow.Apply("one\ntwo\n", 3, null, 100);

        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(2, result.TotalLines);
    }

    [Theory]
    [InlineData(11, "0123456789", false)]
    [InlineData(10, "0123456789", false)]
    [InlineData(9, "012345678", true)]
    [InlineData(1, "0", true)]
    public void Apply_LimitsCharactersAndReportsWhetherAnythingWasCut(int maxChars, string expected, bool truncated)
    {
        var result = TextWindow.Apply("0123456789", null, null, maxChars);

        Assert.Equal(expected, result.Text);
        Assert.Equal(10, result.TotalChars);
        Assert.Equal(truncated, result.Truncated);
    }

    [Fact]
    public void Apply_NeverLeavesHalfASurrogatePairAtTheCut()
    {
        var result = TextWindow.Apply("ab\U0001F600cd", null, null, 3);

        Assert.Equal("ab", result.Text);
        Assert.True(result.Truncated);
        Assert.Equal(6, result.TotalChars);
    }
}

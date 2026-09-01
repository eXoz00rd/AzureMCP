using System.Text;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class BoundedTextTests
{
    [Fact]
    public async Task ReadAsync_WhenContentFitsLimit_ReturnsWholeText()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("build succeeded"));

        var result = await BoundedText.ReadAsync(stream, 100, TestContext.Current.CancellationToken);

        Assert.Equal("build succeeded", result.Text);
        Assert.Equal(15, result.TotalChars);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task ReadAsync_WhenContentExceedsLimit_TruncatesAndCountsTotal()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 5000)));

        var result = await BoundedText.ReadAsync(stream, 100, TestContext.Current.CancellationToken);

        Assert.Equal(100, result.Text.Length);
        Assert.Equal(5000, result.TotalChars);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task ReadAsync_WhenContentExactlyMatchesLimit_DoesNotReportTruncation()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 100)));

        var result = await BoundedText.ReadAsync(stream, 100, TestContext.Current.CancellationToken);

        Assert.Equal(100, result.Text.Length);
        Assert.Equal(100, result.TotalChars);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task ReadAsync_WhenStreamIsFarLargerThanLimit_KeepsOnlyTheLimit()
    {
        const long length = 5_000_000;
        await using var stream = new GeneratedStream(length);

        var result = await BoundedText.ReadAsync(stream, 1_000, TestContext.Current.CancellationToken);

        Assert.Equal(1_000, result.Text.Length);
        Assert.Equal(length, result.TotalChars);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task ReadAsync_WhenMultiByteCharactersSpanChunks_CountsCharactersNotBytes()
    {
        var text = string.Concat(Enumerable.Repeat("ąćęłń", 4_000));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var result = await BoundedText.ReadAsync(stream, 12_000, TestContext.Current.CancellationToken);

        Assert.Equal(12_000, result.Text.Length);
        Assert.Equal(text.Length, result.TotalChars);
        Assert.Equal(text[..12_000], result.Text);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task ReadAsync_WhenCancelledMidStream_StopsReading()
    {
        using var cancellation = new CancellationTokenSource();
        await using var stream = new GeneratedStream(50_000_000, 'x', cancellation, 3);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => BoundedText.ReadAsync(stream, int.MaxValue, cancellation.Token)
        );

        Assert.True(stream.BytesProduced < 50_000_000);
    }

    [Fact]
    public async Task ReadAsync_WhenLimitIsZero_ReturnsEmptyTextAndReportsTruncation()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("log"));

        var result = await BoundedText.ReadAsync(stream, 0, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(3, result.TotalChars);
        Assert.True(result.Truncated);
    }
}

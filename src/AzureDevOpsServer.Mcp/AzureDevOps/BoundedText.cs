using System.Text;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

internal static class BoundedText
{
    private const int ChunkSize = 8192;

    public static async Task<BoundedTextResult> ReadAsync(
        HttpContent content,
        int maxChars,
        CancellationToken cancellationToken)
    {
        var stream = await content.ReadAsStreamAsync(cancellationToken);
        await using (stream.ConfigureAwait(false))
        {
            return await ReadAsync(stream, maxChars, cancellationToken);
        }
    }

    // Retains at most maxChars characters while still counting the whole stream, so a
    // large response cannot allocate far beyond the limit the caller asked for.
    public static async Task<BoundedTextResult> ReadAsync(
        Stream stream,
        int maxChars,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(maxChars, 0);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, ChunkSize, true);
        var builder = new StringBuilder(Math.Min(limit, ChunkSize));
        var buffer = new char[ChunkSize];
        long total = 0;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            var remaining = limit - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }

            total += read;
        }

        return new BoundedTextResult(
            builder.ToString(),
            (int)Math.Min(total, int.MaxValue),
            total > limit
        );
    }
}

internal readonly record struct BoundedTextResult(string Text, int TotalChars, bool Truncated);

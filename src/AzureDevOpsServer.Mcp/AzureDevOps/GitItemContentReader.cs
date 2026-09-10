namespace AzureDevOpsServer.Mcp.AzureDevOps;

// The repository item endpoint embeds file content as a JSON string inside an envelope object
// (path, contentMetadata, content, ...). System.Text.Json cannot return a string token until the
// whole token is buffered, so a large file would be fully materialized before any limit applies.
// This reader walks the envelope by hand, decoding the "content" (and "path") string values one
// UTF-8 byte at a time so memory stays bounded by maxChars regardless of how large the file is.
internal static class GitItemContentReader
{
    private const int BinarySampleChars = 8000;
    private const int MaxPropertyNameChars = 256;
    private const int MaxPathChars = 4096;
    private const int BufferSize = 8192;

    public static async Task<GitItemContentReadResult> ReadAsync(
        Stream stream,
        int maxChars,
        CancellationToken cancellationToken)
    {
        var cursor = new Utf8StreamCursor(stream, BufferSize);

        await cursor.SkipWhitespaceAsync(cancellationToken).ConfigureAwait(false);
        if (!await cursor.TryConsumeAsync((byte)'{', cancellationToken).ConfigureAwait(false))
        {
            throw new AzureDevOpsClientException("The item response could not be parsed: expected a JSON object.");
        }

        string? path = null;
        DecodedJsonString? content = null;

        await cursor.SkipWhitespaceAsync(cancellationToken).ConfigureAwait(false);
        if (!await cursor.TryConsumeAsync((byte)'}', cancellationToken).ConfigureAwait(false))
        {
            while (true)
            {
                await cursor.SkipWhitespaceAsync(cancellationToken).ConfigureAwait(false);
                await cursor.ExpectAsync((byte)'"', cancellationToken).ConfigureAwait(false);
                var propertyName = await cursor
                                         .ReadStringAsync(MaxPropertyNameChars, 0, cancellationToken)
                                         .ConfigureAwait(false);

                await cursor.SkipWhitespaceAsync(cancellationToken).ConfigureAwait(false);
                await cursor.ExpectAsync((byte)':', cancellationToken).ConfigureAwait(false);
                await cursor.SkipWhitespaceAsync(cancellationToken).ConfigureAwait(false);

                if (propertyName.Kept == "content")
                {
                    await cursor.ExpectAsync((byte)'"', cancellationToken).ConfigureAwait(false);
                    content = await cursor
                                     .ReadStringAsync(maxChars, BinarySampleChars, cancellationToken)
                                     .ConfigureAwait(false);
                }
                else if (propertyName.Kept == "path")
                {
                    await cursor.ExpectAsync((byte)'"', cancellationToken).ConfigureAwait(false);
                    var decoded = await cursor
                                        .ReadStringAsync(MaxPathChars, 0, cancellationToken)
                                        .ConfigureAwait(false);
                    if (decoded.Truncated)
                    {
                        throw new AzureDevOpsClientException(
                            $"The item response could not be parsed: 'path' exceeds the maximum supported length of {MaxPathChars} characters."
                        );
                    }

                    path = decoded.Kept;
                }
                else
                {
                    await cursor.SkipValueAsync(cancellationToken).ConfigureAwait(false);
                }

                await cursor.SkipWhitespaceAsync(cancellationToken).ConfigureAwait(false);
                if (await cursor.TryConsumeAsync((byte)',', cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                await cursor.ExpectAsync((byte)'}', cancellationToken).ConfigureAwait(false);
                break;
            }
        }

        return new GitItemContentReadResult(
            path,
            content?.Kept ?? string.Empty,
            content?.TotalChars ?? 0,
            content?.Truncated ?? false,
            content?.Sample ?? string.Empty
        );
    }
}

internal readonly record struct GitItemContentReadResult(
    string? Path,
    string? Content,
    int TotalChars,
    bool Truncated,
    string BinarySample);

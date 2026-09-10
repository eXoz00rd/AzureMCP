using System.Text;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

// Buffered byte-at-a-time reader over a Stream for hand-rolled JSON parsing that must bound how
// many characters of a single large string token it keeps in memory. Refills happen only when the
// internal buffer is exhausted, so decoding stays cheap while still being correct across arbitrary
// network chunk boundaries.
internal sealed class Utf8StreamCursor
{
    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private int _bufferStart;
    private int _bufferLength;

    public Utf8StreamCursor(Stream stream, int bufferSize)
    {
        _stream = stream;
        _buffer = new byte[bufferSize];
    }

    public async ValueTask SkipWhitespaceAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var next = await PeekAsync(cancellationToken).ConfigureAwait(false);
            if (next is null || !IsWhitespace(next.Value))
            {
                return;
            }

            await ReadByteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask ExpectAsync(byte expected, CancellationToken cancellationToken)
    {
        var actual = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
        if (actual != expected)
        {
            throw new AzureDevOpsClientException(
                $"The item response could not be parsed: expected '{(char)expected}' but found " +
                (actual is null ? "end of stream." : $"'{(char)actual.Value}'.")
            );
        }
    }

    public async ValueTask<bool> TryConsumeAsync(byte expected, CancellationToken cancellationToken)
    {
        var next = await PeekAsync(cancellationToken).ConfigureAwait(false);
        if (next != expected)
        {
            return false;
        }

        await ReadByteAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // Decodes a JSON string value assuming the opening quote was already consumed. Keeps at most
    // maxKeepChars characters for the returned result and, independently, up to sampleChars
    // characters for callers that need an early sample regardless of maxKeepChars (binary
    // detection). TotalChars always reflects the full decoded length.
    public async ValueTask<DecodedJsonString> ReadStringAsync(
        int maxKeepChars,
        int sampleChars,
        CancellationToken cancellationToken)
    {
        var captureLimit = Math.Max(Math.Max(maxKeepChars, sampleChars), 0);
        var builder = captureLimit == 0 ? null : new StringBuilder(Math.Min(captureLimit, 4096));
        var total = 0L;

        while (true)
        {
            var b = await ReadByteAsync(cancellationToken).ConfigureAwait(false) ??
                throw new AzureDevOpsClientException("The item response could not be parsed: unterminated string.");

            if (b == (byte)'"')
            {
                break;
            }

            if (b < 0x20)
            {
                throw new AzureDevOpsClientException(
                    "The item response could not be parsed: unescaped control character in string."
                );
            }

            if (b == (byte)'\\')
            {
                var escape = await ReadByteAsync(cancellationToken).ConfigureAwait(false) ??
                    throw new AzureDevOpsClientException(
                        "The item response could not be parsed: unterminated escape sequence."
                    );

                if (escape == (byte)'u')
                {
                    var unit = (char)await ReadHex4Async(cancellationToken).ConfigureAwait(false);
                    if (char.IsHighSurrogate(unit))
                    {
                        var low = await ReadLowSurrogateEscapeAsync(cancellationToken).ConfigureAwait(false);
                        AppendPair(builder, captureLimit, unit, low);
                        total += 2;
                    }
                    else if (char.IsLowSurrogate(unit))
                    {
                        throw new AzureDevOpsClientException(
                            "The item response could not be parsed: unpaired low surrogate in unicode escape."
                        );
                    }
                    else
                    {
                        Append(builder, captureLimit, unit);
                        total++;
                    }

                    continue;
                }

                var decoded = escape switch
                {
                    (byte)'"' => '"',
                    (byte)'\\' => '\\',
                    (byte)'/' => '/',
                    (byte)'b' => '\b',
                    (byte)'f' => '\f',
                    (byte)'n' => '\n',
                    (byte)'r' => '\r',
                    (byte)'t' => '\t',
                    _ => throw new AzureDevOpsClientException(
                        $"The item response could not be parsed: invalid escape '\\{(char)escape}'."
                    )
                };

                Append(builder, captureLimit, decoded);
                total++;
                continue;
            }

            var (first, second) = await DecodeUtf8CharAsync(b, cancellationToken).ConfigureAwait(false);
            if (second is null)
            {
                Append(builder, captureLimit, first);
                total++;
            }
            else
            {
                AppendPair(builder, captureLimit, first, second.Value);
                total += 2;
            }
        }

        var text = builder?.ToString() ?? string.Empty;
        return new DecodedJsonString(
            Truncate(text, maxKeepChars),
            Truncate(text, sampleChars),
            (int)Math.Min(total, int.MaxValue),
            total > maxKeepChars
        );
    }

    // Skips a single JSON value of any type, assuming any leading whitespace was already consumed.
    public async ValueTask SkipValueAsync(CancellationToken cancellationToken)
    {
        var next = await PeekAsync(cancellationToken).ConfigureAwait(false) ??
            throw new AzureDevOpsClientException("The item response could not be parsed: expected a value.");

        if (next == (byte)'"')
        {
            await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            await ReadStringAsync(0, 0, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (next is (byte)'{' or (byte)'[')
        {
            await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            var depth = 1;
            while (depth > 0)
            {
                var b = await ReadByteAsync(cancellationToken).ConfigureAwait(false) ??
                    throw new AzureDevOpsClientException(
                        "The item response could not be parsed: unterminated object or array."
                    );

                switch (b)
                {
                    case (byte)'"':
                        await ReadStringAsync(0, 0, cancellationToken).ConfigureAwait(false);
                        break;
                    case (byte)'{' or (byte)'[':
                        depth++;
                        break;
                    case (byte)'}' or (byte)']':
                        depth--;
                        break;
                }
            }

            return;
        }

        while (true)
        {
            var b = await PeekAsync(cancellationToken).ConfigureAwait(false);
            if (b is null || IsWhitespace(b.Value) || b is (byte)',' or (byte)'}' or (byte)']')
            {
                return;
            }

            await ReadByteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<(char First, char? Second)> DecodeUtf8CharAsync(byte first, CancellationToken cancellationToken)
    {
        if (first < 0x80)
        {
            return ((char)first, null);
        }

        int extraBytes;
        int codepoint;
        int minCodepoint;
        if ((first & 0b1110_0000) == 0b1100_0000)
        {
            extraBytes = 1;
            codepoint = first & 0b0001_1111;
            minCodepoint = 0x80;
        }
        else if ((first & 0b1111_0000) == 0b1110_0000)
        {
            extraBytes = 2;
            codepoint = first & 0b0000_1111;
            minCodepoint = 0x800;
        }
        else if ((first & 0b1111_1000) == 0b1111_0000)
        {
            extraBytes = 3;
            codepoint = first & 0b0000_0111;
            minCodepoint = 0x10000;
        }
        else
        {
            throw new AzureDevOpsClientException("The item response could not be parsed: invalid UTF-8 byte sequence.");
        }

        for (var i = 0; i < extraBytes; i++)
        {
            var next = await ReadByteAsync(cancellationToken).ConfigureAwait(false) ??
                throw new AzureDevOpsClientException(
                    "The item response could not be parsed: truncated UTF-8 byte sequence."
                );
            if ((next & 0b1100_0000) != 0b1000_0000)
            {
                throw new AzureDevOpsClientException(
                    "The item response could not be parsed: invalid UTF-8 continuation byte."
                );
            }

            codepoint = (codepoint << 6) | (next & 0b0011_1111);
        }

        // Rejects overlong encodings (codepoint below the shortest valid form), UTF-16 surrogate
        // code points (never legal in UTF-8), and anything above the maximum Unicode code point.
        if (codepoint < minCodepoint || codepoint > 0x10FFFF || (codepoint >= 0xD800 && codepoint <= 0xDFFF))
        {
            throw new AzureDevOpsClientException("The item response could not be parsed: invalid UTF-8 code point.");
        }

        if (codepoint <= 0xFFFF)
        {
            return ((char)codepoint, null);
        }

        codepoint -= 0x10000;
        var high = (char)(0xD800 + (codepoint >> 10));
        var low = (char)(0xDC00 + (codepoint & 0x3FF));
        return (high, low);
    }

    // A high surrogate escape must be immediately followed by a second \u escape decoding to a low
    // surrogate; anything else (no second escape, or one that isn't a low surrogate) is rejected
    // rather than left as an unpaired surrogate in the decoded string.
    private async ValueTask<char> ReadLowSurrogateEscapeAsync(CancellationToken cancellationToken)
    {
        var backslash = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
        var escape = backslash == (byte)'\\' ?
            await ReadByteAsync(cancellationToken).ConfigureAwait(false) :
            null;
        if (backslash != (byte)'\\' || escape != (byte)'u')
        {
            throw new AzureDevOpsClientException(
                "The item response could not be parsed: high surrogate not followed by a low surrogate escape."
            );
        }

        var unit = (char)await ReadHex4Async(cancellationToken).ConfigureAwait(false);
        if (!char.IsLowSurrogate(unit))
        {
            throw new AzureDevOpsClientException(
                "The item response could not be parsed: high surrogate not followed by a low surrogate escape."
            );
        }

        return unit;
    }

    private async ValueTask<int> ReadHex4Async(CancellationToken cancellationToken)
    {
        var value = 0;
        for (var i = 0; i < 4; i++)
        {
            var b = await ReadByteAsync(cancellationToken).ConfigureAwait(false) ??
                throw new AzureDevOpsClientException(
                    "The item response could not be parsed: truncated unicode escape."
                );
            value = (value << 4) | HexDigitValue(b);
        }

        return value;
    }

    private static int HexDigitValue(byte b)
    {
        return b switch
        {
            >= (byte)'0' and <= (byte)'9' => b - (byte)'0',
            >= (byte)'a' and <= (byte)'f' => b - (byte)'a' + 10,
            >= (byte)'A' and <= (byte)'F' => b - (byte)'A' + 10,
            _ => throw new AzureDevOpsClientException(
                "The item response could not be parsed: invalid unicode escape digit."
            )
        };
    }

    private static void Append(StringBuilder? builder, int captureLimit, char value)
    {
        if (builder is not null && builder.Length < captureLimit)
        {
            builder.Append(value);
        }
    }

    // Appends a surrogate pair as a single unit: if there is room for only one of the two code
    // units, neither is appended, so the capture never ends on an unpaired high surrogate.
    private static void AppendPair(StringBuilder? builder, int captureLimit, char high, char low)
    {
        if (builder is not null && builder.Length + 2 <= captureLimit)
        {
            builder.Append(high).Append(low);
        }
    }

    // Slices at maxChars, backing off by one character if that would otherwise cut a surrogate pair
    // in half and leave an unpaired high surrogate at the end of the result.
    private static string Truncate(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        var cut = Math.Max(maxChars, 0);
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut];
    }

    private static bool IsWhitespace(byte b)
    {
        return b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r';
    }

    private async ValueTask<byte?> PeekAsync(CancellationToken cancellationToken)
    {
        if (_bufferStart >= _bufferLength && !await RefillAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return _buffer[_bufferStart];
    }

    private async ValueTask<byte?> ReadByteAsync(CancellationToken cancellationToken)
    {
        if (_bufferStart >= _bufferLength && !await RefillAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return _buffer[_bufferStart++];
    }

    private async ValueTask<bool> RefillAsync(CancellationToken cancellationToken)
    {
        _bufferLength = await _stream.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        _bufferStart = 0;
        return _bufferLength > 0;
    }
}

internal readonly record struct DecodedJsonString(string Kept, string Sample, int TotalChars, bool Truncated);

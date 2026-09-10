using System.Text;
using System.Text.Json;

namespace AzureDevOpsServer.Mcp.Tests.Infrastructure;

// Produces {"path":"<path>","content":"<fillLength filler bytes>"} without ever materializing the
// filler, so tests can exercise repository item responses far larger than any buffer under test.
// The fill character is restricted to plain printable ASCII so each filler byte is always exactly
// one valid, unescaped JSON content character.
public sealed class JsonEnvelopeStream : Stream
{
    private readonly byte[] _prefix;
    private readonly byte[] _suffix;
    private readonly long _fillLength;
    private readonly byte _fill;
    private readonly CancellationTokenSource? _cancelSource;
    private readonly int _cancelAfterReads;

    private long _position;
    private int _reads;

    public JsonEnvelopeStream(
        string path,
        long fillLength,
        char fill = 'x',
        CancellationTokenSource? cancelSource = null,
        int cancelAfterReads = 0)
    {
        if (fill is < (char)0x20 or > (char)0x7E or '"' or '\\')
        {
            throw new ArgumentOutOfRangeException(
                nameof(fill),
                fill,
                "Fill character must be printable ASCII other than '\"' or '\\', so every filler byte is a single valid, unescaped JSON content character."
            );
        }

        _prefix = Encoding.UTF8.GetBytes($"{{\"path\":{JsonSerializer.Serialize(path)},\"content\":\"");
        _suffix = "\"}"u8.ToArray();
        _fillLength = fillLength;
        _fill = (byte)fill;
        _cancelSource = cancelSource;
        _cancelAfterReads = cancelAfterReads;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _prefix.Length + _fillLength + _suffix.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty || _position >= Length)
        {
            return 0;
        }

        if (_position < _prefix.Length)
        {
            return FillFrom(_prefix, (int)_position, buffer);
        }

        var fillEnd = _prefix.Length + _fillLength;
        if (_position < fillEnd)
        {
            var count = (int)Math.Min(buffer.Length, fillEnd - _position);
            buffer[..count].Fill(_fill);
            _position += count;
            return count;
        }

        return FillFrom(_suffix, (int)(_position - fillEnd), buffer);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _reads++;
        if (_cancelSource is not null && _reads >= _cancelAfterReads)
        {
            _cancelSource.Cancel();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    private int FillFrom(byte[] source, int sourceOffset, Span<byte> buffer)
    {
        var count = Math.Min(buffer.Length, source.Length - sourceOffset);
        source.AsSpan(sourceOffset, count).CopyTo(buffer);
        _position += count;
        return count;
    }
}

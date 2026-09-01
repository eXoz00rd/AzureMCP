namespace AzureDevOpsServer.Mcp.Tests.Infrastructure;

// Produces a fixed number of single-byte characters without ever materializing them,
// so tests can exercise responses far larger than any buffer under test.
public sealed class GeneratedStream : Stream
{
    private readonly byte _fill;
    private readonly long _length;
    private readonly CancellationTokenSource? _cancelSource;
    private readonly int _cancelAfterReads;

    private long _position;
    private int _reads;

    public GeneratedStream(
        long length,
        char fill = 'x',
        CancellationTokenSource? cancelSource = null,
        int cancelAfterReads = 0)
    {
        _length = length;
        _fill = (byte)fill;
        _cancelSource = cancelSource;
        _cancelAfterReads = cancelAfterReads;
    }

    public long BytesProduced => _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _length;

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
        var remaining = _length - _position;
        if (remaining <= 0)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, remaining);
        buffer[..count].Fill(_fill);
        _position += count;
        return count;
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
}

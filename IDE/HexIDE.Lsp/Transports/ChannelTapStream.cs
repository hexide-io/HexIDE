namespace HexIDE.Lsp;

/// <summary>
/// A stream that passes everything through and says, once each, when bytes first started into the channel, when
/// they first finished reaching it, when the first read began waiting, and when bytes first came back out of it.
/// </summary>
/// <remarks>
/// <para>
/// The record already says when a message was <i>serialized</i>: the formatter is where it taps. That is not
/// when the bytes reached the server. Between the two sit StreamJsonRpc's pipe writer, the background loop that
/// copies it into this stream, and the stream's own write, and a message held anywhere in that chain looks, from
/// the record, exactly like a server that received it and said nothing. A foreign server answering nothing to
/// <c>initialize</c> for thirty seconds, then dying of SIGPIPE the moment the connection was torn down, is that
/// ambiguity, and it is why this exists (hexide-io/HexIDE#390).
/// </para>
/// <para>
/// Once each, because what is being asked is whether the channel works at all and when it started to. Every
/// message after the first is already in the record with its own time.
/// </para>
/// </remarks>
internal sealed class ChannelTapStream(Stream inner, Action<string> note) : Stream
{
    private int _writeStarted;
    private int _writeReached;
    private int _readStarted;
    private int _readArrived;
    private long _bytesWritten;

    public override bool CanRead => inner.CanRead;
    public override bool CanWrite => inner.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    // ── writing ──────────────────────────────────────────────────────────────────────────────────

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Starting(buffer.Length);
        inner.Write(buffer);
        Interlocked.Add(ref _bytesWritten, buffer.Length);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Starting(buffer.Length);
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        Interlocked.Add(ref _bytesWritten, buffer.Length);
    }

    // A flush is where a write is finished: StreamJsonRpc's copy loop writes a message's segments and then
    // flushes, and a buffered stream holds them until it does.
    public override void Flush()
    {
        inner.Flush();
        Reached();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        Reached();
    }

    private void Starting(int count)
    {
        if (count > 0 && Interlocked.Exchange(ref _writeStarted, 1) == 0)
            note($"began writing {count} bytes to the server");
    }

    private void Reached()
    {
        var written = Interlocked.Read(ref _bytesWritten);
        if (written > 0 && Interlocked.Exchange(ref _writeReached, 1) == 0)
            note($"the first {written} bytes reached the server's channel");
    }

    // ── reading ──────────────────────────────────────────────────────────────────────────────────

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        Waiting();
        return Arrived(inner.Read(buffer));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Waiting();
        return Arrived(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));
    }

    // A read that was never issued and one that was issued and never answered are different faults: ours, and the
    // server's. Without this the record cannot tell them apart.
    private void Waiting()
    {
        if (Interlocked.Exchange(ref _readStarted, 1) == 0)
            note("began waiting for the server's first bytes");
    }

    private int Arrived(int count)
    {
        if (count > 0 && Interlocked.Exchange(ref _readArrived, 1) == 0)
            note($"the first {count} bytes arrived from the server");
        return count;
    }

    // ── lifetime ─────────────────────────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

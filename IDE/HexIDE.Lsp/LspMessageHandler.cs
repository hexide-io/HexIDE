using System.Buffers;
using System.IO.Pipelines;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Lsp;

// #3520 / #4484: StreamJsonRpc's last read-loop AdvanceTo can race DisposeReader's Complete as a
// connection ends, releasing one owner twice. This is StreamJsonRpc's race, not .NET's or Nerdbank's.
// Upstream: microsoft/vs-streamjsonrpc#1605. Keep read buffers inside each connection and return each once.
// Round 8 suppressed 3 second disposes with 0 subsequent rents from those pools: TCP checked 2.51M
// frames / 240k teardowns, in-memory 827k / 122k, all 0 bad. Even without the guard a second release
// would stay inside a dying connection; with it the second release never reaches the array pool.
// The explicit adapters retain the Stream constructor's ownership, including its cancellation guard.
//
// The owner's part: a stream used for both reading and writing (TCP, named pipes) is closed here only once
// the last write ends. PipeWriter.Create writes through to the stream, so a peer that stops reading blocks
// that write, and once RPC is disposed nobody reads the peer's writes either. Neither side can then end the
// connection; its owner must close the socket or pipe after disposing RPC. Every transport does: StopAsync
// and the restart dispose it right after DisposeRpc, and ConnectAsync disposes the old connection before a
// reconnect. BlockedConnectionStopTests fails for a transport that doesn't. (StreamJsonRpc's Stream
// constructor buffers writes in a Nerdbank pipe drained by its own task instead.)
public sealed class LspMessageHandler : HeaderDelimitedMessageHandler
{
    private readonly Stream _writeTo;
    private readonly Stream _readFrom;
    internal ConnectionMemoryPool ReadPool { get; }

    public LspMessageHandler(Stream writeTo, Stream readFrom, IJsonRpcMessageFormatter formatter,
        ILogger? logger = null)
        : this(writeTo, readFrom, formatter, new ConnectionMemoryPool(logger)) { }

    private LspMessageHandler(Stream writeTo, Stream readFrom, IJsonRpcMessageFormatter formatter,
        ConnectionMemoryPool pool)
        : base(PipeWriter.Create(writeTo, new StreamPipeWriterOptions(leaveOpen: true)),
            PipeReader.Create(readFrom, new StreamPipeReaderOptions(pool: pool, leaveOpen: true)), formatter)
    {
        _writeTo = writeTo;
        _readFrom = readFrom;
        ReadPool = pool;
        // Some streams ignore read cancellation. Match StreamJsonRpc's Stream constructor so the
        // pending read ends even when it has a different stream from the writer.
        if (!ReferenceEquals(readFrom, writeTo))
            DisposalToken.Register(static stream => DisposeStream((Stream)stream!), readFrom);
    }

    protected override void DisposeWriter()
    {
        try { base.DisposeWriter(); } catch { /* disposal must not escape a worker callback */ }
        finally { DisposeStream(_writeTo); }
    }

    protected override void DisposeReader()
    {
        try { base.DisposeReader(); } catch { /* the read loop may already have completed it */ }
        finally
        {
            if (!ReferenceEquals(_readFrom, _writeTo)) DisposeStream(_readFrom);
        }
    }

    private static void DisposeStream(Stream stream)
    {
        try { stream.Dispose(); } catch { /* disposal must never throw */ }
    }
}

internal sealed class ConnectionMemoryPool(ILogger? logger) : MemoryPool<byte>
{
    private readonly ArrayPool<byte> _arrays = ArrayPool<byte>.Create();
    private long _suppressedDisposals;
    private long _rentsAfterSuppressed;
    internal long SuppressedDisposals => Interlocked.Read(ref _suppressedDisposals);
    internal long RentsAfterSuppressed => Interlocked.Read(ref _rentsAfterSuppressed);
    public override int MaxBufferSize => int.MaxValue;

    public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
    {
        if (SuppressedDisposals != 0) Interlocked.Increment(ref _rentsAfterSuppressed);
        return new Owner(this, _arrays.Rent(Math.Max(minBufferSize, 4096)));
    }

    // Outstanding owners can return on the read loop after handler disposal. The private array pool
    // becomes collectible with the connection and its last owner, rather than joining a shared pool.
    protected override void Dispose(bool disposing) { }

    private sealed class Owner(ConnectionMemoryPool pool, byte[] array) : IMemoryOwner<byte>
    {
        private readonly byte[] _array = array;
        private byte[]? _held = array;
        public Memory<byte> Memory => _array;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _held, null) is { } held)
            {
                pool._arrays.Return(held);
                return;
            }
            if (Interlocked.Increment(ref pool._suppressedDisposals) == 1)
            {
                try { pool.Logger?.LogDebug("LSP connection suppressed a second read-buffer disposal (#4484)"); }
                catch { /* logging must not let disposal throw */ }
            }
        }

    }

    private ILogger? Logger => logger;
}

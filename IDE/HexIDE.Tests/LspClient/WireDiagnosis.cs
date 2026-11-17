using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

// Shared evidence for the intermittent in-memory connection drops (#3520, #3677, #3908).
internal sealed class WireDiagnosis(CancellationToken testToken)
{
    private readonly long started = System.Diagnostics.Stopwatch.GetTimestamp();
    private readonly ConcurrentQueue<string> drops = new();
    private readonly ConcurrentQueue<RecordingLogger> logs = new();
    private readonly ConcurrentQueue<ReadTap> reads = new();
    // VBLspClient reports its private JsonRpc's disconnect reason through its logger.
    public bool HasDropped => !drops.IsEmpty || logs.Any(l => l.Lines.Any(line => line.Contains("VB LSP connection lost:")));
    public string Elapsed() => $"{System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}ms";

    public ReadTap TapPeer(Stream stream)
    {
        var tap = new ReadTap(stream);
        reads.Enqueue(tap);
        return tap;
    }

    public void Watch(JsonRpc rpc, string side, int number) =>
        rpc.Disconnected += (_, e) => drops.Enqueue($"{Elapsed()} {side} {number} {e.Reason}: {e.Description}"
            + (e.Exception is null ? "" : $" {e.Exception}"));

    public RecordingLogger<T> ClientLog<T>(int number)
    {
        var log = new RecordingLogger<T>(this, number);
        logs.Enqueue(log);
        return log;
    }

    public async Task<T> Call<T>(Task<T> call)
    {
        try { return await call; }
        catch (ConnectionLostException e) { throw new InvalidOperationException(Diagnosis("a call lost its connection"), e); }
    }

    public async Task Call(Task call)
    {
        try { await call; }
        catch (ConnectionLostException e) { throw new InvalidOperationException(Diagnosis("a call lost its connection"), e); }
    }

    public string Diagnosis(string what, string clients = "")
    {
        var read = reads.Select((tap, i) => $"stand-in {i + 1} read: <{tap.Recent()}>");
        return $"#3520: {what} at {Elapsed()}. Test token cancelled={testToken.IsCancellationRequested}; "
            + $"thread pool threads={ThreadPool.ThreadCount}, queued={ThreadPool.PendingWorkItemCount}. "
            + $"{(clients.Length == 0 ? "" : clients + ". ")}Stand-in drops: [{string.Join(" | ", drops)}]. "
            + $"Client log: [{string.Join(" | ", logs.SelectMany(l => l.Lines))}]. "
            + $"Last bytes each stand-in read: [{string.Join(" || ", read)}]";
    }

    // The stand-in's end of the pair, keeping the last bytes it read, so a frame it couldn't parse can be shown.
    internal sealed class ReadTap(Stream inner) : Stream
    {
        private const int Keep = 4096;
        private readonly Queue<byte> recent = new();

        public string Recent()
        {
            byte[] bytes;
            lock (recent) bytes = [.. recent];
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            return text.Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private void Record(ReadOnlySpan<byte> read)
        {
            lock (recent)
                foreach (var b in read)
                {
                    recent.Enqueue(b);
                    if (recent.Count > Keep) recent.Dequeue();
                }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await inner.ReadAsync(buffer, cancellationToken);
            Record(buffer.Span[..n]);
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            Record(buffer.AsSpan(offset, n));
            return n;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.WriteAsync(buffer, offset, count, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override void Flush() => inner.Flush();
        public override bool CanRead => inner.CanRead;
        public override bool CanWrite => inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class RecordingLogger<T>(WireDiagnosis test, int connection)
        : RecordingLogger(test, connection), ILogger<T> { }

    internal class RecordingLogger(WireDiagnosis test, int connection) : ILogger
    {
        public readonly ConcurrentQueue<string> Lines = new();
        public List<string> Warnings { get; } = [];
        public List<string> Debugs { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            Lines.Enqueue($"{test.Elapsed()} client {connection} {logLevel}: {message}"
                + (exception is null ? "" : $" {exception}"));
            switch (logLevel)
            {
                case LogLevel.Warning: lock (Warnings) Warnings.Add(message); break;
                case LogLevel.Debug or LogLevel.Trace: lock (Debugs) Debugs.Add(message); break;
            }
        }
    }
}

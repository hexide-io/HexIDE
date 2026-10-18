using System.Text.Json;
using System.Threading.Channels;

namespace HexIDE.Dap;

/// <summary>
/// One DAP connection over owned streams. Reads start immediately. Consume Events promptly; an overflowing
/// event queue closes the connection rather than dropping debugger state or growing memory without a bound.
/// No UI dispatch, process ownership, handshake policy or automatic retry is hidden in this wire layer.
/// </summary>
public sealed class DapConnection : IAsyncDisposable
{
    private sealed record Pending(string Command, TaskCompletionSource<DapResponse> Reply);
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly DapConnectionOptions _options;
    private readonly DapFraming _framing;
    private readonly object _sync = new();
    private readonly Dictionary<int, Pending> _pending = [];
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<DapEvent> _events;
    private readonly Task<Exception?> _reader;
    private bool _closed;
    private Exception? _failure;
    private int _sequence;

    /// <param name="input">Adapter output; must support cancellation or be unblocked by disposal.</param>
    /// <param name="output">Adapter input; must support cancellation or be unblocked by disposal.</param>
    /// <param name="options">Limits copied at construction. Streams are owned after successful construction.</param>
    public DapConnection(Stream input, Stream output, DapConnectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (!input.CanRead || !output.CanWrite) throw new ArgumentException("DAP requires readable input and writable output.");
        _options = options ?? new();
        _options.Validate();
        (_input, _output) = (input, output);
        _framing = new(input, _options);
        _events = Channel.CreateBounded<DapEvent>(new BoundedChannelOptions(_options.EventCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
        _reader = Task.Run(ReadLoopAsync);
    }

    /// <summary>Ordered event stream; use one consumer. Completion carries the connection's failure, if any.</summary>
    public ChannelReader<DapEvent> Events => _events.Reader;

    /// <summary>Completes when input handling ends: null for local disposal, otherwise the cause of closure.</summary>
    public Task<Exception?> Completion => _reader;

    /// <summary>
    /// Sends one request and returns its successful response, preserving all JSON fields. An unsuccessful response
    /// raises DapRequestException. Timeout/cancellation abandons the local wait; it does not undo execution remotely.
    /// Cancellation during a write closes the connection because a partial frame cannot be reused safely.
    /// </summary>
    public async Task<DapResponse> RequestAsync(string command, JsonElement? arguments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (arguments is { ValueKind: not JsonValueKind.Object })
            throw new ArgumentException("DAP arguments must be a JSON object or omitted.", nameof(arguments));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        deadline.CancelAfter(_options.RequestTimeout);
        var token = deadline.Token;
        var reply = new TaskCompletionSource<DapResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sequence = 0;
        // Cancellation completes the same task as a response or connection failure; none is left unobserved.
        using var registration = token.Register(() => reply.TrySetCanceled(token));
        try
        {
            await _writer.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    ThrowIfClosed();
                    if (_pending.Count >= _options.MaximumPendingRequests)
                        throw new InvalidOperationException("Too many pending DAP requests.");
                    sequence = checked(++_sequence);
                    _pending.Add(sequence, new(command, reply));
                }
                var body = DapFraming.Serialize(writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("seq", sequence);
                    writer.WriteString("type", "request");
                    writer.WriteString("command", command);
                    if (arguments is { } args) { writer.WritePropertyName("arguments"); args.WriteTo(writer); }
                    writer.WriteEndObject();
                });
                if (body.Length > _options.MaximumMessageBytes)
                    throw new ArgumentException("DAP request exceeds the configured message limit.", nameof(arguments));
                await WriteAsync(body, token).ConfigureAwait(false);
            }
            finally { _writer.Release(); }
            var response = await reply.Task.ConfigureAwait(false);
            if (!response.Success) throw new DapRequestException(response);
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
                if (_closed) throw new IOException("DAP connection is closed.", _failure);
            throw new TimeoutException($"Debug adapter did not complete '{command}' within {_options.RequestTimeout}.");
        }
        finally
        {
            lock (_sync) _pending.Remove(sequence);
            // A send can fail before awaiting the reply while closure faults it concurrently.
            if (reply.Task.IsFaulted) _ = reply.Task.Exception;
        }
    }

    private void ThrowIfClosed()
    {
        if (_closed) throw new IOException("DAP connection is closed.", _failure);
    }

    private async Task WriteAsync(byte[] body, CancellationToken token)
    {
        try { await DapFraming.WriteAsync(_output, body, token).ConfigureAwait(false); }
        catch (Exception ex)
        {
            Close(new IOException("Writing a DAP frame failed; the connection cannot be reused.", ex));
            throw;
        }
    }

    private async Task<Exception?> ReadLoopAsync()
    {
        try
        {
            while (true)
            {
                var message = await _framing.ReadAsync(_lifetime.Token).ConfigureAwait(false);
                var sequence = Number(message, "seq");
                switch (Text(message, "type"))
                {
                    case "response":
                        var request = Number(message, "request_seq");
                        var command = Text(message, "command");
                        if (!message.TryGetProperty("success", out var success) ||
                            success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                            throw new InvalidDataException("DAP response has no boolean success field.");
                        Pending? pending;
                        lock (_sync) _pending.TryGetValue(request, out pending);
                        // A late reply to a locally cancelled request is harmless and cannot complete another request.
                        if (pending is null) break;
                        if (pending.Command != command)
                            throw new InvalidDataException("DAP response command does not match its request.");
                        pending.Reply.TrySetResult(new(sequence, request, command, success.GetBoolean(), message));
                        break;
                    case "event":
                        var name = Text(message, "event");
                        if (!_events.Writer.TryWrite(new(sequence, name, message)))
                            throw new IOException("DAP event queue overflowed; the consumer is not keeping up.");
                        break;
                    case "request":
                        await RejectReverseRequestAsync(sequence, Text(message, "command")).ConfigureAwait(false);
                        break;
                    default:
                        throw new InvalidDataException("Unknown DAP message type.");
                }
            }
        }
        catch (Exception ex) { Close(ex); }
        lock (_sync) return _failure;
    }

    private async Task RejectReverseRequestAsync(int request, string command)
    {
        // Do not advertise reverse-request capabilities (e.g. runInTerminal) until a host implements them.
        // An unexpected request still gets a response, so the adapter is not left waiting indefinitely.
        await _writer.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        try
        {
            int sequence;
            lock (_sync) { ThrowIfClosed(); sequence = checked(++_sequence); }
            var body = DapFraming.Serialize(writer =>
            {
                writer.WriteStartObject();
                writer.WriteNumber("seq", sequence);
                writer.WriteString("type", "response");
                writer.WriteNumber("request_seq", request);
                writer.WriteString("command", command);
                writer.WriteBoolean("success", false);
                writer.WriteString("message", "HexIDE does not support adapter-initiated requests on this connection.");
                writer.WriteEndObject();
            });
            if (body.Length > _options.MaximumMessageBytes)
                throw new InvalidDataException("DAP reverse-request response exceeds the configured message limit.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            deadline.CancelAfter(_options.RequestTimeout);
            await WriteAsync(body, deadline.Token).ConfigureAwait(false);
        }
        finally { _writer.Release(); }
    }

    private static string Text(JsonElement message, string field) =>
        message.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 } text ? text
            : throw new InvalidDataException($"DAP message has no nonempty '{field}' string.");

    private static int Number(JsonElement message, string field) =>
        message.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) && number >= 0 ? number
            : throw new InvalidDataException($"DAP message has no valid '{field}' integer.");

    private void Close(Exception? failure)
    {
        lock (_sync)
        {
            if (_closed) return;
            _closed = true;
            _failure = failure;
            foreach (var pending in _pending.Values)
                pending.Reply.TrySetException(new IOException("DAP connection closed before its response arrived.", failure));
            _pending.Clear();
            _events.Writer.TryComplete(failure);
        }
        _lifetime.Cancel();
        // Disposal also unblocks streams whose cancellation support is limited (e.g. process pipes).
        try { _input.Dispose(); } catch (IOException) { }
        if (!ReferenceEquals(_input, _output))
            try { _output.Dispose(); } catch (IOException) { }
    }

    public async ValueTask DisposeAsync()
    {
        Close(null);
        await _reader.ConfigureAwait(false);
    }
}

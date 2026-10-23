using System.Collections.Immutable;
using System.Net.WebSockets;

namespace HexIDE.TwinBasic;

public sealed record CompilerProjectPreparationLimits(int MaximumProjectBytes = 64 * 1024 * 1024,
    int MaximumMessageBytes = 16 * 1024 * 1024, int MaximumReceivedBytes = 32 * 1024 * 1024,
    int MaximumPackageBytes = 8 * 1024 * 1024, int MaximumTotalPackageBytes = 32 * 1024 * 1024,
    int MaximumNotifications = 128, TimeSpan? Timeout = null);

public sealed class CompilerProjectStatusException(int statusCode) : IOException(statusCode switch
{
    -4 => "Compiler project requires a different DEP configuration.",
    -5 => "Compiler project requires a different LAA configuration.",
    -6 => "Compiler project requires a different DEP and LAA configuration.",
    _ => $"Compiler project preparation failed with status {statusCode}."
})
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>A retained staging folder. Stop its compiler owner before retrying cleanup.</summary>
public sealed class CompilerProjectCleanupException : IOException
{
    public string StagingDirectory { get; }
    internal CompilerProjectCleanupException(string directory, Exception cause)
        : base("The compiler staging folder could not be removed; stop its compiler owner and retry cleanup.", cause)
        => StagingDirectory = directory;

    public void RetryCleanup() => CompilerProjectPreparation.DeleteStage(StagingDirectory);
}

/// <summary>
/// Owns the staged copy and root, but not the compiler process. Stop attached channels and the
/// externally owned compiler before disposal. If cleanup fails, disposal can be retried.
/// </summary>
public sealed class CompilerPreparedProject : IDisposable
{
    internal CompilerRootSession Root { get; }
    private readonly string directory;
    private readonly object cleanupGate = new();
    private int disposed;
    public string StagedProjectPath { get; }
    public CompilerRootConnectionInfo Connection => Root.Connection;
    public ImmutableArray<byte> LoadReply { get; }
    public ImmutableArray<byte> ReadyReply { get; }
    public ImmutableArray<CompilerControlMessage> Notifications { get; }

    internal CompilerPreparedProject(CompilerRootSession root, string directory, string path,
        ImmutableArray<byte> loadReply, ImmutableArray<byte> readyReply, ImmutableArray<CompilerControlMessage> notifications)
    {
        Root = root;
        this.directory = directory;
        StagedProjectPath = path;
        LoadReply = loadReply;
        ReadyReply = readyReply;
        Notifications = notifications;
    }

    public void Dispose()
    {
        lock (cleanupGate)
        {
            if (disposed != 0) return;
            Root.Dispose();
            try { CompilerProjectPreparation.DeleteStage(directory); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { throw new CompilerProjectCleanupException(directory, error); }
            disposed = 1;
        }
    }
}

/// <summary>
/// Copies a project and prepares it using the observed load/settings/packages/readiness exchange.
/// Takes exclusive ownership of the root on entry; callers must stop using that root.
/// Neither saves the original nor loads add-ins. Relative external dependencies are not copied.
/// Package resolution is explicit, and unknown notifications are retained without executing actions.
/// </summary>
public static class CompilerProjectPreparation
{
    public static async Task<CompilerPreparedProject> PrepareAsync(CompilerRootSession root, string projectPath,
        ICompilerPackageProvider packages, CompilerProjectPreparationLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!root.TryClaimProjectPreparation()) throw new InvalidOperationException("The compiler root already has a project owner.");
        string? directory = null;
        try
        {
            ArgumentNullException.ThrowIfNull(packages);
            limits ??= new();
            var timeout = limits.Timeout ?? TimeSpan.FromSeconds(60);
            if (limits.MaximumProjectBytes < 1 || limits.MaximumMessageBytes < 32 || limits.MaximumReceivedBytes < 8 ||
                limits.MaximumPackageBytes < 1 || limits.MaximumTotalPackageBytes < 1 || limits.MaximumNotifications < 0 ||
                timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
                throw new ArgumentOutOfRangeException(nameof(limits));
            if (!Path.IsPathFullyQualified(projectPath) || !string.Equals(Path.GetExtension(projectPath), ".twinproj", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("An absolute project-container path is required.", nameof(projectPath));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                directory = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "hexide-compiler-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var stagedPath = Path.Combine(directory, Path.GetFileName(projectPath));
                await CopyStageAsync(projectPath, stagedPath, limits.MaximumProjectBytes, deadline.Token).ConfigureAwait(false);
                return await PrepareCoreAsync(root, directory, stagedPath, packages, limits, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                throw new TimeoutException("Compiler project preparation timed out.");
            }
        }
        catch (Exception error)
        {
            root.Dispose();
            if (directory is not null)
            {
                try { DeleteStage(directory); }
                catch (Exception cleanup) { throw new CompilerProjectCleanupException(directory,
                    new AggregateException("Project preparation and staging cleanup failed.", error, cleanup)); }
            }
            throw;
        }
    }

    private static async Task<CompilerPreparedProject> PrepareCoreAsync(CompilerRootSession root, string directory,
        string stagedPath, ICompilerPackageProvider packages, CompilerProjectPreparationLimits limits, CancellationToken token)
    {
        var codec = root.Codec;
        var pending = new HashSet<int>();
        var notifications = ImmutableArray.CreateBuilder<CompilerControlMessage>();
        var nextId = 2; // Root initialization consumed ids 0 and 1.
        long received = 0, packageBytes = 0;

        async Task<int> Send(CompilerProjectRequest request)
        {
            var id = nextId++;
            pending.Add(id);
            await root.Transport.SendAsync(codec.EncodeProjectRequest(id, request, limits.MaximumMessageBytes), WebSocketMessageType.Binary, token).ConfigureAwait(false);
            return id;
        }

        async Task<ImmutableArray<byte>> Complete(int awaitedId)
        {
            ImmutableArray<byte> result = default;
            while (pending.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                var message = await root.Transport.ReceiveAsync(token).ConfigureAwait(false);
                received += message.Bytes.Length;
                if (received > limits.MaximumReceivedBytes) throw Invalid("Compiler project preparation exceeds the receive budget.");
                if (message.Type != WebSocketMessageType.Binary) throw Invalid("Compiler project replies must be binary.");
                var reply = codec.DecodeControl(message.Bytes, limits.MaximumMessageBytes);
                if (!reply.IsNotification)
                {
                    if (!pending.Remove(reply.RequestId)) throw Invalid("Unexpected compiler project reply id.");
                    if (reply.Code != 0) throw new CompilerProjectStatusException(reply.Code);
                    if (reply.RequestId == awaitedId) result = reply.Payload;
                    continue;
                }
                if (notifications.Count == limits.MaximumNotifications) throw Invalid("Compiler project preparation exceeds the notification budget.");
                notifications.Add(reply);
                if (reply.Code == 243)
                {
                    _ = codec.ReadSettingsRequest(reply);
                    // Match the stock additional-compiler path: retain original settings unchanged.
                    await Send(new CompilerKeepSettings()).ConfigureAwait(false);
                }
                else if (reply.Code == 255)
                {
                    var request = codec.ReadPackageRequest(reply);
                    var allowance = Math.Min(limits.MaximumPackageBytes,
                        Math.Min(limits.MaximumTotalPackageBytes - packageBytes, codec.MaximumPackageContentBytes(request, limits.MaximumMessageBytes)));
                    if (allowance < 1) throw Invalid("Compiler package budget is exhausted.");
                    using var stream = await OpenPackageAsync(packages, request.Identifier, token).ConfigureAwait(false);
                    if (stream is null) throw Invalid("The requested compiler package is not available from the configured provider.");
                    using var content = new MemoryStream();
                    var buffer = new byte[8192];
                    while (true)
                    {
                        int count;
                        try { count = await stream.ReadAsync(buffer, token).AsTask().WaitAsync(token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch { throw new IOException("Compiler package stream failed."); }
                        if (count == 0) break;
                        if (content.Length + count > allowance) throw Invalid("Compiler package exceeds the byte budget.");
                        content.Write(buffer, 0, count);
                    }
                    if (content.Length == 0) throw Invalid("The configured compiler package is empty.");
                    packageBytes += content.Length;
                    await Send(new CompilerProvidePackage(request, content.GetBuffer().AsMemory(0, (int)content.Length))).ConfigureAwait(false);
                }
            }
            if (result.IsDefault) throw Invalid("Missing compiler project reply.");
            return result;
        }

        var load = await Complete(await Send(new CompilerLoadProject(stagedPath)).ConfigureAwait(false)).ConfigureAwait(false);
        var ready = await Complete(await Send(new CompilerProjectsLoaded()).ConfigureAwait(false)).ConfigureAwait(false);
        return new(root, directory, stagedPath, load, ready, notifications.ToImmutable());
    }

    private static async Task<Stream?> OpenPackageAsync(ICompilerPackageProvider provider, string id, CancellationToken token)
    {
        Task<Stream?> pending;
        try { pending = provider.OpenReadAsync(id, token).AsTask(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { throw new IOException("Compiler package provider failed."); }
        try { return await pending.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _ = DisposeLateAsync(pending);
            throw;
        }
        catch { throw new IOException("Compiler package provider failed."); }
    }

    private static async Task DisposeLateAsync(Task<Stream?> pending)
    {
        try { (await pending.ConfigureAwait(false))?.Dispose(); }
        catch { /* A late provider failure cannot replace the already reported cancellation. */ }
    }

    private static async Task CopyStageAsync(string source, string target, int maximumBytes, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (input.Length > maximumBytes) throw Invalid("Compiler project exceeds the staging byte limit.");
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous);
        var buffer = new byte[8192];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > maximumBytes) throw Invalid("Compiler project exceeds the staging byte limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
    }

    internal static void DeleteStage(string directory)
    {
        // Only delete this operation's GUID-named direct child of the system temp directory.
        var full = Path.GetFullPath(directory);
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var name = Path.GetFileName(full);
        if (!string.Equals(Path.GetDirectoryName(full), parent, comparison) || !name.StartsWith("hexide-compiler-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[16..], "N", out _))
            throw new IOException("Refusing cleanup outside the owned compiler staging folder.");
        if (!Directory.Exists(full)) return;
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The owned compiler staging folder was replaced by a link.");
        Directory.Delete(full, recursive: true);
    }

    private static InvalidDataException Invalid(string message) => new(message);
}

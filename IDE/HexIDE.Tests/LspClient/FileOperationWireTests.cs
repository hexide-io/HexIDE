using System.Globalization;
using System.Text;
using System.Text.Json;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

public class FileOperationWireTests : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(30));
    private readonly List<JsonElement> _frames = [];
    private VBLspClient? _client;

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        await _cts.CancelAsync();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<VBLspClient> ConnectAsync(string operations, bool start = true)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        _ = Task.Run(async () =>
        {
            try
            {
                while (await ReadFrameAsync(serverSide, _cts.Token) is { } json)
                {
                    using var doc = JsonDocument.Parse(json);
                    var frame = doc.RootElement.Clone();
                    lock (_frames) _frames.Add(frame);
                    if (!frame.TryGetProperty("id", out var id)) continue;
                    var result = frame.GetProperty("method").GetString() == "initialize"
                        ? "{\"capabilities\":{\"workspace\":{\"fileOperations\":" + operations + "}}}"
                        : "null";
                    await WriteFrameAsync(serverSide,
                        "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":" + result + "}", _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }, _cts.Token);
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));
        _client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6");
        if (start) await _client.StartAsync();
        return _client;
    }

    [Fact]
    public async Task FailedModuleWriteSendsNoCreationFrame()
    {
        var client = await ConnectAsync("{\"didCreate\":{\"filters\":[{\"pattern\":{\"glob\":\"**/*\"}}]}}");
        var directory = Path.Combine(Path.GetTempPath(), "hexide-failed-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule)
            {
                AbsolutePath = directory // Replacing a directory fails on both Windows and Linux.
            };
            project.AddModule(module);
            var service = new ProjectService(
                () => throw new InvalidOperationException("No dialog expected"),
                Substitute.For<IWindowManager>(), Substitute.For<IEventBus>(),
                Substitute.For<IProjectManager>(), Substitute.For<IRecentProjectsService>(),
                Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(),
                new FileBaselineStore(), Substitute.For<HexIDE.Localization.ILocalizationService>(),
                Substitute.For<IHeaderRefresher>(), lspClient: () => client);

            var save = () => service.SaveModule(module, false);
            (await save.Should().ThrowAsync<Exception>()).Which.Should()
                .Match<Exception>(ex => ex is IOException || ex is UnauthorizedAccessException);

            await StopAtBarrierAsync(client);
            lock (_frames) _frames.Should().NotContain(f =>
                f.GetProperty("method").GetString() == "workspace/didCreateFiles");
            File.Exists(directory + ".tmp").Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAdvertisesFileOperationNotifications()
    {
        var client = await ConnectAsync("{}");
        await StopAtBarrierAsync(client);
        var initialize = _frames.Single(f => f.GetProperty("method").GetString() == "initialize");
        var operations = initialize.GetProperty("params").GetProperty("capabilities")
            .GetProperty("workspace").GetProperty("fileOperations");
        foreach (var operation in new[] { "didCreate", "didRename", "didDelete" })
            operations.GetProperty(operation).GetBoolean().Should().BeTrue();
        operations.TryGetProperty("willCreate", out _).Should().BeFalse();
    }

    private async Task StopAtBarrierAsync(VBLspClient client)
    {
        await client.StopAsync();
        lock (_frames) _frames.Should().Contain(f => f.GetProperty("method").GetString() == "shutdown",
            "the peer must read the barrier before absence of earlier notifications proves silence");
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Rename")]
    [InlineData("Delete")]
    public async Task SendsOnlyMatchingFiles(string operation)
    {
        var client = await ConnectAsync("{\"did" + operation + "\":{\"filters\":[{\"scheme\":\"file\",\"pattern\":{\"glob\":\"**/*.{bas,cls}\",\"matches\":\"file\",\"options\":{\"ignoreCase\":true}}}]}}");
        const string oldUri = "file:///project/Old.BAS";
        const string newUri = "file:///project/New.cls";
        var ct = TestContext.Current.CancellationToken;
        switch (operation)
        {
            case "Create": await client.DidCreateFilesAsync([newUri, "file:///project/Form.frm"], ct); break;
            case "Rename": await client.DidRenameFilesAsync([new(oldUri, newUri), new("file:///a.txt", "file:///b.txt")], ct); break;
            case "Delete": await client.DidDeleteFilesAsync([oldUri, "file:///project/Form.frm"], ct); break;
        }
        // shutdown's response proves the peer read every preceding notification, including silence.
        await StopAtBarrierAsync(client);
        JsonElement[] sent;
        lock (_frames) sent = _frames.Where(f => f.GetProperty("method").GetString() == "workspace/did" + operation + "Files").ToArray();
        sent.Should().ContainSingle();
        sent[0].TryGetProperty("id", out _).Should().BeFalse();
        var files = sent[0].GetProperty("params").GetProperty("files");
        files.GetArrayLength().Should().Be(1);
        if (operation == "Rename")
        {
            files[0].GetProperty("oldUri").GetString().Should().Be(oldUri);
            files[0].GetProperty("newUri").GetString().Should().Be(newUri);
        }
        else files[0].GetProperty("uri").GetString().Should().Be(operation == "Create" ? newUri : oldUri);
    }

    [Theory]
    [InlineData("Create", true)]
    [InlineData("Rename", true)]
    [InlineData("Delete", true)]
    [InlineData("Create", false)]
    [InlineData("Rename", false)]
    [InlineData("Delete", false)]
    public async Task RegistryFileOperationsHaveOneWorkspaceWriter(string operation, bool generatesArtifact)
    {
        var client = await ConnectAsync("{\"did" + operation + "\":{\"filters\":[{\"pattern\":{\"glob\":\"**/*.bas\"}}]}}", start: false);
        var registration = new LanguageServerRegistration("probe", "Probe", [".bas"], "vb6", () => client,
            WorkspaceArtifact: generatesArtifact ? new("probe.rdproj", "rdcore") : null);
        await using var registry = new LspClientRegistry([registration], Substitute.For<ILogger<LspClientRegistry>>());
        var ct = TestContext.Current.CancellationToken;
        await registry.OpenDocumentAsync("file:///project/Old.bas", "", ct);
        switch (operation)
        {
            case "Create": await registry.DidCreateFilesAsync(["file:///project/New.bas"], ct); break;
            case "Rename": await registry.DidRenameFilesAsync([new("file:///project/Old.bas", "file:///project/New.bas")], ct); break;
            case "Delete": await registry.DidDeleteFilesAsync(["file:///project/Old.bas"], ct); break;
        }
        await StopAtBarrierAsync(client);
        JsonElement[] sent;
        lock (_frames) sent = _frames.Where(f => f.GetProperty("method").GetString() == "workspace/did" + operation + "Files").ToArray();
        sent.Should().HaveCount(generatesArtifact ? 0 : 1);
        if (!generatesArtifact)
        {
            var files = sent[0].GetProperty("params").GetProperty("files");
            files.GetArrayLength().Should().Be(1);
            if (operation == "Rename")
            {
                files[0].GetProperty("oldUri").GetString().Should().Be("file:///project/Old.bas");
                files[0].GetProperty("newUri").GetString().Should().Be("file:///project/New.bas");
            }
            else files[0].GetProperty("uri").GetString().Should().Be(
                operation == "Create" ? "file:///project/New.bas" : "file:///project/Old.bas");
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"didCreate\":{\"filters\":[{\"scheme\":\"untitled\",\"pattern\":{\"glob\":\"**/*\"}}]}}")]
    [InlineData("{\"didCreate\":{\"filters\":[{\"pattern\":{\"glob\":\"**/*\",\"matches\":\"folder\"}}]}}")]
    public async Task UnrequestedFilesSendNothing(string operations)
    {
        var client = await ConnectAsync(operations);
        await client.DidCreateFilesAsync(["file:///project/Test.bas"], TestContext.Current.CancellationToken);
        await client.DidRenameFilesAsync([new("file:///project/A.bas", "file:///project/B.bas")], TestContext.Current.CancellationToken);
        await client.DidDeleteFilesAsync(["file:///project/Test.bas"], TestContext.Current.CancellationToken);
        await StopAtBarrierAsync(client);
        lock (_frames) _frames.Should().NotContain(f => f.GetProperty("method").GetString()!.StartsWith("workspace/did", StringComparison.Ordinal));
    }

    private static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new List<byte>(64);
        var one = new byte[1];

        while (true)
        {
            if (await stream.ReadAsync(one.AsMemory(0, 1), ct) == 0) return null;
            header.Add(one[0]);
            if (header.Count >= 4
                && header[^4] == (byte)'\r' && header[^3] == (byte)'\n'
                && header[^2] == (byte)'\r' && header[^1] == (byte)'\n')
            {
                break;
            }
        }

        var length = 0;
        foreach (var line in Encoding.ASCII.GetString([.. header])
                     .Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
            }
        }

        if (length <= 0) return null;

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(read, length - read), ct);
            if (n == 0) return null;
            read += n;
        }

        return Encoding.UTF8.GetString(body);
    }

    private static async Task WriteFrameAsync(Stream stream, string json, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes(
            $"Content-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}

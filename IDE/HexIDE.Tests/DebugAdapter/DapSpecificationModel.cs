using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace HexIDE.Tests.DebugAdapter;

internal static class DapSpecificationModel
{
    internal const string Commit = "d2387e875a1ec4c2ed9964edea22617c81651cc0";
    internal const string Sha256 = "5dc16664b5cd7a08a769c14dc19be900dc4c3e7d227cd922583780d25480877a";
    internal const string PathVariable = "HEXIDE_DAP_SCHEMA";
    internal const string Url = "https://raw.githubusercontent.com/microsoft/debug-adapter-protocol/" + Commit + "/debugAdapterProtocol.json";
    internal sealed record Message(string Name, string Kind, string Direction);
    private static readonly Lazy<IReadOnlyList<Message>?> Cached = new(Load);
    internal static string? Failure { get; private set; }
    internal static IReadOnlyList<Message>? All() => Cached.Value;

    internal static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IReadOnlyList<Message>? Load()
    {
        try
        {
            var configured = Environment.GetEnvironmentVariable(PathVariable);
            var path = configured ?? Path.Combine(Root(), "artifacts", "dap-schema", Commit, "debugAdapterProtocol.json");
            if (!File.Exists(path))
            {
                if (configured is not null || Environment.GetEnvironmentVariable("HEXIDE_FOREIGN_LSP_DOWNLOAD") is "0" or "false")
                    return null;
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var bytes = http.GetByteArrayAsync(Url).GetAwaiter().GetResult();
                Verify(bytes);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    File.Move(temporary, path, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            var content = File.ReadAllBytes(path);
            Verify(content);
            return Parse(content);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
        catch (Exception exception)
        {
            Failure = exception.Message;
            return null;
        }
    }

    internal static void Verify(byte[] bytes)
    {
        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!actual.Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"DAP schema checksum mismatch: expected {Sha256}, got {actual}.");
    }

    internal static IReadOnlyList<Message> Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var messages = new List<Message>();
        foreach (var definition in document.RootElement.GetProperty("definitions").EnumerateObject())
        {
            if (!definition.Value.TryGetProperty("allOf", out var parts)) continue;
            var entries = parts.EnumerateArray().ToArray();
            var request = entries.Any(p => p.TryGetProperty("$ref", out var reference) && reference.GetString() == "#/definitions/Request");
            var notification = entries.Any(p => p.TryGetProperty("$ref", out var reference) && reference.GetString() == "#/definitions/Event");
            if (!request && !notification) continue;
            var shape = entries.Single(p => p.TryGetProperty("properties", out _));
            var name = shape.GetProperty("properties").GetProperty(request ? "command" : "event").GetProperty("enum")[0].GetString()!;
            // DAP encodes reverse direction in prose, not a messageDirection field like LSP.
            var reverse = request && shape.GetProperty("description").GetString()!
                .Contains("sent from the debug adapter to the client", StringComparison.Ordinal);
            messages.Add(new(name, notification ? "event" : reverse ? "reverse request" : "request", notification || reverse ? "←" : "→"));
        }
        return messages.OrderBy(m => m.Kind, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal).ToArray();
    }
}

public sealed class DapSchemaFactAttribute : FactAttribute
{
    public DapSchemaFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (DapSpecificationModel.All() is null)
            Skip = $"DAP schema unavailable; set {DapSpecificationModel.PathVariable} to the pinned schema for offline use. {DapSpecificationModel.Failure}";
    }
}

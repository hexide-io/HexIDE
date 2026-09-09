using System;
using System.IO;
using System.Threading.Tasks;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

/// <summary>
/// Proves that <c>textDocument/declaration</c> and <c>textDocument/definition</c> are two questions, by
/// getting two different answers to them from a real server.
///
/// <para>
/// The client has been able to ask both since the method was wired, and <c>docs/lsp-client.md</c> has said
/// a server "may answer differently" — but nothing demonstrated it. Every server previously in this suite
/// either implements one of the pair or answers both identically, so a client that quietly aliased
/// declaration onto definition would have passed everything. That is precisely the failure this fixture
/// exists to catch: a client and a server by one hand agree with each other rather than with the
/// specification.
/// </para>
///
/// <para>
/// C++ is why clangd earns its place here rather than being a fourth server for its own sake. The language
/// separates a declaration from a definition as a matter of course, so the two requests land on different
/// lines of the same file and the difference is assertable rather than assumed.
/// </para>
/// </summary>
public class DeclarationIsNotDefinitionTests : IAsyncDisposable
{
    // Five lines, no #include, and deliberately so: clangd finds no compile_commands.json, says so at info
    // level, and proceeds with a fallback command. Measured on Windows and Linux — a self-contained file
    // needs no compilation database, no file on disk, and no system toolchain, since clangd embeds its own
    // clang. Anything with an include would need more, which is why this has none.
    private const string CppSource =
        "int twice(int n);\n" +
        "\n" +
        "int twice(int n) { return n * 2; }\n" +
        "\n" +
        "int main() { return twice(21); }\n";

    // Inside the `twice` call on the last line. The identifier spans characters 20-24.
    private static readonly Position CallSite = new(4, 21);

    private LspClientRegistry? _registry;

    /// <summary>
    /// A URI built from a platform-native path, which matters more here than elsewhere.
    /// </summary>
    /// <remarks>
    /// clangd applies a drive-letter heuristic when normalising: the <c>file:///c:/proj/...</c> shape the
    /// Markdown tests use comes back on Linux as <c>file:///&lt;cwd&gt;/c:/proj/...</c>, and a POSIX-shaped
    /// path comes back drive-prefixed on Windows. Neither is wrong of the server; both would make an
    /// equality assertion fail for a reason unrelated to what is being tested.
    /// </remarks>
    private static string DocumentUri() =>
        new Uri(Path.Combine(Path.GetTempPath(), "hexide-clangd", "twice.cpp")).AbsoluteUri;

    private LspClientRegistry CppRegistry()
    {
        var serverInfo = new LspServerInfo(
            ForeignServer.Cpp.Find()!, ForeignServer.Cpp.ServerArguments, Path.GetTempPath());

        var loggerFactory = LoggerFactory.Create(b => { });
        var registration = new LanguageServerRegistration(
            Id: "foreign.cpp",
            DisplayName: "Foreign C/C++ server",
            Extensions: ForeignServer.Cpp.Extensions,
            LanguageId: ForeignServer.Cpp.LanguageId,
            CreateClient: () => new VBLspClient(
                new StdioProcessLspTransport(serverInfo, loggerFactory.CreateLogger<StdioProcessLspTransport>()),
                loggerFactory.CreateLogger<VBLspClient>(),
                ForeignServer.Cpp.LanguageId));

        _registry = new LspClientRegistry([registration], loggerFactory.CreateLogger<LspClientRegistry>());
        return _registry;
    }

    [ForeignServerFact("cpp")]
    public async Task AServerAnswersDeclarationAndDefinitionDifferently()
    {
        var sut = CppRegistry();
        var uri = DocumentUri();
        await sut.OpenDocumentAsync(uri, CppSource);

        var declaration = await sut.RequestDeclarationAsync(uri, CallSite);
        var definition = await sut.RequestDefinitionAsync(uri, CallSite);

        declaration.Should().NotBeNullOrEmpty("clangd advertises declarationProvider, so the gate must open");
        definition.Should().NotBeNullOrEmpty();

        // Asserted on the RANGES, not the URIs. Both answers are in the one document, so the URI carries
        // nothing here, and comparing it would only re-test the normalisation trap described above.
        declaration![0].Range.Start.Line.Should().Be(0, "the declaration is the signature on line 1");
        definition![0].Range.Start.Line.Should().Be(2, "the definition is the body on line 3");

        declaration[0].Range.Start.Line.Should().NotBe(definition[0].Range.Start.Line,
            "the whole reason this method exists is that the two can differ — if a change ever made the "
            + "client send definition for both, every other assertion here would still pass");
    }

    [ForeignServerFact("cpp")]
    public async Task TheDeclarationGateStaysShutForAServerThatDoesNotOfferIt()
    {
        // The control case, and it is not hypothetical: several real servers advertise definition and
        // explicitly not declaration (deno's source reads `declaration_provider: None`). A client that
        // fell back to definitionProvider would send requests such a server never claimed to answer, and
        // the gate would look like it worked because SOMETHING came back.
        var sut = CppRegistry();
        var uri = DocumentUri();
        await sut.OpenDocumentAsync(uri, CppSource);

        // Routing is by document, so a URI this registry claims nothing for has no client to ask.
        var unclaimed = await sut.RequestDeclarationAsync("file:///c:/proj/notes/README.md", CallSite);

        unclaimed.Should().BeNull("no registered server claims Markdown, so nothing should be asked");
    }

    public async ValueTask DisposeAsync()
    {
        if (_registry is not null)
        {
            try { await _registry.StopAsync(); } catch { /* best effort, as the sibling suites do */ }
        }
        GC.SuppressFinalize(this);
    }
}

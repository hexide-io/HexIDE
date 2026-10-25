// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Makes <c>HEXIDE_REQUIRE_FOREIGN_LSP</c> mean something that discovery cannot swallow.
///
/// <para>
/// That variable exists so a skipped proof cannot quietly stop happening: CI sets it, and absence of a
/// foreign server is then a failure rather than a skip. The enforcement used to live in the
/// <c>[ForeignServerFact]</c> and <c>[LspModelFact]</c> constructors, which threw when a server was
/// missing and the variable was set.
/// </para>
///
/// <para>
/// <b>Under xunit v3 that silently stopped working.</b> v3 discards a test whose attribute constructor
/// throws, where v2 reported it as a failure — so the same configuration produced a green run with the
/// tests simply not present. Measured during the v3 migration: 978 passed, 0 failed, 0 skipped, against a
/// normal 992. Fourteen tests gone, nothing red, which is exactly the fail-open the variable was written
/// to prevent, arriving through the mechanism meant to enforce it.
/// </para>
///
/// <para>
/// Ordinary <c>[Fact]</c>s, deliberately. They are discovered whatever the environment, so when CI cannot
/// obtain a server the run goes red here with a message naming which one — rather than going green with
/// less coverage than anyone thinks. Checksum mismatches fail here even outside CI (#487).
/// </para>
/// </summary>
public class RequiredServersTests
{
    private static void RequireOrIgnore(string name, Func<bool> available, string what)
    {
        if (!ForeignServer.IsRequired) return;

        available().Should().BeTrue(
            $"{ForeignServer.RequiredVariable} is set, so {what} must be obtainable. The '{name}' fixture "
          + "is the only check of its kind here, and a run that cannot get it must go red rather than "
          + "quietly cover less.");
    }

    internal static void CheckServer(ForeignServerSource source, Func<string?> find, bool required)
    {
        var path = find();
        source.ChecksumMismatch.Should().BeNull("{0}", source.ChecksumMismatch);
        if (!required) return;

        path.Should().NotBeNull(
            $"{ForeignServer.RequiredVariable} is set, so the '{source.Key}' language server must be obtainable");
    }

    [Fact]
    public void TheMarkdownServerIsAvailableWhenItIsRequired() =>
        CheckServer(ForeignServer.Markdown.Source, ForeignServer.Markdown.Find, ForeignServer.IsRequired);

    [Fact]
    public void TheLatexServerIsAvailableWhenItIsRequired() =>
        CheckServer(ForeignServer.Latex.Source, ForeignServer.Latex.Find, ForeignServer.IsRequired);

    [Fact]
    public void TheCppServerIsAvailableWhenItIsRequired() =>
        CheckServer(ForeignServer.Cpp.Source, ForeignServer.Cpp.Find, ForeignServer.IsRequired);

    [Fact]
    public void ThePythonServerIsAvailableWhenItIsRequired() =>
        CheckServer(ForeignServer.Python.Source, ForeignServer.Python.Find, ForeignServer.IsRequired);

    [Fact]
    public void TheReferenceServerIsAvailableWhenItIsRequired() =>
        CheckServer(ForeignServer.Json.Source, ForeignServer.Json.Find, ForeignServer.IsRequired);

    [Fact]
    public void TheProtocolModelIsAvailableWhenItIsRequired() =>
        RequireOrIgnore("metaModel", () => LspSpecificationModel.All() is not null,
            $"the LSP {LspSpecificationModel.Version} metaModel");

    /// <summary>
    /// The bundled server is not downloaded, it is built — but it fails open in exactly the same way.
    /// </summary>
    /// <remarks>
    /// Running the IDE’s tests does not build the server half, so a tree that has only ever built the IDE
    /// skips the one test where HexIDE’s own client and HexIDE’s own server actually meet. That is fine on a
    /// laptop and unacceptable in CI, where the two halves agreeing with each other is the whole point.
    /// </remarks>
    [Fact]
    public void TheBundledServerIsBuiltWhenForeignServersAreRequired() =>
        RequireOrIgnore("bundled",
            () => BundledServer.Find() is not null && BundledServer.StaleReason() is null,
            "the bundled VB6 language server (build it with: cd LspServer && dotnet build HexIDE.VbLspServer/)");
}

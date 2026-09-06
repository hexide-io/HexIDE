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
/// less coverage than anyone thinks.
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

    [Fact]
    public void TheMarkdownServerIsAvailableWhenItIsRequired() =>
        RequireOrIgnore("markdown", () => ForeignServer.Markdown.Find() is not null,
            "the Markdown language server");

    [Fact]
    public void TheLatexServerIsAvailableWhenItIsRequired() =>
        RequireOrIgnore("latex", () => ForeignServer.Latex.Find() is not null,
            "the LaTeX language server");

    [Fact]
    public void TheReferenceServerIsAvailableWhenItIsRequired() =>
        RequireOrIgnore("json", () => ForeignServer.Json.Find() is not null,
            "the reference JSON language server (which needs Node)");

    [Fact]
    public void TheProtocolModelIsAvailableWhenItIsRequired() =>
        RequireOrIgnore("metaModel", () => LspSpecificationModel.All() is not null,
            $"the LSP {LspSpecificationModel.Version} metaModel");
}

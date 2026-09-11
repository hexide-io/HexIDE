using System.Text.RegularExpressions;
using HexIDE.Lsp;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.Conversations;

/// <summary>
/// Keeps the list of capabilities this client consumes honest about the code that consumes them.
/// </summary>
/// <remarks>
/// <b>A hand-written list of what a program does is a document, and documents here rot unless something
/// checks them.</b> This one decides what a capture reports as "advertised and unused", which is the entry
/// a server author most wants — so a stale list does not merely go quiet, it tells them something false
/// about their own server.
///
/// <para>
/// The same shape as the protocol coverage guard: read the source, extract what is actually there, and
/// fail when the two disagree. Wiring a new method and forgetting the list breaks the build instead of
/// silently reporting that capability as unused for the rest of the project's life.
/// </para>
/// </remarks>
public partial class ConsumedCapabilitiesTests
{
    [GeneratedRegex("""CanServe(?:Experimental)?\("([A-Za-z.]+)"\)""")]
    private static partial Regex GateCall();

    private static string ClientSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the repository root should be findable from {0}", AppContext.BaseDirectory);
        var path = Path.Combine(dir!.FullName, "IDE", "HexIDE.Lsp", "VBLspClient.cs");
        File.Exists(path).Should().BeTrue("the client's source is what this guard reads");
        return File.ReadAllText(path);
    }

    [Fact]
    public void TheListMatchesEveryCapabilityTheClientActuallyGatesOn()
    {
        var gated = GateCall()
            .Matches(ClientSource())
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        gated.Should().NotBeEmpty(
            "finding nothing would mean the pattern stopped matching, and a guard that matches nothing "
          + "passes for the wrong reason");

        VBLspClient.ConsumedCapabilities.Order(StringComparer.Ordinal).Should().Equal(gated,
            "every capability the client asks for belongs in the list, and nothing else does — otherwise a "
          + "capture tells a server author their capability is unused when this client uses it, or the "
          + "reverse");
    }

    [Fact]
    public void TheListHasNoDuplicates()
    {
        VBLspClient.ConsumedCapabilities.Should().OnlyHaveUniqueItems();
    }
}

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// The tests that need symbolic links hold security checks: the packer refusing a linked key or output, and the project
/// writer refusing to write through a link. Under CI they must run on every platform, Windows included, so a runner that
/// can't make a link fails here with the reason, rather than leaving those checks to skip unseen.
/// </summary>
public class SymbolicLinkProbeTests
{
    [Fact]
    public void Symbolic_links_can_be_made_wherever_CI_runs()
    {
        if (!SymbolicLinks.Required)
            return;

        SymbolicLinks.CanCreate.Should().BeTrue(
            "CI runs the tests that need symbolic links on every platform, and this runner couldn't make one: on Windows that "
            + "needs Developer Mode or an elevated account");
    }
}

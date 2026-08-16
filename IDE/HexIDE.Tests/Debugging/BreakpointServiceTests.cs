using HexIDE.Debugging;

namespace HexIDE.Tests.Debugging;

/// <summary>
/// The IDE-side breakpoint store: 1-based line sets per document URI. Pure in-memory (persistence lives in
/// <c>UserSidecarService</c> — see <see cref="Sidecar.UserSidecarBreakpointTests"/>).
/// </summary>
public sealed class BreakpointServiceTests
{
    private const string Form1 = "vb6://form/Form1";
    private const string Mod1 = "vb6://module/Module1";

    [Fact]
    public void Toggle_TogglesLineOnAndOff()
    {
        var svc = new BreakpointService();

        svc.Toggle(Form1, 3);
        svc.IsBreakpoint(Form1, 3).Should().BeTrue();

        svc.Toggle(Form1, 3);
        svc.IsBreakpoint(Form1, 3).Should().BeFalse();
        svc.GetBreakpoints(Form1).Should().BeEmpty();
    }

    [Fact]
    public void SetDocument_DedupesSorts_AndEmptyClears()
    {
        var svc = new BreakpointService();

        svc.SetDocument(Form1, new[] { 5, 2, 5, 9 });
        svc.GetBreakpoints(Form1).Should().Equal(2, 5, 9);

        svc.SetDocument(Form1, System.Array.Empty<int>());
        svc.GetBreakpoints(Form1).Should().BeEmpty();
        svc.All().Should().NotContainKey(Form1);
    }

    [Fact]
    public void All_ReturnsEveryDocumentWithBreakpoints()
    {
        var svc = new BreakpointService();
        svc.Toggle(Form1, 1);
        svc.Toggle(Mod1, 7);

        var all = svc.All();
        all.Keys.Should().BeEquivalentTo(new[] { Form1, Mod1 });
        all[Mod1].Should().Equal(7);
    }

    [Fact]
    public void ClearDocument_RemovesOnlyThatDocument()
    {
        var svc = new BreakpointService();
        svc.Toggle(Form1, 1);
        svc.Toggle(Mod1, 2);

        svc.ClearDocument(Form1);

        svc.GetBreakpoints(Form1).Should().BeEmpty();
        svc.GetBreakpoints(Mod1).Should().Equal(2);
    }

    [Fact]
    public void ClearAll_RemovesEverything_AndNotifiesEachDocument()
    {
        var svc = new BreakpointService();
        svc.Toggle(Form1, 1);
        svc.Toggle(Mod1, 2);

        var changed = new List<string>();
        svc.BreakpointsChanged += changed.Add;
        svc.ClearAll();

        svc.All().Should().BeEmpty();
        changed.Should().BeEquivalentTo(new[] { Form1, Mod1 });
    }

    [Fact]
    public void BreakpointsChanged_FiresForTheTouchedDocument()
    {
        var svc = new BreakpointService();
        string? notified = null;
        svc.BreakpointsChanged += uri => notified = uri;

        svc.Toggle(Form1, 4);
        notified.Should().Be(Form1);
    }
}

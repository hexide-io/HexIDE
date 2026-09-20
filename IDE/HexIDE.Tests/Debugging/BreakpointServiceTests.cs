using HexIDE.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.Debugging;

/// <summary>
/// The IDE-side breakpoint store: 1-based line sets per document. Pure in-memory (persistence lives in
/// <c>UserSidecarService</c> — see <see cref="Sidecar.UserSidecarBreakpointTests"/>).
/// </summary>
public sealed class BreakpointServiceTests
{
    private readonly ProjectDefinition _project = TestHelpers.CreateProject();
    private readonly DocumentIdentity _form1;
    private readonly DocumentIdentity _module1;

    public BreakpointServiceTests()
    {
        var form = TestHelpers.CreateForm(_project, "Form1");
        _project.AddForm(form);
        var module = TestHelpers.CreateModule(_project, "Module1");
        _project.AddModule(module);
        _form1 = DocumentIdentity.For(form);
        _module1 = DocumentIdentity.For(module);
    }

    [Fact]
    public void Toggle_TogglesLineOnAndOff()
    {
        var svc = new BreakpointService();

        svc.Toggle(_form1, 3);
        svc.IsBreakpoint(_form1, 3).Should().BeTrue();

        svc.Toggle(_form1, 3);
        svc.IsBreakpoint(_form1, 3).Should().BeFalse();
        svc.GetBreakpoints(_form1).Should().BeEmpty();
    }

    [Fact]
    public void SetDocument_DedupesSorts_AndEmptyClears()
    {
        var svc = new BreakpointService();

        svc.SetDocument(_form1, new[] { 5, 2, 5, 9 });
        svc.GetBreakpoints(_form1).Should().Equal(2, 5, 9);

        svc.SetDocument(_form1, System.Array.Empty<int>());
        svc.GetBreakpoints(_form1).Should().BeEmpty();
        svc.All().Should().NotContainKey(_form1);
    }

    [Fact]
    public void All_ReturnsEveryDocumentWithBreakpoints()
    {
        var svc = new BreakpointService();
        svc.Toggle(_form1, 1);
        svc.Toggle(_module1, 7);

        var all = svc.All();
        all.Keys.Should().BeEquivalentTo(new[] { _form1, _module1 });
        all[_module1].Should().Equal(7);
    }

    [Fact]
    public void ClearDocument_RemovesOnlyThatDocument()
    {
        var svc = new BreakpointService();
        svc.Toggle(_form1, 1);
        svc.Toggle(_module1, 2);

        svc.ClearDocument(_form1);

        svc.GetBreakpoints(_form1).Should().BeEmpty();
        svc.GetBreakpoints(_module1).Should().Equal(2);
    }

    [Fact]
    public void ClearAll_RemovesEverything_AndNotifiesEachDocument()
    {
        var svc = new BreakpointService();
        svc.Toggle(_form1, 1);
        svc.Toggle(_module1, 2);

        var changed = new List<DocumentIdentity>();
        svc.BreakpointsChanged += changed.Add;
        svc.ClearAll();

        svc.All().Should().BeEmpty();
        changed.Should().BeEquivalentTo(new[] { _form1, _module1 });
    }

    [Fact]
    public void BreakpointsChanged_FiresForTheTouchedDocument()
    {
        var svc = new BreakpointService();
        DocumentIdentity? notified = null;
        svc.BreakpointsChanged += document => notified = document;

        svc.Toggle(_form1, 4);
        notified.Should().Be(_form1);
    }

    /// <summary>
    /// The regression the identity exists for: two projects of a group may each hold a
    /// <c>Module1</c>, and under the old string key they shared one entry.
    /// </summary>
    [Fact]
    public void TwoProjectsWithTheSameModuleName_KeepSeparateBreakpoints()
    {
        var other = TestHelpers.CreateProject("Other");
        var otherModule = TestHelpers.CreateModule(other, "Module1");
        other.AddModule(otherModule);

        var svc = new BreakpointService();
        svc.SetDocument(_module1, new[] { 4 });
        svc.SetDocument(DocumentIdentity.For(otherModule), new[] { 9 });

        svc.GetBreakpoints(_module1).Should().Equal(4);
        svc.GetBreakpoints(DocumentIdentity.For(otherModule)).Should().Equal(9);
    }

    /// <summary>
    /// A rename moves no mark, because the key is the document and not one of its names.
    /// </summary>
    [Fact]
    public void RenamingAModule_KeepsItsBreakpoints()
    {
        var svc = new BreakpointService();
        svc.SetDocument(_module1, new[] { 12 });

        _module1.Module!.Name = "Utilities";

        svc.GetBreakpoints(_module1).Should().Equal(12);
        svc.GetBreakpoints(DocumentIdentity.For(_module1.Module!)).Should().Equal(12);
    }

    /// <summary>
    /// Scoped clearing filters the store's own keys rather than walking the project's current documents,
    /// so a document removed from the project while it carried breakpoints is forgotten too. Left behind,
    /// its entry would be unreachable — and would hold the whole project graph alive through the
    /// definition's <c>Owner</c>.
    /// </summary>
    [Fact]
    public void ClearProject_ForgetsADocumentTheProjectNoLongerHolds()
    {
        var svc = new BreakpointService();
        var removed = TestHelpers.CreateModule(_project, "Doomed");
        _project.AddModule(removed);
        var doomed = DocumentIdentity.For(removed);

        svc.SetDocument(doomed, new[] { 3 });
        svc.SetDocument(_module1, new[] { 5 });
        _project.DeleteModule(removed);

        svc.ClearProject(_project);

        svc.All().Should().BeEmpty();
    }

    [Fact]
    public void ClearProject_LeavesAnotherProjectAlone()
    {
        var other = TestHelpers.CreateProject("Other");
        var otherModule = TestHelpers.CreateModule(other, "Module1");
        other.AddModule(otherModule);

        var svc = new BreakpointService();
        svc.SetDocument(_module1, new[] { 4 });
        svc.SetDocument(DocumentIdentity.For(otherModule), new[] { 9 });

        svc.ClearProject(_project);

        svc.GetBreakpoints(_module1).Should().BeEmpty();
        svc.GetBreakpoints(DocumentIdentity.For(otherModule)).Should().Equal(9);
    }
}

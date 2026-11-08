using System.Collections.Generic;
using HexIDE.IDE;

namespace HexIDE.Tests.IDE;

public class NamedLayoutServiceTests
{
    [Fact]
    public void SaveReloadRenameDelete_PersistsIndependentSnapshots()
    {
        var state = Substitute.For<IWindowStateService>();
        IReadOnlyDictionary<string, LayoutManifest> disk = new Dictionary<string, LayoutManifest>();
        state.LoadNamedLayouts().Returns(_ => disk);
        state.When(s => s.SaveNamedLayouts(Arg.Any<IReadOnlyDictionary<string, LayoutManifest>>()))
            .Do(c => disk = c.Arg<IReadOnlyDictionary<string, LayoutManifest>>());
        var layouts = new NamedLayoutService(state);

        layouts.Save("Coding", LayoutManifest.FullEditor).Should().BeTrue();
        layouts.Save("Design", LayoutManifest.Default).Should().BeTrue();
        layouts = new NamedLayoutService(state);
        layouts.Names.Should().BeEquivalentTo("Coding", "Design");
        layouts.Get("coding")!.Tools.Should().OnlyContain(t => !t.Open);
        layouts.Get("Design")!.Tools.Should().Contain(t => t.Open);
        layouts.Rename("Coding", "Review").Should().BeTrue();
        layouts.Delete("Design");
        layouts = new NamedLayoutService(state);
        layouts.Names.Should().Equal("Review");
        layouts.Get("Review")!.Tools.Should().OnlyContain(t => !t.Open);
    }

    [Fact]
    public void InvalidNamesAndCollisions_DoNotOverwriteLayouts()
    {
        var state = Substitute.For<IWindowStateService>();
        var layouts = new NamedLayoutService(state);
        layouts.Save(" Coding ", LayoutManifest.Default).Should().BeTrue();
        layouts.Save("coding", LayoutManifest.FullEditor).Should().BeFalse();
        layouts.Save(" ", LayoutManifest.Default).Should().BeFalse();
        layouts.Save("Review", LayoutManifest.FullEditor).Should().BeTrue();
        layouts.Rename("Review", "CODING").Should().BeFalse();
        layouts.Rename("Review", " ").Should().BeFalse();
        layouts.Get("Coding").Should().BeSameAs(LayoutManifest.Default);
        layouts.Names.Should().BeEquivalentTo("Coding", "Review");
        state.Received(2).SaveNamedLayouts(Arg.Any<IReadOnlyDictionary<string, LayoutManifest>>());
    }

    [Fact]
    public void FailedWrite_KeepsPreviousNamesAndSnapshots()
    {
        var state = Substitute.For<IWindowStateService>();
        var layouts = new NamedLayoutService(state);
        layouts.Save("Coding", LayoutManifest.Default).Should().BeTrue();
        state.When(s => s.SaveNamedLayouts(Arg.Any<IReadOnlyDictionary<string, LayoutManifest>>()))
            .Do(_ => throw new IOException("write failed"));

        var rename = () => layouts.Rename("Coding", "Review");
        rename.Should().Throw<IOException>();
        layouts.Names.Should().Equal("Coding");
        layouts.Get("Coding").Should().BeSameAs(LayoutManifest.Default);
    }
}

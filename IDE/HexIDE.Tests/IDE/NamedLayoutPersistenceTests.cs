using HexIDE.IDE;

namespace HexIDE.Tests.IDE;

public sealed class NamedLayoutPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hexide-layouts-" + Guid.NewGuid().ToString("N"));

    public NamedLayoutPersistenceTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public void NamedLayouts_SurviveDiskReload_WithoutChangingCurrentLayout()
    {
        var state = WindowStateService.ForDirectory(Substitute.For<ISettingsService>(), _directory);
        state.SaveLayoutManifest(LayoutManifest.Default);
        var layouts = new NamedLayoutService(state);
        layouts.Save("../Coding", LayoutManifest.FullEditor).Should().BeTrue();
        layouts.Rename("../Coding", "Review").Should().BeTrue();

        var reopened = WindowStateService.ForDirectory(Substitute.For<ISettingsService>(), _directory);
        var restored = new NamedLayoutService(reopened);
        restored.Names.Should().Equal("Review");
        restored.Get("Review")!.Tools.Should().OnlyContain(t => !t.Open);
        reopened.LoadLayoutManifest()!.Tools.Should().Equal(LayoutManifest.Default.Tools);
        restored.Delete("Review");
        new NamedLayoutService(reopened).Names.Should().BeEmpty();
        Directory.GetFiles(_directory).Select(Path.GetFileName)
            .Should().BeEquivalentTo("dock-layout.json", "named-layouts.json");
    }

    [Fact]
    public void CorruptNamedLayouts_FallBackWithoutDiscardingCurrentLayout()
    {
        var state = WindowStateService.ForDirectory(Substitute.For<ISettingsService>(), _directory);
        state.SaveLayoutManifest(LayoutManifest.Default);
        File.WriteAllText(Path.Combine(_directory, "named-layouts.json"), "{not json");

        new NamedLayoutService(state).Names.Should().BeEmpty();
        state.LoadLayoutManifest()!.Tools.Should().Equal(LayoutManifest.Default.Tools);
    }
}

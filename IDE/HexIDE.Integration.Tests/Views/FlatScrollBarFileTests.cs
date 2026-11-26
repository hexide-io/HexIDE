using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class FlatScrollBarFileTests
{
    private sealed class Sink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static string Source()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !Directory.Exists(Path.Join(at.FullName, "corpus"))) at = at.Parent;
        at.Should().NotBeNull();
        var root = Path.Join(at!.FullName, "corpus", "inbox-controls");
        return HexIDE.IDE.Vb6TextFile.ReadAllText(Path.Join(root, "saved", "flatscrollbar", "Form1.frm"));
    }

    [AvaloniaFact]
    public void Corpus_bars_are_selectable_designer_components()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Source(), new Sink())!;
        var vm = new HexIDE.VisualDesigner.FormEditViewModel(null!, Substitute.For<HexIDE.IDE.IEventBus>(),
            Substitute.For<HexIDE.Projects.IProjectService>(), null!, null!, Substitute.For<HexIDE.IDE.ISettingsService>(),
            Substitute.For<HexIDE.Localization.ILocalizationService>()).Initialize(form);
        var bars = vm.AllComponents.Where(c => c.Name.StartsWith("FlatScrollBar")).ToArray();
        bars.Should().HaveCount(7);
        foreach (var bar in bars)
        {
            bar.Instance.BaseClass.Should().BeSameAs(HexIDE.Runtime.Components.InBox.FlatScrollBarComponentClass.Instance);
            vm.SelectedComponent = bar;
            vm.SelectedComponent.Should().BeSameAs(bar);
            ((ComponentBaseClass)bar.Instance.BaseClass).InstantiateForDesigner(bar.Instance).Should().BeOfType<VBFlatScrollBar>();
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Headless.XUnit;
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

    [AvaloniaTheory]
    [InlineData("MouseIcon = 0")]
    [InlineData("Index = 0")]
    [InlineData("Unmeasured = 1")]
    public void Unmodelled_blocks_stay_carried(string property)
    {
        var source = Source().Replace("      Value           =   30", "      " + property + Environment.NewLine + "      Value           =   30");
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
        form.UnknownChildSubtreeTexts.Should().ContainSingle(b => b.Contains("FlatScrollBar2"));
        new FormSerializer().Serialize(form, "Form1.frm").frmText.ReplaceLineEndings().Should().Be(source.ReplaceLineEndings());
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

    [AvaloniaFact]
    public void Saved_blocks_round_trip_verbatim_including_after_a_form_edit()
    {
        var source = Source();
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
        form.UnknownChildSubtreeTexts.Should().BeEmpty();
        form.Components.Count(c => c.BaseClass is HexIDE.Runtime.Components.InBox.FlatScrollBarComponentClass).Should().Be(7);
        var serializer = new FormSerializer();
        Encoding.ASCII.GetBytes(serializer.Serialize(form, "Form1.frm").frmText).Should().Equal(Encoding.ASCII.GetBytes(source));
        form.Components[0].SetProperty(VBProperties.CaptionProperty, "Edited");
        var edited = serializer.Serialize(form, "Form1.frm").frmText.ReplaceLineEndings();
        foreach (var block in source.ReplaceLineEndings().Split("   Begin MSComCtl2.FlatScrollBar").Skip(1))
            edited.Should().Contain("   Begin MSComCtl2.FlatScrollBar" + block[..(block.IndexOf("   End", StringComparison.Ordinal) + "   End".Length)]);
        var reloaded = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), edited, new Sink())!;
        reloaded.Components.Count(c => c.BaseClass is HexIDE.Runtime.Components.InBox.FlatScrollBarComponentClass).Should().Be(7);
    }

    [AvaloniaFact]
    public void Hand_written_orientation_without_a_high_word_does_not_swap_size()
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn(Source().Replace("1638400", "0"));
        var bar = (VBFlatScrollBar)ContainerRuntimeHarness.Child(canvas, "FlatScrollBar3");
        bar.TryGetProperty("Orientation", out var direction).Should().BeTrue();
        Convert.ToInt32(direction.Value).Should().Be(0);
        bar.Width.Should().Be(160);
        bar.Height.Should().Be(17);
    }

    [AvaloniaTheory]
    [InlineData(1, 1, 1, 0, 0, 0, 32767, 1, 1, true, 0)]
    [InlineData(2, 1, 1, 0, 30, 10, 200, 2, 20, true, 0)]
    [InlineData(3, 0, 1, 0, 0, 0, 32767, 1, 1, true, 0)]
    [InlineData(4, 1, 0, 1, 0, 0, 32767, 1, 1, false, 11)]
    [InlineData(5, 1, 2, 2, 0, 0, 32767, 1, 1, true, 0)]
    [InlineData(6, 1, 1, 0, 0, 0, 32767, 1, 1, true, 0)]
    [InlineData(7, 1, 1, 0, 0, 0, 32767, 1, 1, true, 0)]
    public void Saved_controls_load_low_words_without_swapping_dimensions(int index, int orientation, int appearance,
        int arrows, int value, int min, int max, int small, int large, bool enabled, long pointer)
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn(Source());
        canvas.Children.OfType<VBFlatScrollBar>().Should().HaveCount(7);
        var bar = (VBFlatScrollBar)ContainerRuntimeHarness.Child(canvas, "FlatScrollBar" + index);
        foreach (var (name, expected) in new[] { ("Orientation", orientation), ("Appearance", appearance), ("Arrows", arrows),
                     ("Value", value), ("Min", min), ("Max", max), ("SmallChange", small), ("LargeChange", large) })
        {
            bar.TryGetProperty(name, out var read).Should().BeTrue();
            Convert.ToInt32(read.Value).Should().Be(expected);
        }
        bar.TryGetProperty("MousePointer", out var mouse).Should().BeTrue();
        mouse.Value.Should().Be(pointer);
        bar.IsEnabled.Should().Be(enabled);
        bar.Width.Should().Be(index == 7 ? 100 : 160);
        bar.Height.Should().Be(17);
        bar.Focusable.Should().BeFalse();
    }
}

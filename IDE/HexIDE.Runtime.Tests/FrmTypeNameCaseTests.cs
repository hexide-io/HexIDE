using System.IO;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// Measured in VB6 SP6, #824 (oracle PR #4517): Begin type names ignore case; saves use the library's spelling.
public class FrmTypeNameCaseTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new System.InvalidOperationException(error);
    }

    private static FormDefinition Load(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;

    private static string Save(FormDefinition form) => new FormSerializer().Serialize(form, "Form1.frm").frmText;

    [Theory]
    [InlineData("vb.label")]
    [InlineData("VB.LABEL")]
    public void A_label_type_in_another_case_loads_and_saves_in_VB6s_spelling(string type)
    {
        var form = Load($"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin {type} Label1
                  Caption = "L1"
               End
            End
            Attribute VB_Name = "Form1"
            """);

        var label = form.Components.Single(c => c.BaseClass is LabelComponentClass);
        label.GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("L1");
        Save(form).Should().Contain("Begin VB.Label Label1");
    }

    [Theory]
    [InlineData("Begin VB.Form", "Begin vb.form")]
    [InlineData("Begin MSComctlLib.ProgressBar", "Begin mscomctllib.progressbar")]
    [InlineData("Begin VB.Form", "Begin VB.Form")]
    public void A_saved_form_round_trips_with_only_the_type_spelling_normalised(string canonical, string input)
    {
        var source = Vb6TextFile.ReadAllText(Path.Join(RepoTree.Root(), "corpus", "inbox-controls", "saved", "six", "Form1.frm"));
        var form = Load(source.Replace(canonical, input));

        form.Components.Count(c => c.BaseClass is ProgressBarComponentClass).Should().Be(9);
        Save(form).ReplaceLineEndings().Should().Be(source.ReplaceLineEndings());
    }

    [Fact]
    public void An_inbox_writer_uses_the_librarys_spelling_for_a_loaded_type()
    {
        var component = new ComponentInstance(ProgressBarComponentClass.Instance, "ProgressBar1");
        ProgressBarComponentClass.Instance.Loaded(component, "mscomctllib.progressbar");

        ProgressBarComponentClass.Instance.TypeNameOf(component).Should().Be("MSComctlLib.ProgressBar");
    }
}

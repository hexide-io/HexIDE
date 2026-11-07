using System.IO;
using System.Text;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

// VB6's save in #1088, issuecomment-6428845889. These exercise preservation, not configured runtime loading.
public class ToolbarSerializationTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static string CorpusDirectory()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            var candidate = Path.Join(at.FullName, "corpus", "inbox-controls", "saved", "toolbar");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("The Toolbar corpus ships with the repository.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Vb6s_toolbar_form_and_companion_survive_a_save(bool editFormCaption)
    {
        var directory = CorpusDirectory();
        var original = File.ReadAllBytes(Path.Join(directory, "Form1.frm"));
        var companion = File.ReadAllBytes(Path.Join(directory, "Form1.frx"));
        var source = Vb6TextFile.ReadDocument(original).Text;
        var expected = editFormCaption
            ? source.Replace("Caption         =   \"Form1\"", "Caption         =   \"Edited\"", StringComparison.Ordinal)
            : source;

        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source,
            new Sink(), FrxDeserializer.Read(companion, source))!;
        form.Should().NotBeNull();
        form.CanSaveFaithfully.Should().BeTrue();
        if (editFormCaption)
            form.Components.Single(c => c.BaseClass is FormComponentClass).SetProperty(VBProperties.CaptionProperty, "Edited");

        var (text, bytes) = new FormSerializer().Serialize(form, "Form1.frm");
        // The corpus is measured binary data (-text), including its CRLFs; compare the actual output bytes.
        Encoding.ASCII.GetBytes(text).Should().Equal(editFormCaption ? Encoding.ASCII.GetBytes(expected) : original);
        bytes.Should().Equal(companion);

        var reread = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), text,
            new Sink(), FrxDeserializer.Read(bytes!, text))!;
        reread.CanSaveFaithfully.Should().BeTrue();
        var second = new FormSerializer().Serialize(reread, "Form1.frm");
        second.frmText.Should().Be(text);
        second.frxContent.Should().Equal(companion);
    }
}

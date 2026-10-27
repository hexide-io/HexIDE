using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// VB6 SP6 saves: #1087, issuecomment-6427372779 and issuecomment-6428228813.
public class StatusBarPersistenceTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new InvalidOperationException(error);
    }

    internal static string Corpus(string folder)
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent)
        {
            foreach (var half in new[] { "saved", "pending" })
            {
                var path = Path.Join(at.FullName, "corpus", "inbox-controls", half, folder, "Form1.frm");
                if (File.Exists(path)) return path;
            }
        }
        throw new FileNotFoundException(folder);
    }

    [Theory]
    [InlineData("statusbar")]
    [InlineData("statusbar-picture")]
    public void Measured_saves_round_trip_byte_for_byte(string folder)
    {
        var path = Corpus(folder);
        var bytes = File.ReadAllBytes(path);
        var source = Encoding.UTF8.GetString(bytes);
        var companion = Path.ChangeExtension(path, ".frx");
        var originalFrx = File.Exists(companion) ? File.ReadAllBytes(companion) : null;
        var blobs = originalFrx == null ? null : FrxDeserializer.Read(originalFrx, source);
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe");
        var form = new FormDeserializer().Deserialize(project, source, new Sink(), frxBlobs: blobs)!;
        form.CanSaveFaithfully.Should().BeTrue();
        var (text, frx) = new FormSerializer().Serialize(form, "Form1.frm");
        Encoding.UTF8.GetBytes(text).Should().Equal(bytes);
        if (originalFrx == null) frx.Should().BeNull();
        else frx.Should().Equal(originalFrx);
    }
}

using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #3508: a .frm holds only a Timer's Left and Top, so its size has to come from the class, or it loads 0 by 0 and can't be
// seen or clicked.
public class TimerOnTheDesignerTests
{
    private const string Source =
        "VERSION 5.00\r\nBegin VB.Form Form1\r\n   ClientHeight = 3000\r\n   ClientWidth = 4000\r\n" +
        "   Begin VB.Timer Timer1\r\n      Left = 600\r\n      Top = 300\r\n   End\r\nEnd\r\n";

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static FormDefinition Load()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Source, new NullSink())!;
        project.AddForm(form);
        return form;
    }

    [AvaloniaFact]
    public void A_Timer_is_still_written_with_no_size() =>
        new FormSerializer().Serialize(Load(), "Form1.frm").frmText.Should().Contain("Begin VB.Timer Timer1")
            .And.Subject.Split("Begin VB.Timer Timer1")[1].Split("End")[0].Should().NotContain("Width").And.NotContain("Height");
}

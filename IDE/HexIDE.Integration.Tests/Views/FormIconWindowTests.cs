using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The icon a running form's window is made with (#1594): the form's own, none for (None), and the runner's default for a
/// form with no Icon line. What Windows is told and draws, big icon and small, was checked live against vb6.exe's
/// windows; here, the icon each window is given.
/// </summary>
public class FormIconWindowTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    // A one-pixel icon: the ICONDIR, its one entry, then a 32-bit DIB with its AND mask.
    private static readonly byte[] OnePixelIcon =
    [
        0, 0, 1, 0, 1, 0,
        1, 1, 0, 0, 1, 0, 32, 0, 48, 0, 0, 0, 22, 0, 0, 0,
        40, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0, 32, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 255, 255,
        0, 0, 0, 0,
    ];

    private static byte[] Record(byte[] image) =>
        [.. BitConverter.GetBytes(image.Length + 8), 0x6C, 0x74, 0, 0, .. BitConverter.GetBytes(image.Length), .. image];

    private static Window Window(byte[]? icon)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   ClientHeight    =   1500\r\n   ClientWidth     =   2520\r\nEnd\r\n"
            + "Attribute VB_Name = \"Form1\"\r\n", new NullSink())!;
        var root = form.Components.First(c => c.BaseClass is FormComponentClass);
        if (icon is not null)
            root.SetProperty(VBProperties.IconProperty, icon);
        return FormComponentClass.Instance.InstantiateWindow(root);
    }

    [AvaloniaFact]
    public void A_form_with_its_own_Icon_runs_with_it() =>
        Window(Record(OnePixelIcon)).Icon.Should().NotBeNull();

    [AvaloniaFact]
    public void A_form_whose_Icon_is_None_takes_no_default()
    {
        var runner = new WindowIcon(new MemoryStream(OnePixelIcon));
        VBLoader.DefaultFormIcon = runner;
        try
        {
            Window(Record([])).Icon.Should().BeNull("(None) is no icon at all, not the runner's");
        }
        finally
        {
            VBLoader.DefaultFormIcon = null;
        }
    }

    [AvaloniaFact]
    public void A_form_with_no_Icon_line_runs_with_the_runners_default()
    {
        var runner = new WindowIcon(new MemoryStream(OnePixelIcon));
        VBLoader.DefaultFormIcon = runner;
        try
        {
            Window(null).Icon.Should().BeSameAs(runner);
        }
        finally
        {
            VBLoader.DefaultFormIcon = null;
        }
    }

    [AvaloniaFact]
    public void Without_a_runners_default_a_form_with_no_Icon_line_keeps_Avalonias() =>
        Window(null).Icon.Should().BeNull("Avalonia then gives it the executable's icon, the IDE's on F5");
}

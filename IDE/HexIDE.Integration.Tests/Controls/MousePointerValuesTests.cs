using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using HexIDE.Runtime.Components;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Controls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Controls;

/// <summary>
/// MousePointer has VB6's 17 values, named as VB6 names them and drawn as VB6 draws them (#120). It had 13: 4, 5, 13 and
/// 14 loaded as bare numbers, saved without the comment VB6 writes beside them, showed a blank in the Properties window,
/// and drew the default pointer.
/// </summary>
/// <remarks>
/// Every expectation is the oracle's "MousePointer's names and pointers": the comments are what the VB6 IDE wrote on
/// saving a form that gave a Label each value bare, and the pointers are what GetCursorInfo reported over each Label of
/// the same form built and run.
/// </remarks>
public class MousePointerValuesTests
{
    [AvaloniaFact]
    public void The_Properties_window_offers_all_seventeen_values_by_VB6s_names()
    {
        new PropertyEnumBox { PropertyType = typeof(VBCursorType) }.Options!.Select(o => o.Text).Should().Equal(
            "0 - Default", "1 - Arrow", "2 - Cross", "3 - I-Beam", "4 - Icon", "5 - Size", "6 - Size NE SW", "7 - Size N S",
            "8 - Size NW SE", "9 - Size W E", "10 - Up Arrow", "11 - Hourglass", "12 - No Drop", "13 - Arrow and Hourglass",
            "14 - Arrow and Question", "15 - Size All", "99 - Custom");
    }

    // Measured defaults and IDE saves: oracle mousepointers-names-and-pointers.md, #1970.
    [Theory]
    [InlineData("Form")]
    [InlineData("CommandButton")]
    [InlineData("Label")]
    [InlineData("TextBox")]
    [InlineData("Frame")]
    [InlineData("CheckBox")]
    [InlineData("OptionButton")]
    [InlineData("ComboBox")]
    [InlineData("ListBox")]
    [InlineData("HScrollBar")]
    [InlineData("VScrollBar")]
    [InlineData("PictureBox")]
    [InlineData("Image")]
    public void Designer_defaults_to_zero_and_save_omits_zero_but_keeps_arrow(string type)
    {
        var body = type == "Form" ? "" : $"   Begin VB.{type} Control1\r\n   End\r\n";
        var frm = "VERSION 5.00\r\nBegin VB.Form Form1\r\n" + body
                  + "End\r\nAttribute VB_Name = \"Form1\"\r\n";
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), frm, new NullSink())!;
        var component = type == "Form" ? HexIDE.Runtime.VBLoader.FormComponentOf(form) : form.Components.Single(c => c.BaseClass is not FormComponentClass);
        component.GetPropertyOrDefault(VBProperties.MousePointerProperty).Should().Be(VBCursorType.Default);
        new ComponentInstance(component.BaseClass, "NewControl").GetPropertyOrDefault(VBProperties.MousePointerProperty)
            .Should().Be(VBCursorType.Default);
        component.SetProperty(VBProperties.MousePointerProperty, VBCursorType.Default);
        var (zero, _) = new FormSerializer().Serialize(form, "Form1.frm");
        zero.Should().NotContain("MousePointer");
        var explicitZero = frm.Replace(type == "Form" ? "Begin VB.Form Form1\r\n" : $"Begin VB.{type} Control1\r\n",
            (type == "Form" ? "Begin VB.Form Form1\r\n" : $"Begin VB.{type} Control1\r\n") + "      MousePointer = 0  'Default\r\n");
        var loadedZero = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), explicitZero, new NullSink())!;
        new FormSerializer().Serialize(loadedZero, "Form1.frm").Item1.Should().NotContain("MousePointer");
        component.SetProperty(VBProperties.MousePointerProperty, VBCursorType.Arrow);
        var (arrow, _) = new FormSerializer().Serialize(form, "Form1.frm");
        arrow.Should().Contain("MousePointer").And.Contain("1  'Arrow");
        var reloaded = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), arrow, new NullSink())!;
        var saved = type == "Form" ? HexIDE.Runtime.VBLoader.FormComponentOf(reloaded) : reloaded.Components.Single(c => c.BaseClass is not FormComponentClass);
        saved.GetPropertyOrDefault(VBProperties.MousePointerProperty).Should().Be(VBCursorType.Arrow);
    }

    // Saved-form regression found by #2540, recorded in #1970: keep the file value on Me.
    [AvaloniaTheory]
    [InlineData(0, "Default", StandardCursorType.Arrow)]
    [InlineData(1, "Arrow", StandardCursorType.Arrow)]
    [InlineData(11, "Hourglass", StandardCursorType.Wait)]
    public void A_saved_form_reads_its_MousePointer_at_runtime(int value, string name, StandardCursorType pointer)
    {
        var source = $"""
            VERSION 5.00
            Begin VB.Form Form1
               MousePointer = {value} '{name}
            End
            Attribute VB_Name = "Form1"
            Private Sub Form_Load()
                Debug.Print CStr(Me.MousePointer)
            End Sub
            """;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        var printed = new List<string>();
        VBDebugConsole.Output += printed.Add;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            try
            {
                Dispatcher.UIThread.RunJobs();
                printed.Should().Equal(value.ToString(CultureInfo.InvariantCulture));
                window.Cursor.Should().NotBeNull();
                window.Cursor!.ToString().Should().Be(new Cursor(pointer).ToString());
                VBCursorTypeExtensions.PointerOf(window.Cursor).Should().Be((VBCursorType)value);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            VBDebugConsole.Output -= printed.Add;
        }
    }

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    [Theory]
    [InlineData(4, "Icon")]
    [InlineData(5, "Size")]
    [InlineData(13, "Arrow and Hourglass")]
    [InlineData(14, "Arrow and Question")]
    public void A_form_keeps_VB6s_comment_beside_the_value(int value, string name)
    {
        var line = $"      MousePointer    =   {value}  '{name}";
        var frm = "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n   ClientHeight    =   3000\r\n"
                  + "   ClientLeft      =   60\r\n   ClientTop       =   450\r\n   ClientWidth     =   4000\r\n"
                  + "   Begin VB.Label Label1 \r\n      Caption         =   \"Label1\"\r\n      Height          =   255\r\n"
                  + "      Left            =   120\r\n" + line + "\r\n      TabIndex        =   0\r\n      Top             =   120\r\n"
                  + "      Width           =   1215\r\n   End\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n";
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), frm, new NullSink())!;

        var (written, _) = new FormSerializer().Serialize(form, "Form1.frm");

        written.Should().Contain(line + "\r\n", "VB6 writes the value's name beside it, and a save that drops it changes the file");
    }

    [Theory]
    [InlineData(4, StandardCursorType.Arrow)]
    [InlineData(5, StandardCursorType.SizeAll)]
    [InlineData(13, StandardCursorType.AppStarting)]
    [InlineData(14, StandardCursorType.Help)]
    public void Each_value_draws_the_pointer_vb6_draws(int value, StandardCursorType pointer)
    {
        // Icon draws the plain arrow on Windows 10: IDC_ICON is obsolete there, and LoadCursor gives nothing for it.
        ((VBCursorType)value).ToStandardCursorType().Should().Be(pointer);
    }
}

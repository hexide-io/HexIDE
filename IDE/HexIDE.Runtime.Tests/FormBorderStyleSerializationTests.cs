using System;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// Saved spellings and default for #1596: https://github.com/hexide-io/HexIDE/pull/5216.
public class FormBorderStyleSerializationTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new InvalidOperationException(error);
    }

    private static void Set(ComponentInstance form, int style)
    {
        var before = form.GetPropertyOrDefault(VBProperties.FormBorderStyleProperty);
        form.SetProperty(VBProperties.FormBorderStyleProperty, (VBFormBorderStyle)style);
        if ((int)before != style)
            form.BaseClass.ApplyDesignerConsequences(form, VBProperties.FormBorderStyleProperty);
    }

    private static void Saved(FormDefinition form, int style, bool min, bool max, bool taskbar)
    {
        var lines = new FormSerializer().Serialize(form, "Form1.frm").frmText.ReplaceLineEndings("\n").Split('\n');
        foreach (var (name, value) in new[] { ("MinButton", min), ("MaxButton", max), ("ShowInTaskbar", taskbar) })
        {
            var matching = lines.Where(l => l.TrimStart().StartsWith(name + " ")).ToArray();
            if (value) matching.Should().BeEmpty();
            else matching.Should().Equal("   " + name.PadRight(16) + "=   0   'False");
        }
        if (style == 2) lines.Should().NotContain(l => l.Contains("BorderStyle"));
        else lines.Should().Contain(l => l.StartsWith("   BorderStyle     =   " + style + "  '"));
    }

    [Theory]
    [InlineData(0, true, true, false)]
    [InlineData(1, false, false, true)]
    [InlineData(3, false, false, false)]
    [InlineData(4, false, false, false)]
    [InlineData(5, false, false, false)]
    public void Style_and_back_save_only_measured_lines(int style, bool min, bool max, bool taskbar)
    {
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        Set(form.Components.Single(), style);
        Saved(form, style, min, max, taskbar);
        Set(form.Components.Single(), 2);
        Saved(form, 2, true, true, true);
    }

    [Fact]
    public void Hand_set_rows_save_only_measured_lines()
    {
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        var instance = form.Components.Single();
        instance.SetProperty(VBProperties.MinButtonProperty, false);
        instance.SetProperty(VBProperties.ShowInTaskbarProperty, false);
        Set(instance, 1);
        Saved(form, 1, false, false, true);
        Set(instance, 2);
        Saved(form, 2, true, true, true);
        Set(instance, 3);
        instance.SetProperty(VBProperties.MaxButtonProperty, true);
        Saved(form, 3, false, true, false);
        Set(instance, 1);
        Saved(form, 1, false, false, true);
        Set(instance, 3);
        Set(instance, 0);
        Saved(form, 0, false, false, false);
        Set(instance, 2);
        instance.SetProperty(VBProperties.MinButtonProperty, false);
        Set(instance, 0);
        Saved(form, 0, false, true, false);
        Set(instance, 2);
        instance.SetProperty(VBProperties.MinButtonProperty, false);
        Set(instance, 2);
        Saved(form, 2, false, true, true);
        Set(instance, 1);
        instance.SetProperty(VBProperties.ShowInTaskbarProperty, false);
        Set(instance, 1);
        Saved(form, 1, false, false, false);
    }

    [Theory]
    [InlineData("MinButton", true)]
    [InlineData("MinButton", false)]
    [InlineData("MaxButton", true)]
    [InlineData("MaxButton", false)]
    [InlineData("ShowInTaskbar", true)]
    [InlineData("ShowInTaskbar", false)]
    public void Loaded_true_is_dropped_and_false_round_trips(string name, bool isTrue)
    {
        var value = isTrue ? "-1  'True" : "0   'False";
        var line = "   " + name.PadRight(16) + "=   " + value + "\r\n";
        var source = "VERSION 5.00\r\nBegin VB.Form Form1 \r\n" + line
            + "End\r\nAttribute VB_Name = \"Form1\"\r\n";
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
        var saved = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        saved.Should().Be(value.StartsWith("-1") ? source.Replace(line, "") : source);
    }

    [Fact]
    public void Mdi_root_keeps_its_existing_serialization_rules()
    {
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        form.UpdateRootTypeName("VB.MDIForm");
        var instance = form.Components.Single();
        instance.SetProperty(VBProperties.MinButtonProperty, true);
        instance.SetProperty(VBProperties.MaxButtonProperty, true);
        instance.SetProperty(VBProperties.ShowInTaskbarProperty, true);
        var saved = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        foreach (var name in new[] { "MinButton", "MaxButton", "ShowInTaskbar" })
            saved.Should().Contain("   " + name.PadRight(16) + "=   -1  'True");
    }

    [Theory]
    [InlineData("VB.UserControl")]
    [InlineData("VB.PropertyPage")]
    public void Other_roots_keep_explicit_true_lines(string root)
    {
        var source = "VERSION 5.00\r\nBegin " + root + " Form1 \r\n"
            + "   MaxButton       =   -1  'True\r\n   MinButton       =   -1  'True\r\n"
            + "   ShowInTaskbar   =   -1  'True\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n";
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
        new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Be(source);
    }

    [Theory]
    [InlineData(0, "None")]
    [InlineData(1, "Fixed Single")]
    [InlineData(2, "Sizable")]
    [InlineData(3, "Fixed Dialog")]
    [InlineData(4, "Fixed ToolWindow")]
    [InlineData(5, "Sizable ToolWindow")]
    public void Modelled_styles_write_the_measured_line_in_alphabetical_order(int style, string name)
    {
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        var instance = form.Components.Single();
        var property = instance.BaseClass.Properties.SingleOrDefault(p => p.Name == "BorderStyle");
        property.Should().NotBeNull();
        instance.SetProperty(VBProperties.BackColorProperty, HexIDE.Runtime.BuiltinTypes.VBColor.FromOle(0));
        instance.SetProperty(VBProperties.CaptionProperty, "Form1");
        instance.SetUntypedProperty(property!, Enum.ToObject(property!.PropertyType, style));
        var saved = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        if (style == 2) saved.Should().NotContain("BorderStyle");
        else saved.Should().Contain("   BackColor       =   &H00000000&\r\n   BorderStyle     =   " + style
            + "  '" + name + "\r\n   Caption");
    }

    [Theory]
    [InlineData("   BorderStyle     =   3  'Fixed Dialog\r\n")]
    [InlineData("")]
    public void Fixed_dialog_and_default_sizable_round_trip_byte_for_byte(string border)
    {
        var source = "VERSION 5.00\r\nBegin VB.Form Form1 \r\n" + border
            + "   Caption         =   \"Form1\"\r\n   ClientHeight    =   1500\r\n"
            + "   ClientLeft      =   60\r\n   ClientTop       =   345\r\n   ClientWidth     =   2520\r\n"
            + "   ScaleHeight     =   1500\r\n   ScaleWidth      =   2520\r\n"
            + "End\r\nAttribute VB_Name = \"Form1\"\r\n";
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
        var saved = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        saved.Should().Be(source);
        if (border.Length == 0) saved.Should().NotContain("BorderStyle");
    }

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("\"unreadable\"")]
    public void Unmeasured_values_keep_the_raw_line(string value)
    {
        var source = "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   BorderStyle     =   " + value
            + "\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n";
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
        form.Components.Single().UnknownRawPropertyLines.Should().Contain("   BorderStyle     =   " + value);
        new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Contain("   BorderStyle     =   " + value + "\r\n");
    }
}

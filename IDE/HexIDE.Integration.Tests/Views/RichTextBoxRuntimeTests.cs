using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinTypes;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.RichText;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class RichTextBoxRuntimeTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static List<string> Run(string body, string extra = "", string savedProperties = "", byte[]? companion = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var source = "VERSION 5.00\r\nBegin VB.Form Form1\r\nClientWidth = 4500\r\nClientHeight = 3000\r\n"
            + "Begin RichTextLib.RichTextBox r\r\nWidth = 3000\r\nHeight = 1500\r\n" + savedProperties.ReplaceLineEndings("\r\n") + "\r\nEnd\r\nEnd\r\n"
            + "Attribute VB_Name = \"Form1\"\r\nPrivate Sub Form_Load()\r\n" + body.ReplaceLineEndings("\r\n") + "\r\nEnd Sub\r\n" + extra.ReplaceLineEndings("\r\n");
        project.AddForm(new FormDeserializer().Deserialize(project, source, new NullSink(),
            companion is null ? null : FrxDeserializer.Read(companion, source))!);
        var printed = new List<string>();
        Action<string> print = printed.Add;
        using var cancel = new CancellationTokenSource();
        VBDebugConsole.Output += print;
        var context = System.Threading.ExecutionContext.Capture();
        try
        {
            _ = VBLoader.RunForm(project.StartupForm!, cancel.Token, out _);
            for (var i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            cancel.Cancel();
            Dispatcher.UIThread.RunJobs();
            if (context != null) System.Threading.ExecutionContext.Restore(context);
            VBDebugConsole.Output -= print;
        }
        return printed;
    }

    private static FormDefinition RecordedForm()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Join(root.FullName, "corpus"))) root = root.Parent;
        root.Should().NotBeNull();
        var path = Path.Join(root!.FullName, "corpus", "inbox-controls", "saved", "richtextbox", "Form1.frm");
        var source = File.ReadAllText(path);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink(),
            FrxDeserializer.Read(File.ReadAllBytes(Path.ChangeExtension(path, ".frx")), source))!;
        project.AddForm(form);
        return form;
    }

    [AvaloniaFact]
    public void Recorded_saved_form_runs_with_its_decimal_background_colour()
    {
        using var cancel = new CancellationTokenSource();
        VBFormRuntime? window = null;
        var context = System.Threading.ExecutionContext.Capture();
        try
        {
            _ = VBLoader.RunForm(RecordedForm(), cancel.Token, out window);
            window.UpdateLayout();
            var boxes = window.GetVisualDescendants().OfType<VBRichTextBox>().ToList();
            boxes.Should().HaveCount(3);
            var box = boxes.Single(b => VBProps.GetName(b) == "RichTextBox1");
            box.SavedBackColor.Should().Be(VBColor.FromColor(255, 255, 192));
            box.Document.Text.Should().Be("Hello bold");
        }
        finally
        {
            cancel.Cancel();
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            if (context != null) System.Threading.ExecutionContext.Restore(context);
        }
    }

    [AvaloniaFact]
    public void Recorded_saved_boxes_have_labelled_view_only_designer_placeholders()
    {
        var form = RecordedForm();
        var instances = form.Components.Where(c => c.BaseClass is RichTextBoxComponentClass).ToList();
        instances.Should().HaveCount(3);
        foreach (var instance in instances)
        {
            var visual = RichTextBoxComponentClass.Instance.InstantiateForDesigner(instance);
            var border = visual.Should().BeOfType<Border>().Subject;
            border.Background.Should().NotBeNull();
            border.BorderBrush.Should().NotBeNull();
            border.Child.Should().BeOfType<TextBlock>().Which.Text.Should().Be("RichTextBox");
            visual.Focusable.Should().BeFalse();
        }
        // Unsupported RTF must still open as a placeholder without invoking the runtime reader.
        var unsupported = new ComponentInstance(RichTextBoxComponentClass.Instance, "r");
        unsupported.UnknownRawProperties.Add(new("TextRTF", ["TextRTF = \"{\\rtf1\\unknown1 x}\""]));
        RichTextBoxComponentClass.Instance.InstantiateForDesigner(unsupported).Should().BeOfType<Border>();
    }

    [AvaloniaFact]
    public void MaxLength_truncates_code_text_and_drops_insertion_at_the_limit()
    {
        Run("r.MaxLength = 5\nr.Text = \"1234567890\"\nDebug.Print r.Text\nr.SelStart = 5\nr.SelText = \"abc\"\nDebug.Print r.Text\nDebug.Print CStr(r.MaxLength)")
            .Should().Equal("12345", "12345", "5");
        var box = new VBRichTextBox();
        box.SetText("1234567890");
        Action reduce = () => RichTextBoxMembers.TrySet(box, "MaxLength", new Vb6Value(5));
        reduce.Should().Throw<NotImplementedException>().WithMessage("*reduction*#1100*");
        box.InputMaxLength.Should().Be(0);
        Action otherLimit = () => RichTextBoxMembers.TrySet(box, "MaxLength", new Vb6Value(-1));
        otherLimit.Should().Throw<NotImplementedException>().WithMessage("*measured*#1100*");
        box.SetText("12345");
        RichTextBoxMembers.TrySet(box, "MaxLength", new Vb6Value(5));
        box.Select(0, 1);
        Action replace = () => RichTextBoxMembers.TrySet(box, "SelText", new Vb6Value("x"));
        replace.Should().Throw<NotImplementedException>().WithMessage("*code selection*#1100*");
        box.Document.Text.Should().Be("12345");
    }

    [AvaloniaFact]
    public void Saved_MaxLength_also_limits_code_text()
    {
        Run("r.Text = \"1234567890\"\nDebug.Print r.Text", savedProperties: "MaxLength = 5")
            .Should().Equal("12345");
    }

    [AvaloniaFact]
    public void Locked_is_readable_and_does_not_stop_code_edits()
    {
        Run("Debug.Print r.Locked\nr.Locked = True\nr.Text = \"abc\"\nDebug.Print r.Locked\nDebug.Print r.Text\nr.Locked = False\nDebug.Print r.Locked")
            .Should().Equal("False", "True", "abc", "False");
        Run("Debug.Print r.Locked", savedProperties: "ReadOnly = -1").Should().Equal("True");
    }

    [AvaloniaFact]
    public void Protected_selection_reads_mixed_values_and_code_can_replace_it()
    {
        Run("r.Text = \"abc\"\nr.SelLength = 1\nr.SelProtected = True\nDebug.Print r.SelProtected\nr.SelLength = 3\nDebug.Print IsNull(r.SelProtected)\nr.SelLength = 1\nr.SelText = \"Q\"\nDebug.Print r.Text")
            .Should().Equal("True", "True", "Qbc");
    }

    [AvaloniaTheory]
    [InlineData("MultiLine", "True")]
    [InlineData("ScrollBars", "0")]
    public void Box_configuration_reads_and_rejects_runtime_writes(string member, string initial)
    {
        Run($"Debug.Print CStr(r.{member})\nOn Error Resume Next\nr.{member} = 0\nDebug.Print CStr(Err.Number)\nDebug.Print CStr(r.{member})")
            .Should().Equal(initial, "383", initial);
    }

    [AvaloniaFact]
    public void Saved_MaxLength_still_refuses_unmeasured_RTF_edits()
    {
        var instance = new ComponentInstance(RichTextBoxComponentClass.Instance, "r");
        instance.UnknownRawProperties.Add(new("MaxLength", ["MaxLength = 5"]));
        instance.UnknownRawProperties.Add(new("TextRTF", ["TextRTF = \"Hello\""]));
        var control = RichTextBoxComponentClass.Instance.Instantiate(instance);
        RichTextBoxMembers.TryGet(control, "Text", out var text);
        text.Value.Should().Be("Hello");
        foreach (var member in new[] { "TextRTF", "SelRTF" })
        {
            Action edit = () => RichTextBoxMembers.TrySet(control, member, new Vb6Value("x"));
            edit.Should().Throw<NotImplementedException>().WithMessage("*RTF editing with MaxLength*#1100*");
        }
    }

    [AvaloniaFact]
    public void Single_line_unmeasured_RTF_and_selection_paragraph_edits_refuse()
    {
        var instance = new ComponentInstance(RichTextBoxComponentClass.Instance, "r");
        instance.UnknownRawProperties.Add(new("MultiLine", ["MultiLine = 0"]));
        var control = RichTextBoxComponentClass.Instance.Instantiate(instance);
        Action rtf = () => RichTextBoxMembers.TrySet(control, "TextRTF", new Vb6Value(RtfDocument.FromText("a\r\nb").Write()));
        rtf.Should().Throw<NotImplementedException>().WithMessage("*single-line TextRTF*#1100*");
        Action selection = () => RichTextBoxMembers.TrySet(control, "SelText", new Vb6Value("a\r\nb"));
        selection.Should().Throw<NotImplementedException>().WithMessage("*single-line selection*#1100*");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saved_RTF_loads_and_MultiLine_false_truncates_both_load_and_code_text(bool inCompanion)
    {
        const string rtf = "{\\rtf1\\ansi{\\fonttbl{\\f0 Arial;}}\\f0\\fs20 Hello \\b bold\\b0\\par second line}";
        var bytes = System.Text.Encoding.ASCII.GetBytes(rtf);
        Run("Debug.Print r.Text\nDebug.Print CStr(r.SelBold)\nr.Text = \"a\" & vbCrLf & \"b\"\nDebug.Print r.Text",
            savedProperties: "MultiLine = 0\r\nReadOnly = -1\r\nTextRTF = "
                + (inCompanion ? "$\"Form1.frx\":0000" : "\"" + rtf + "\""),
            companion: inCompanion ? [.. BitConverter.GetBytes(bytes.Length), .. bytes] : null)
            .Should().Equal("Hello bold", "False", "a");
    }

    [AvaloniaFact]
    public void Form_runs_and_text_uses_the_measured_RTF_shape()
    {
        Run("r.Text = \"Hello\"\nDebug.Print r.Text\nDebug.Print Replace(Replace(r.TextRTF, vbCr, \"<CR>\"), vbLf, \"<LF>\")")
            .Should().Equal("Hello", "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 MS Sans Serif;}}<CR><LF>\\viewkind4\\uc1\\pard\\lang1033\\f0\\fs17 Hello<CR><LF>\\par }<CR><LF>");
    }

    [AvaloniaTheory]
    [InlineData("SelBold", "True", "True", "False")]
    [InlineData("SelItalic", "True", "True", "False")]
    [InlineData("SelUnderline", "True", "True", "False")]
    [InlineData("SelStrikeThru", "True", "True", "False")]
    [InlineData("SelFontName", "\"Courier New\"", "Courier New", "MS Sans Serif")]
    [InlineData("SelFontSize", "14", "14", "8.25")]
    public void Character_formatting_applies_only_to_the_selection_and_mixed_reads_Null(
        string member, string assignment, string selected, string unselected)
    {
        Run($"r.Text = \"The slow brown fox\"\nr.SelStart = 4\nr.SelLength = 4\nr.{member} = {assignment}\nDebug.Print CStr(r.{member})\nr.SelStart = 0\nr.SelLength = 10\nDebug.Print IsNull(r.{member})\nr.SelLength = 1\nDebug.Print CStr(r.{member})\nr.SelStart = 5\nDebug.Print CStr(r.{member})")
            .Should().Equal(selected, "True", unselected, selected);
    }

    [AvaloniaFact]
    public void Alignment_applies_to_paragraphs_and_a_mixed_selection_reads_Null()
    {
        Run("r.Text = \"a\" & vbCrLf & \"b\"\nr.SelLength = 1\nr.SelAlignment = 2\nDebug.Print CStr(r.SelAlignment)\n"
            + "r.SelStart = 3\nr.SelLength = 1\nDebug.Print CStr(r.SelAlignment)\nr.SelStart = 0\nr.SelLength = 4\nDebug.Print IsNull(r.SelAlignment)\n"
            + "r.SelAlignment = 1\nDebug.Print CStr(r.SelAlignment)\nr.SelAlignment = 0\nDebug.Print CStr(r.SelAlignment)")
            .Should().Equal("2", "0", "True", "1", "0");
    }

    [AvaloniaFact]
    public void Selection_replacement_and_ANSI_text_follow_the_measured_values()
    {
        Run("r.TextRTF = \"just text\"\nDebug.Print r.Text\nr.Text = \"The quick brown fox\"\nr.SelStart = 4\nr.SelLength = 5\nr.SelText = \"slow\"\nDebug.Print r.Text\nDebug.Print CStr(r.SelStart)\nDebug.Print CStr(r.SelLength)\nr.Text = \"é€א\"\nDebug.Print r.Text")
            .Should().Equal("just text", "The slow brown fox", "8", "0", "é€?");
    }

    [AvaloniaTheory]
    [InlineData("3")]
    [InlineData("4")]
    [InlineData("100")]
    [InlineData("-1")]
    [InlineData("Null")]
    public void Invalid_alignment_is_380_and_keeps_the_alignment(string value)
    {
        Run($"On Error Resume Next\nr.Text = \"abc\"\nr.SelAlignment = 2\nr.SelAlignment = {value}\nDebug.Print CStr(Err.Number)\nDebug.Print CStr(r.SelAlignment)")
            .Should().Equal("380", "2");
    }

    private const string FormattedRtf = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil Arial;}{\\f1\\fnil\\fcharset0 Courier New;}}\r\n{\\colortbl ;\\red255\\green0\\blue0;}\r\n\\viewkind4\\uc1\\pard\\lang1033\\f0\\fs20 The \\cf1\\ul\\b\\i\\f1\\fs28 slow\\cf0\\ulnone\\b0\\i0\\f0\\fs20  brown fox\r\n\\par }\r\n";

    private const string SelectionRtf = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 Courier New;}}\r\n{\\colortbl ;\\red255\\green0\\blue0;}\r\n\\uc1\\pard\\cf1\\lang1033\\ul\\b\\i\\f0\\fs28 slow}\r\n";

    [AvaloniaFact]
    public void Recorded_formatting_can_be_built_through_the_control_byte_for_byte()
    {
        var lines = Run("r.TextRTF = \"{\\rtf1\\ansi{\\fonttbl{\\f0 Arial;}}\\f0\\fs20 The quick brown fox}\"\n"
            + "r.Text = \"The quick brown fox\"\nr.SelStart = 4\nr.SelLength = 5\nr.SelText = \"slow\"\nr.SelStart = 4\nr.SelLength = 4\n"
            + "r.SelBold = True\nr.SelItalic = True\nr.SelUnderline = True\nr.SelColor = 255\nr.SelFontName = \"Courier New\"\nr.SelFontSize = 14\n"
            + "Debug.Print Replace(Replace(r.TextRTF, vbCr, \"<CR>\"), vbLf, \"<LF>\")\n"
            + "Debug.Print Replace(Replace(r.SelRTF, vbCr, \"<CR>\"), vbLf, \"<LF>\")");
        lines.Should().Equal(FormattedRtf.Replace("\r", "<CR>").Replace("\n", "<LF>"),
            "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 Courier New;}}<CR><LF>{\\colortbl ;\\red255\\green0\\blue0;}<CR><LF>\\uc1\\pard\\cf1\\lang1033\\ul\\b\\i\\f0\\fs28 slow}<CR><LF>");
    }

    [AvaloniaFact]
    public void SelRTF_replaces_text_and_formatting()
    {
        var selection = "\"" + SelectionRtf.Replace("\r\n", "\" & vbCrLf & \"") + "\"";
        Run("r.TextRTF = \"{\\rtf1\\ansi{\\fonttbl{\\f0 Arial;}}\\f0\\fs20 The quick brown fox}\"\n"
            + $"r.SelStart = 4\nr.SelLength = 5\nr.SelRTF = {selection}\nDebug.Print r.Text\n"
            + "Debug.Print Replace(Replace(r.TextRTF, vbCr, \"<CR>\"), vbLf, \"<LF>\")")
            .Should().Equal("The slow brown fox", FormattedRtf.Replace("\r", "<CR>").Replace("\n", "<LF>"));
    }

    [AvaloniaFact]
    public void Measured_selection_limits_and_errors_are_kept()
    {
        Run("On Error Resume Next\nr.Text = \"The quick brown fox\"\nr.SelStart = 100\nDebug.Print CStr(r.SelStart)\n"
            + "r.SelStart = -1\nDebug.Print CStr(Err.Number)\nErr.Clear\nr.SelStart = 4\nr.SelLength = 100\nDebug.Print CStr(r.SelLength)\n"
            + "r.SelLength = -1\nDebug.Print CStr(Err.Number)")
            .Should().Equal("19", "380", "17", "380");
    }

    [AvaloniaFact]
    public void Measured_character_coercions_are_kept()
    {
        Run("r.Text = \"abc\"\nr.SelLength = 3\nr.SelBold = Null\nDebug.Print r.SelBold\n"
            + "r.SelBold = False\nr.SelBold = 5\nDebug.Print r.SelBold\nr.SelFontSize = 14\nr.SelFontSize = 0\nr.SelFontSize = -5\nDebug.Print CStr(r.SelFontSize)\n"
            + "r.SelFontSize = 2000\nDebug.Print CStr(r.SelFontSize)\nr.SelFontName = \"No Such Font\"\nDebug.Print r.SelFontName")
            .Should().Equal("True", "True", "14", "1638.35", "No Such Font");
    }

    [AvaloniaFact]
    public void Code_edits_raise_the_measured_events_in_order()
    {
        Run("collecting = True\nr.Text = \"abc\"\nr.SelStart = 1\nr.SelStart = 1\nr.SelLength = 1\nr.SelBold = True\nr.SelText = \"Q\"",
            "Private collecting As Boolean\nPrivate Sub r_Change()\nIf collecting Then Debug.Print \"Change\"\nEnd Sub\n"
            + "Private Sub r_SelChange()\nIf collecting Then Debug.Print \"SelChange\"\nEnd Sub\n")
            .Should().Equal("SelChange", "Change", "SelChange", "SelChange", "Change", "SelChange", "Change");
    }

    [AvaloniaTheory]
    [InlineData("SelCharOffset")]
    [InlineData("SelIndent")]
    [InlineData("SelHangingIndent")]
    [InlineData("SelRightIndent")]
    [InlineData("SelBullet")]
    [InlineData("BulletIndent")]
    [InlineData("SelTabCount")]
    [InlineData("SelTabs")]
    [InlineData("HideSelection")]
    [InlineData("DisableNoScroll")]
    [InlineData("RightMargin")]
    [InlineData("FileName")]
    [InlineData("BackColor")]
    [InlineData("Font")]
    [InlineData("Hwnd")]
    [InlineData("MousePointer")]
    [InlineData("MouseIcon")]
    [InlineData("AutoVerbMenu")]
    [InlineData("OLEObjects")]
    [InlineData("OLEDragMode")]
    [InlineData("OLEDropMode")]
    public void Remaining_properties_refuse_instead_of_falling_through_to_shared_bindings(string name)
    {
        FormDeserializer.TryGetComponentClass("RichTextLib.RichTextBox", out var type).Should().BeTrue();
        var control = type!.Instantiate(new ComponentInstance(type, "r"));
        Action read = () => RichTextBoxMembers.TryGet(control, name, out _);
        Action write = () => RichTextBoxMembers.TrySet(control, name, new Vb6Value(1));
        read.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
        write.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
    }

    [AvaloniaTheory]
    [InlineData("Appearance")]
    [InlineData("BorderStyle")]
    public void Appearance_and_border_read_saved_values_and_refuse_runtime_assignment_with_383(string member)
    {
        Run($"Debug.Print CStr(r.{member})\nOn Error Resume Next\nr.{member} = 0\nDebug.Print CStr(Err.Number)\nDebug.Print CStr(r.{member})")
            .Should().Equal("1", "383", "1");
        Run($"Debug.Print CStr(r.{member})", savedProperties: $"{member} = 0").Should().Equal("0");
    }

    [AvaloniaFact]
    public void Enabled_reads_the_saved_value_and_can_be_changed_at_runtime()
    {
        Run("Debug.Print r.Enabled\nr.Enabled = True\nDebug.Print r.Enabled", savedProperties: "Enabled = 0")
            .Should().Equal("False", "True");
    }

    [AvaloniaTheory]
    [InlineData("Find")]
    [InlineData("Span")]
    [InlineData("UpTo")]
    [InlineData("GetLineFromChar")]
    [InlineData("LoadFile")]
    [InlineData("SaveFile")]
    [InlineData("SelPrint")]
    [InlineData("Refresh")]
    [InlineData("OLEDrag")]
    public void Remaining_methods_refuse(string name)
    {
        FormDeserializer.TryGetComponentClass("RichTextLib.RichTextBox", out var type).Should().BeTrue();
        var control = type!.Instantiate(new ComponentInstance(type, "r"));
        Action call = () => control.Call(name, []);
        call.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
    }

    [AvaloniaTheory]
    [InlineData("SelItalic", null)]
    [InlineData("SelUnderline", null)]
    [InlineData("SelStrikeThru", null)]
    [InlineData("SelStart", null)]
    [InlineData("SelFontSize", "14")]
    [InlineData("Text", 14)]
    [InlineData("SelAlignment", 1.5)]
    public void Unmeasured_property_conversions_refuse(string name, object? input)
    {
        FormDeserializer.TryGetComponentClass("RichTextLib.RichTextBox", out var type).Should().BeTrue();
        var control = type!.Instantiate(new ComponentInstance(type, "r"));
        var value = input switch
        {
            string text => new Vb6Value(text),
            int number => new Vb6Value(number),
            double number => new Vb6Value(number),
            _ => Vb6Value.Null
        };
        Action write = () => RichTextBoxMembers.TrySet(control, name, value);
        write.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
    }

    [AvaloniaFact]
    public void Fontless_SelRTF_refuses_the_unmeasured_insertion_format_conversion()
    {
        FormDeserializer.TryGetComponentClass("RichTextLib.RichTextBox", out var type).Should().BeTrue();
        var control = type!.Instantiate(new ComponentInstance(type, "r"));
        RichTextBoxMembers.TrySet(control, "TextRTF", new Vb6Value(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs20 The quick brown fox}"));
        RichTextBoxMembers.TrySet(control, "SelLength", new Vb6Value(3));
        Action replace = () => RichTextBoxMembers.TrySet(control, "SelRTF",
            new Vb6Value(@"{\rtf1{\colortbl;\red0\green0\blue255;}\cf1 BLUE}"));
        replace.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
    }

    [AvaloniaTheory]
    [InlineData("Bad;Name")]
    [InlineData("é")]
    [InlineData("No Such Font")]
    public void Font_table_names_need_measured_encoding_or_face_metadata(string name)
    {
        FormDeserializer.TryGetComponentClass("RichTextLib.RichTextBox", out var type).Should().BeTrue();
        var control = type!.Instantiate(new ComponentInstance(type, "r"));
        RichTextBoxMembers.TrySet(control, "Text", new Vb6Value("Hello"));
        RichTextBoxMembers.TrySet(control, "SelLength", new Vb6Value(5));
        RichTextBoxMembers.TrySet(control, "SelFontName", new Vb6Value(name));
        Action write = () => RichTextBoxMembers.TryGet(control, "TextRTF", out _);
        write.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
    }

    [AvaloniaFact]
    public void Font_table_ids_remain_unique_when_adding_a_face()
    {
        // Model invariant: no new VB6 font-table normalisation is pinned here.
        FormDeserializer.TryGetComponentClass("RichTextLib.RichTextBox", out var type).Should().BeTrue();
        var control = type!.Instantiate(new ComponentInstance(type, "r"));
        RichTextBoxMembers.TrySet(control, "TextRTF", new Vb6Value(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}{\f2\fnil\fcharset0 MS Sans Serif;}}\f0\fs20 Hello}"));
        RichTextBoxMembers.TrySet(control, "SelLength", new Vb6Value(5));
        RichTextBoxMembers.TrySet(control, "SelFontName", new Vb6Value("Courier New"));
        RichTextBoxMembers.TryGet(control, "TextRTF", out var written);
        var read = RtfDocument.Read((string)written.Value!);
        read.Text.Should().Be("Hello");
        read.Runs.Single().Format.FontName.Should().Be("Courier New");
    }

    [AvaloniaFact]
    public void Paragraph_SelRTF_refuses_unmeasured_paragraph_and_bullet_font_remapping()
    {
        FormDeserializer.TryGetComponentClass("RichTextLib.RichTextBox", out var type).Should().BeTrue();
        var control = type!.Instantiate(new ComponentInstance(type, "r"));
        Action replace = () => RichTextBoxMembers.TrySet(control, "SelRTF",
            new Vb6Value(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs20 \qc first\par second}"));
        replace.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
    }

    [AvaloniaFact]
    public void Caret_formatting_RTF_output_refuses_unmeasured_insertion_format_serialization()
    {
        FormDeserializer.TryGetComponentClass("RichTextLib.RichTextBox", out var type).Should().BeTrue();
        var control = type!.Instantiate(new ComponentInstance(type, "r"));
        RichTextBoxMembers.TrySet(control, "Text", new Vb6Value("Hello"));
        RichTextBoxMembers.TrySet(control, "SelBold", new Vb6Value(true));
        Action write = () => RichTextBoxMembers.TryGet(control, "TextRTF", out _);
        write.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
    }

    [AvaloniaFact]
    public void SelColor_reads_Null_for_a_mix_and_refuses_the_unrepresented_scalar_subtype()
    {
        FormDeserializer.TryGetComponentClass("RichTextLib.RichTextBox", out var type).Should().BeTrue();
        var control = type!.Instantiate(new ComponentInstance(type, "r"));
        RichTextBoxMembers.TrySet(control, "Text", new Vb6Value("The slow brown fox"));
        RichTextBoxMembers.TrySet(control, "SelStart", new Vb6Value(4));
        RichTextBoxMembers.TrySet(control, "SelLength", new Vb6Value(4));
        RichTextBoxMembers.TrySet(control, "SelColor", new Vb6Value(255));
        RichTextBoxMembers.TrySet(control, "SelStart", new Vb6Value(0));
        RichTextBoxMembers.TrySet(control, "SelLength", new Vb6Value(10));
        RichTextBoxMembers.TryGet(control, "SelColor", out var mixed);
        mixed.IsNull.Should().BeTrue();
        RichTextBoxMembers.TrySet(control, "SelStart", new Vb6Value(5));
        Action read = () => RichTextBoxMembers.TryGet(control, "SelColor", out _);
        read.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
    }

    [AvaloniaTheory]
    [InlineData(-1, 0)]
    [InlineData(-2147483640, 0)]
    [InlineData(16777216, 16777216)]
    public void SelColor_assignments_keep_the_measured_model_values(int input, int expected)
    {
        FormDeserializer.TryGetComponentClass("RichTextLib.RichTextBox", out var type).Should().BeTrue();
        var control = type!.Instantiate(new ComponentInstance(type, "r"));
        RichTextBoxMembers.TrySet(control, "Text", new Vb6Value("Hello"));
        RichTextBoxMembers.TrySet(control, "SelLength", new Vb6Value(5));
        RichTextBoxMembers.TrySet(control, "SelColor", new Vb6Value(input));
        ((VBRichTextBox)control).Document.Runs.Single().Format.Color.Should().Be(expected);
        if (expected > 0xffffff)
        {
            Action write = () => RichTextBoxMembers.TryGet(control, "TextRTF", out _);
            write.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
        }
    }

    [AvaloniaFact]
    public void Default_member_is_TextRTF()
    {
        Run("r = \"via default\"\nDebug.Print r.Text\nDim v\nv = r\nDebug.Print v = r.TextRTF\nSet v = r\nv = 5\nDebug.Print TypeName(v)\nDebug.Print r.Text")
            .Should().Equal("via default", "True", "Integer", "via default");
    }
}

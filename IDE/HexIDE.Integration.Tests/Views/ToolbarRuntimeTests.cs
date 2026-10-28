using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.Interpreter;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

// Measured on vb6.exe SP6, native and p-code: #1088, issuecomment-6428845889,
// docs/vb6-fidelity-oracle/in-box-controls-the-toolbar.md. Runtime object model; drawing and persistence are separate slices.
public class ToolbarRuntimeTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 4500
           ClientWidth = 6000
           Begin MSComctlLib.Toolbar Toolbar1
              Height = 660
              Width = 6000
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<string[]> Run(string code, string? form = null)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(form ?? Form);
        var printed = await ContainerRuntimeHarness.Run(ctx, env, "On Error Resume Next\n" + code + "\n");
        return printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
    }

    [AvaloniaFact]
    public async Task Designed_buttons_and_menus_load_into_the_runtime()
    {
        var configured = Form.Replace("Height = 660", """
            Height = 660
            ToolTips = 0
            Style = 1
            ButtonWidth = 1005
            ButtonHeight = 1005
            BeginProperty Buttons
               NumButtons = 2
               BeginProperty Button1
                  Caption = "Menu"
                  Key = "drop"
                  Description = "Makes one"
                  Object.ToolTipText = "New file"
                  Object.Tag = "t1"
                  ImageIndex = 3
                  Style = 5
                  BeginProperty ButtonMenus
                     NumButtonMenus = 1
                     BeginProperty ButtonMenu1
                        Enabled = 0
                        Object.Visible = 0
                        Key = "m2"
                        Object.Tag = "mt"
                        Text = "Second"
                     EndProperty
                  EndProperty
               EndProperty
               BeginProperty Button2
                  Key = "ph"
                  Style = 4
                  Object.Width = 1500
               EndProperty
            EndProperty
            """, StringComparison.Ordinal);
        (await Run("""
            Dim b As Object, m As Object
            Set b = Toolbar1.Buttons("drop")
            Set m = b.ButtonMenus(1)
            Debug.Print Toolbar1.Buttons.Count, Toolbar1.ShowTips, Toolbar1.Style
            Debug.Print b.Caption, b.Description, b.ToolTipText, b.Tag, b.Image, TypeName(b.Image), b.Style
            Debug.Print m.Text, m.Key, m.Tag, m.Enabled, m.Visible, m.Parent.Key
            Debug.Print Toolbar1.Buttons("ph").Width
            b.Image = 0
            Debug.Print b.Image
            """, configured)).Should().Equal("2", "False", "1", "Menu", "Makes one", "New file", "t1", "3", "Integer", "5",
                "Second", "m2", "mt", "False", "False", "drop", "1500", "0");
    }

    [AvaloniaFact]
    public async Task Vb6s_saved_toolbar_buttons_are_executable()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.Directory.Exists(System.IO.Path.Join(directory.FullName, "corpus")))
            directory = directory.Parent;
        var source = System.IO.File.ReadAllText(System.IO.Path.Join(directory!.FullName,
            "corpus", "inbox-controls", "saved", "toolbar", "Form1.frm"));
        var definition = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new NullSink())!;
        // Exercise the button bag alone; ToolbarImageListTests covers the full saved form binding.
        var block = definition.UnknownChildSubtreeTexts.Single(t => t.Contains("Toolbar Toolbar1", StringComparison.Ordinal))
            .ReplaceLineEndings("\n")
            .Replace("      ImageList       =   \"ImageList1\"\n", "", StringComparison.Ordinal);
        var form = "VERSION 5.00\r\nBegin VB.Form Form1\r\n" + block + "\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n";
        (await Run("""
            Debug.Print Toolbar1.Buttons.Count, Toolbar1.Buttons("new").Caption
            Debug.Print Toolbar1.Buttons("bold").Value, Toolbar1.Buttons("right").Value
            Debug.Print Toolbar1.Buttons("ph").Width, Toolbar1.Buttons("drop").ButtonMenus.Count
            Debug.Print Toolbar1.Buttons(8).MixedState, Toolbar1.Buttons(8).Enabled, Toolbar1.Buttons(9).Visible
            """, form)).Should().Equal("9", "New", "1", "1", "1500", "2", "True", "False", "False");
    }

    [AvaloniaFact]
    public async Task Designed_scalar_properties_use_the_measured_file_names()
    {
        var form = Form.Replace("Height = 660", """
            Height = 660
            Align = 2
            ToolTips = 0
            AllowCustomize = 0
            Wrappable = 0
            Appearance = 1
            HelpContextID = 7
            Style = 1
            TextAlignment = 1
            _Version = 393216
            BorderStyle = 1
            """, StringComparison.Ordinal);
        (await Run("""
            Debug.Print Toolbar1.ShowTips, Toolbar1.AllowCustomize, Toolbar1.Wrappable, Toolbar1.Appearance
            Debug.Print Toolbar1.HelpContextID, Toolbar1.Style, Toolbar1.TextAlignment, Toolbar1.BorderStyle, Toolbar1.Align
            """, form)).Should().Equal("False", "False", "False", "1", "7", "1", "1", "1", "2");
    }

    [AvaloniaFact]
    public async Task The_unread_ShowTips_file_name_is_ignored()
    {
        (await Run("Debug.Print Toolbar1.ShowTips", Form.Replace("Height = 660", "Height = 660\nShowTips = 0", StringComparison.Ordinal)))
            .Should().Equal("True");
    }

    [AvaloniaFact]
    public async Task The_empty_bars_defaults_and_types_are_measured()
    {
        (await Run("""
            Debug.Print TypeName(Toolbar1), Toolbar1.Appearance, Toolbar1.BorderStyle, Toolbar1.Style, Toolbar1.TextAlignment
            Debug.Print TypeName(Toolbar1.Style), Toolbar1.AllowCustomize, Toolbar1.Enabled, Toolbar1.ShowTips, Toolbar1.Wrappable
            Debug.Print Toolbar1.HelpContextID, Toolbar1.MousePointer, Toolbar1.OLEDropMode, Toolbar1.HelpFile
            Debug.Print Toolbar1.Align, TypeName(Toolbar1.Align), Toolbar1.Height
            Debug.Print TypeName(Toolbar1.ButtonWidth), TypeName(Toolbar1.ButtonHeight)
            """)).Should().Equal("Toolbar", "1", "0", "0", "0", "Long", "True", "True", "True", "True",
                "0", "0", "0", "", "1", "Integer", "660", "Single", "Single");
    }

    [AvaloniaTheory]
    [InlineData("Style", "1", "0", "1")]
    [InlineData("Style", "2", "380", "0")]
    [InlineData("TextAlignment", "1", "0", "1")]
    [InlineData("TextAlignment", "2", "380", "0")]
    [InlineData("Wrappable", "False", "0", "False")]
    [InlineData("BorderStyle", "1", "0", "1")]
    public async Task Measured_assignments_preserve_the_value_on_error(string property, string value, string error, string expected)
    {
        (await Run($"Toolbar1.{property} = {value}\nDebug.Print Err.Number, Toolbar1.{property}"))
            .Should().Equal(error, expected);
    }

    [AvaloniaTheory]
    [InlineData("ButtonWidth", "1000")]
    [InlineData("ButtonWidth", "10")]
    [InlineData("ButtonWidth", "0")]
    [InlineData("ButtonWidth", "-5")]
    [InlineData("ButtonHeight", "800")]
    [InlineData("Height", "2000")]
    public async Task Size_assignments_raise_nothing_and_change_nothing(string property, string value)
    {
        (await Run($"Dim before\nbefore = Toolbar1.{property}\nToolbar1.{property} = {value}\nDebug.Print Err.Number, Toolbar1.{property} = before"))
            .Should().Equal("0", "True");
    }

    [AvaloniaFact]
    public async Task Right_aligned_text_changes_button_height()
    {
        (await Run("Toolbar1.TextAlignment = 1\nDebug.Print Abs(Toolbar1.ButtonHeight - 329.9528) < 0.001"))
            .Should().Equal("True");
    }

    [AvaloniaFact]
    public async Task Empty_button_dimensions_are_the_measured_singles()
    {
        (await Run("Debug.Print Abs(Toolbar1.ButtonWidth - 345.2604) < 0.001, Abs(Toolbar1.ButtonHeight - 569.7643) < 0.001"))
            .Should().Equal("True", "True");
    }

    [AvaloniaTheory]
    [InlineData("Font")]
    [InlineData("TabStop")]
    public async Task Missing_members_raise_the_measured_late_bound_error(string member)
    {
        (await Run($"Dim bar As Object\nSet bar = Toolbar1\nDebug.Print bar.{member}\nDebug.Print Err.Number"))
            .Should().Equal("438");
    }

    [AvaloniaTheory]
    [InlineData("Controls")]
    [InlineData("MouseIcon")]
    [InlineData("hWnd")]
    public async Task Pending_members_refuse_instead_of_returning_a_guessed_value(string member)
    {
        var act = () => Run($"Debug.Print Toolbar1.{member}");
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage($"The Toolbar's {member} is not implemented (#1088)");
    }

    [AvaloniaTheory]
    [InlineData("Toolbar1.Customize")]
    [InlineData("Toolbar1.Align = 0")]
    public async Task Pending_assignments_indexing_and_methods_refuse(string code)
    {
        var act = () => Run(code);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*Toolbar*not implemented*");
    }

    [AvaloniaFact]
    public async Task An_incomplete_button_bag_does_not_silently_run_as_an_empty_one()
    {
        (await Run("Debug.Print TypeName(Toolbar1)")).Should().Equal("Toolbar");
        var configured = Form.Replace("Height = 660", "Height = 660\n              BeginProperty Buttons\n                 NumButtons = 1\n              EndProperty", StringComparison.Ordinal);
        var definition = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            configured, new NullSink())!;
        var block = definition.UnknownChildSubtreeTexts.Single();
        new FormSerializer().Serialize(definition, "Form1.frm").Item1.ReplaceLineEndings()
            .Should().Contain(block.ReplaceLineEndings());
        var act = () => Run("", configured);
        await act.Should().ThrowAsync<NotImplementedException>()
            .WithMessage("*Toolbar*not implemented*1088*");
    }

    [AvaloniaTheory]
    [InlineData("Style = \"1\"")]
    [InlineData("Enabled = 0")]
    [InlineData("HelpFile = 42")]
    [InlineData("Height = \"660\"")]
    [InlineData("Appearance = 0")]
    [InlineData("BorderStyle = 2")]
    [InlineData("MousePointer = 99")]
    [InlineData("OLEDropMode = 1")]
    public async Task Unmeasured_property_values_and_conversions_are_refused(string assignment)
    {
        var act = () => Run($"Toolbar1.{assignment}");
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*Toolbar property conversions*");
    }

    [AvaloniaFact]
    public async Task The_registration_retains_its_library_identity()
    {
        (await Run("Debug.Print TypeOf Toolbar1 Is MSComctlLib.Toolbar, TypeOf Toolbar1 Is Control, TypeOf Toolbar1 Is Object"))
            .Should().Equal("True", "True", "True");
    }

    [AvaloniaFact]
    public async Task Button_and_menu_defaults_and_default_members_are_measured()
    {
        (await Run("""
            Dim b As Object, m As Object, v
            Debug.Print TypeName(Toolbar1.Buttons), TypeName(Toolbar1.Buttons.Count), Toolbar1.Buttons.Count
            Set b = Toolbar1.Buttons.Add(, "a", "Alpha")
            Debug.Print TypeName(b), b.Index, TypeName(b.Index), b.Caption, b.Key
            Debug.Print b.Tag, b.ToolTipText, b.Description, b.Enabled, b.Visible, b.MixedState
            Debug.Print b.Style, TypeName(b.Style), b.Value, TypeName(b.Value), b.Image, TypeName(b.Image)
            v = b
            Debug.Print v, TypeName(b.ButtonMenus), b.ButtonMenus.Count, Toolbar1.Height
            Set m = b.ButtonMenus.Add(, "m1", "Menu one")
            Debug.Print TypeName(m), m.Index, m.Key, m.Text, m.Enabled, m.Visible, m.Tag
            v = m
            Debug.Print v, m.Parent Is b
            """)).Should().Equal("IButtons", "Integer", "0", "IButton", "1", "Integer", "Alpha", "a",
                "", "", "", "True", "True", "False", "0", "Long", "0", "Long", "0", "Integer",
                "Alpha", "IButtonMenus", "0", "630", "IButtonMenu", "1", "m1", "Menu one", "True", "True", "", "Menu one", "True");
    }

    [AvaloniaFact]
    public async Task Collections_insert_lookup_remove_clear_and_round_numeric_indexes()
    {
        (await Run("""
            Dim c As Object, b As Object
            Set c = Toolbar1.Buttons
            Set b = c.Add(, "a", "Alpha")
            Set b = c.Add(1, "b", "Beta")
            Set b = c.Add
            Debug.Print c.Count, c(1).Caption, c("a").Index, c(1.5).Caption, c(2.5).Caption, b.Caption
            c.Remove "a"
            Debug.Print c.Count, b.Index
            c.Clear
            Debug.Print c.Count, Toolbar1.Height
            """)).Should().Equal("3", "Beta", "2", "Alpha", "Alpha", "", "2", "2", "0", "630");
    }

    [AvaloniaTheory]
    [InlineData("c.Add , \"a\"", "35602")]
    [InlineData("c.Add , \"5\"", "35603")]
    [InlineData("c.Add 0", "35600")]
    [InlineData("c.Add 99", "35600")]
    [InlineData("Debug.Print c(0)", "35600")]
    [InlineData("Debug.Print c(99)", "35600")]
    [InlineData("Debug.Print c(\"missing\")", "35601")]
    [InlineData("Debug.Print c(\"5\")", "35601")]
    [InlineData("Debug.Print c(\"\")", "35603")]
    [InlineData("c.Count = 8", "383")]
    public async Task Both_collections_raise_the_measured_errors(string operation, string error)
    {
        foreach (var collection in new[] { "Toolbar1.Buttons", "b.ButtonMenus" })
            (await Run($"Dim b As Object, c As Object, x As Object\nSet b = Toolbar1.Buttons.Add(, \"button\")\nSet c = {collection}\nSet x = c.Add(, \"a\")\n{operation}\nDebug.Print Err.Number"))
                .Should().Equal(error);
    }

    [AvaloniaTheory]
    [InlineData("Index = 2", "383")]
    [InlineData("Left = 2", "383")]
    [InlineData("Top = 2", "383")]
    [InlineData("Height = 2", "383")]
    [InlineData("Width = 2", "383")]
    [InlineData("Style = 6", "380")]
    [InlineData("Value = 2", "380")]
    [InlineData("Image = 1", "35613")]
    public async Task Button_assignments_raise_measured_errors(string assignment, string error)
    {
        (await Run($"Dim b As Object\nSet b = Toolbar1.Buttons.Add\nb.{assignment}\nDebug.Print Err.Number"))
            .Should().Equal(error);
    }

    [AvaloniaFact]
    public async Task Button_properties_can_be_changed_without_drawing()
    {
        (await Run("""
            Dim b As Object, m As Object
            Set b = Toolbar1.Buttons.Add
            b.Caption = 42
            b.Key = "key"
            b.Tag = "tag"
            b.Description = "description"
            b.ToolTipText = "tip"
            b.MixedState = True
            b.Enabled = False
            b.Visible = False
            b.Style = 4
            b.Width = 1500
            Debug.Print b.Caption, TypeName(b.Caption), b.Key, b.Tag, b.Description, b.ToolTipText
            Debug.Print b.MixedState, b.Enabled, b.Visible, b.Width, b.Height
            b.Visible = True
            Debug.Print b.Width
            Set m = b.ButtonMenus.Add
            m.Text = "text"
            m.Key = ""
            m.Tag = "mt"
            m.Enabled = False
            m.Visible = False
            Debug.Print m.Text, m.Key, m.Tag, m.Enabled, m.Visible
            """)).Should().Equal("42", "String", "key", "tag", "description", "tip", "True", "False", "False", "0", "0", "1500",
                "text", "", "mt", "False", "False");
    }

    [AvaloniaFact]
    public async Task Group_value_assignment_clears_the_previous_button_and_allows_none()
    {
        (await Run("""
            Dim a As Object, b As Object
            Set a = Toolbar1.Buttons.Add(, "a", , 2)
            Set b = Toolbar1.Buttons.Add(, "b", , 2)
            a.Value = 1
            b.Value = 1
            Debug.Print a.Value, b.Value
            b.Value = 0
            Debug.Print a.Value, b.Value
            """)).Should().Equal("0", "1", "0", "0");
    }

    [AvaloniaFact]
    public async Task Default_member_writes_and_object_assignment_errors_are_measured()
    {
        (await Run("""
            Dim b As Object, c As Object, m As Object
            Set b = Toolbar1.Buttons.Add(, "a", "Alpha")
            Toolbar1.Buttons(1) = "first"
            Debug.Print b.Caption
            Set c = Toolbar1.Buttons
            c(1) = "second"
            Debug.Print b.Caption
            b = "third"
            Debug.Print b.Caption
            Set m = b.ButtonMenus.Add
            b.ButtonMenus(1) = "menu"
            Debug.Print m.Text
            Set b.Tag = Toolbar1
            Debug.Print Err.Number
            Err.Clear
            Set Toolbar1.Buttons = Nothing
            Debug.Print Err.Number
            """)).Should().Equal("first", "second", "third", "menu", "35700", "383");
    }

    [AvaloniaTheory]
    [InlineData("Set b = Toolbar1.Buttons.Add(, , , 6)", "380")]
    [InlineData("Set b = Toolbar1.Buttons.Add(, , , , 1)", "35613")]
    public async Task Add_rejects_invalid_styles_and_images_without_an_image_list(string operation, string error)
    {
        (await Run($"Dim b As Object\n{operation}\nDebug.Print Err.Number, Toolbar1.Buttons.Count"))
            .Should().Equal(error, "0");
    }

    [AvaloniaFact]
    public async Task Unmeasured_boolean_conversion_refuses()
    {
        var act = () => Run("Dim b As Object\nSet b = Toolbar1.Buttons.Add\nb.Enabled = 0");
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("Toolbar item conversions or geometry are not implemented (#1088)");
    }

    [AvaloniaFact]
    public async Task Menu_collection_insert_remove_and_clear_keep_indexes_current()
    {
        (await Run("""
            Dim b As Object, c As Object, a As Object, m As Object
            Set b = Toolbar1.Buttons.Add
            Set c = b.ButtonMenus
            Set a = c.Add(, "a", "Alpha")
            Set m = c.Add(1, "m", "Menu")
            Debug.Print c.Count, a.Index, c("m").Text
            c.Remove 1
            Debug.Print c.Count, a.Index
            c.Clear
            Debug.Print c.Count
            """)).Should().Equal("2", "2", "Menu", "1", "1", "0");
    }

    [AvaloniaFact]
    public async Task Invalid_button_values_and_duplicate_keys_leave_properties_unchanged()
    {
        (await Run("""
            Dim a As Object, b As Object
            Set a = Toolbar1.Buttons.Add(, "a", , 1)
            Set b = Toolbar1.Buttons.Add(, "b")
            a.Value = 1
            a.Value = 2
            Debug.Print Err.Number, a.Value
            Err.Clear
            a.Style = 6
            Debug.Print Err.Number, a.Style
            Err.Clear
            a.Key = "b"
            Debug.Print Err.Number, a.Key
            """)).Should().Equal("380", "1", "380", "1", "35602", "a");
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Every_measured_style_can_be_added_and_assigned(int style)
    {
        (await Run($"Dim b As Object\nSet b = Toolbar1.Buttons.Add(, , , {style})\nDebug.Print b.Style\nb.Style = 0\nb.Style = {style}\nDebug.Print Err.Number, b.Style"))
            .Should().Equal(style.ToString(CultureInfo.InvariantCulture), "0", style.ToString(CultureInfo.InvariantCulture));
    }

    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 120)]
    [InlineData(4, 0)]
    [InlineData(5, 195.0237)]
    public async Task Measured_button_styles_have_their_measured_geometry(int style, double width)
    {
        (await Run($"""
            Dim a As Object, b As Object
            Set a = Toolbar1.Buttons.Add(, "a", "Alpha")
            Set b = Toolbar1.Buttons.Add(, "b", "Alpha", {style})
            Debug.Print Abs(a.Left) < 0.001, Abs(a.Top - 30) < 0.001
            Debug.Print Abs(b.Left - a.Width) < 0.001, Abs(b.Width - {(style is 3 or 4 ? "0" : "a.Width")} - {width.ToString(CultureInfo.InvariantCulture)}) < 0.001
            Debug.Print Abs(a.Height - 570) < 0.001, TypeName(b.Width), TypeName(b.Left)
            Debug.Print Abs(Toolbar1.ButtonWidth - a.Width) < 0.001, Abs(Toolbar1.ButtonHeight - 570) < 0.001, Toolbar1.Height
            """)).Should().Equal("True", "True", "True", "True", "True", "Single", "Single", "True", "True", "630");
    }

    [AvaloniaFact]
    public async Task Placeholder_resize_visibility_and_collection_edits_reposition_following_buttons()
    {
        (await Run("""
            Dim a As Object, p As Object, b As Object
            Set a = Toolbar1.Buttons.Add(, "a", "Alpha")
            Set p = Toolbar1.Buttons.Add(, "p", , 4)
            Set b = Toolbar1.Buttons.Add(, "b", "Alpha")
            p.Width = 1500
            Debug.Print Abs(b.Left - a.Width - 1500) < 0.001
            p.Visible = False
            Debug.Print Abs(b.Left - a.Width) < 0.001, p.Width, p.Height
            p.Visible = True
            Debug.Print Abs(b.Left - a.Width - 1500) < 0.001
            a.Style = 3
            Debug.Print Abs(b.Left - 120 - 1500) < 0.001
            Toolbar1.Buttons.Remove "p"
            Debug.Print Abs(b.Left - 120) < 0.001
            Set p = Toolbar1.Buttons.Add(1, "p", , 4)
            p.Width = 1500
            Debug.Print Abs(b.Left - 120 - 1500) < 0.001
            Toolbar1.Buttons.Clear
            Debug.Print Toolbar1.Height
            """)).Should().Equal("True", "True", "0", "0", "True", "True", "True", "True", "630");
    }

    [AvaloniaFact]
    public async Task Populated_size_writes_leave_measured_dimensions_unchanged()
    {
        (await Run("""
            Dim b As Object
            Set b = Toolbar1.Buttons.Add(, , "Alpha")
            Dim originalWidth As Single
            originalWidth = b.Width
            Toolbar1.ButtonWidth = -5
            Toolbar1.ButtonHeight = 800
            Toolbar1.Height = 2000
            Debug.Print Err.Number, Abs(Toolbar1.ButtonWidth - originalWidth) < 0.001, Abs(Toolbar1.ButtonHeight - 570) < 0.001, Toolbar1.Height
            Toolbar1.TextAlignment = 1
            Debug.Print Abs(Toolbar1.ButtonHeight - 330) < 0.001
            """)).Should().Equal("0", "True", "True", "630", "True");
    }

    [AvaloniaTheory]
    [InlineData("b.Visible = False", "b.Left")]
    [InlineData("b.Visible = False", "b.Top")]
    public async Task Measured_layout_refuses_edges_without_a_sizing_measurement(string change, string read)
    {
        (await Run("Dim b As Object\nSet b = Toolbar1.Buttons.Add(, , \"Alpha\")\nDebug.Print b.Width > 0"))
            .Should().Equal("True");
        var act = () => Run($"Dim b As Object, p As Object\nSet b = Toolbar1.Buttons.Add(, , \"Alpha\")\n{change}\nDebug.Print {read}");
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("Toolbar item conversions or geometry are not implemented (#1088)");
    }

}

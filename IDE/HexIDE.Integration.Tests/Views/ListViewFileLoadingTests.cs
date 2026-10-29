using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

// File defaults and widths measured in in-box-controls-the-listview.md, group 3 (#1085).
public class ListViewFileLoadingTests
{
    private static VBListView Load(string properties)
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn($"""
            VERSION 5.00
            Begin VB.Form Form1
               ClientHeight = 4500
               ClientWidth = 6000
               Begin MSComctlLib.ListView ListView1
                  Height = 2500
                  Width = 5000
                  Appearance = 0
            {properties}
               End
            End
            Attribute VB_Name = "Form1"
            """);
        return canvas.Children.OfType<VBListView>().Single();
    }

    private static object? Read(ICSharpPropertyBag bag, string name)
    {
        bag.TryGetProperty(name, out var value).Should().BeTrue();
        return value.Value;
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("BorderStyle = 0")]
    public void Three_dimensional_appearance_without_a_border_is_refused(string border)
    {
        Action load = () => Load("Appearance = 1\n" + border);
        load.Should().Throw<NotImplementedException>().WithMessage("*Appearance*BorderStyle*unmeasured*");
    }

    [AvaloniaFact]
    public void Missing_lines_use_file_defaults_instead_of_new_control_defaults()
    {
        var view = Load("");
        Read(view, "BorderStyle").Should().Be(0L);
        Read(view, "BackColor").Should().Be(-2147483633L);
        Read(view, "ForeColor").Should().Be(-2147483630L);
        Read(view, "LabelWrap").Should().Be(true);
        Read(view, "HideSelection").Should().Be(true);
        Read(view, "Appearance").Should().Be(0L);
    }

    [AvaloniaFact]
    public void Saved_scalar_properties_load_without_runtime_setter_conversions()
    {
        var view = Load("""
                  View = 3
                  Arrange = 2
                  LabelEdit = 1
                  PictureAlignment = 5
                  SortOrder = -1 'True
                  TextBackground = -1 'True
                  Checkboxes = -1
                  MultiSelect = -1
                  LabelWrap = 0
                  HideSelection = 0
                  AllowReorder = -1
                  FullRowSelect = -1
                  GridLines = -1
                  HideColumnHeaders = -1
                  FlatScrollBar = -1
                  HotTracking = -1
                  HoverSelection = -1
                  ForeColor = 255
                  BackColor = 12648447
                  BorderStyle = 1
                  Appearance = 0
                  OLEDragMode = 1
                  OLEDropMode = 1
                  MousePointer = 2
                  Enabled = 0
            """);
        foreach (var name in new[] { "SortOrder", "TextBackground", "BorderStyle", "OLEDragMode", "OLEDropMode", "LabelEdit" })
            Read(view, name).Should().Be(1L);
        Read(view, "View").Should().Be(3L);
        Read(view, "Arrange").Should().Be(2L);
        Read(view, "PictureAlignment").Should().Be(5L);
        Read(view, "Appearance").Should().Be(0L);
        Read(view, "MousePointer").Should().Be(2L);
        Read(view, "ForeColor").Should().Be(255L);
        Read(view, "BackColor").Should().Be(12648447L);
        foreach (var name in new[] { "Checkboxes", "MultiSelect", "AllowColumnReorder", "FullRowSelect", "GridLines", "HideColumnHeaders", "FlatScrollBar", "HotTracking", "HoverSelection" })
            Read(view, name).Should().Be(true);
        Read(view, "LabelWrap").Should().Be(false);
        Read(view, "HideSelection").Should().Be(false);
        view.IsEnabled.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Flat_columns_load_before_SortKey_and_use_HIMETRIC_widths()
    {
        var view = Load("""
                  SortKey = 1
                  Sorted = -1
                  NumItems = 2
                  BeginProperty ColumnHeader(1) {BDD1F052-858B-11D1-B16A-00C0F0283628}
                     Key = "name"
                     Object.Tag = "first"
                     Text = "Name"
                     Object.Width = 2000
                  EndProperty
                  BeginProperty ColumnHeader(2) {BDD1F052-858B-11D1-B16A-00C0F0283628}
                     Alignment = 1
                     SubItemIndex = 1
                     Text = "Size"
                  EndProperty
            """);
        view.Columns.Entries.Should().HaveCount(2);
        var first = view.Columns.Entries[0];
        Read(first, "Text").Should().Be("Name");
        Read(first, "Key").Should().Be("name");
        Read(first, "Tag").Should().Be("first");
        first.Width.Should().BeApproximately(1133.858f, .001f);
        view.Columns.Entries[1].Width.Should().BeApproximately(816.378f, .001f);
        Read(view.Columns.Entries[1], "Alignment").Should().Be(1L);
        Read(view, "SortKey").Should().Be(1);
        Read(view, "Sorted").Should().Be(true);
        view.Items.Entries.Should().BeEmpty(); // NumItems counts columns, not ListItems.
    }

    [AvaloniaFact]
    public void Bare_column_aliases_load_and_typelib_property_names_are_ignored()
    {
        var view = Load("""
                  AllowColumnReorder = -1
                  ColumnHeaderIcons = "ignored"
                  NumItems = 1
                  BeginProperty ColumnHeader(1)
                     Width = 1800
                     Tag = "alias"
                     Icon = 2
                     Text = "One"
                  EndProperty
            """);
        Read(view, "AllowColumnReorder").Should().Be(false);
        Read(view, "ColumnHeaderIcons").Should().BeNull();
        view.Columns.Entries[0].Width.Should().BeApproximately(1020.472f, .001f);
        Read(view.Columns.Entries[0], "Tag").Should().Be("alias");
        Read(view.Columns.Entries[0], "Icon").Should().Be(0);
    }

    [AvaloniaFact]
    public void Saved_font_name_and_fractional_size_are_read()
    {
        var view = Load("""
                  BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851}
                     Name = "Arial"
                     Size = 9.75
                     Weight = 700
                     Charset = 0
                  EndProperty
            """);
        var font = (ICSharpPropertyBag)Read(view, "Font")!;
        Read(font, "Name").Should().Be("Arial");
        Read(font, "Size").Should().Be(9.75);
    }

    [AvaloniaFact]
    public void Toolbar_style_nested_column_bag_is_ignored()
    {
        var view = Load("""
                  BeginProperty ColumnHeaders
                     BeginProperty ColumnHeader(1)
                        Text = "ignored"
                     EndProperty
                  EndProperty
            """);
        view.Columns.Entries.Should().BeEmpty();
    }
}

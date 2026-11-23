using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Localization;
using HexIDE.VisualDesigner;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.ViewModels;

// Measured dialog keys and order: #4141 and #4142; Esc deliberately follows HexIDE's Fidelity Principle.
public class MenuEditorInteractionTests
{
    private sealed class Editor : IDisposable
    {
        public MenuEditorViewModel Model { get; }
        public MenuEditorView View { get; }
        public Window Window { get; }
        private readonly Avalonia.Themes.Simple.SimpleTheme theme = new();
        private readonly StyleInclude automationNames = new(new Uri("avares://HexIDE/"))
            { Source = new Uri("avares://HexIDE/Automation/AccessKeyNames.axaml") };
        private readonly ResourceInclude icons = new(new Uri("avares://HexIDE/"))
            { Source = new Uri("avares://HexIDE/Themes/IconGeometry.axaml") };
        public Editor(MenuEditorViewModel? model = null, string language = "en")
        {
            Model = model ?? new();
            Application.Current!.Styles.Add(theme);
            Application.Current.Styles.Add(automationNames);
            Application.Current.Resources.MergedDictionaries.Add(icons);
            new LocalizationService().Apply(language);
            View = new MenuEditorView { DataContext = Model };
            Window = new Window { Content = View, Width = 650, Height = 500 };
            IdeDialogChrome.Apply(Window);
            Window.Show();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        public T Field<T>(string name) where T : Control
        {
            // The original view had no names. Locate its existing controls too, so before/after failures test behaviour.
            var text = new[] { "CaptionField", "NameField", "IndexField", "HelpContextIDField" };
            var combo = new[] { "ShortcutField", "NegotiatePositionField" };
            var check = new[] { "CheckedField", "EnabledField", "VisibleField", "WindowListField" };
            var button = new[] { "OKButton", "CancelButton", "LeftButton", "RightButton", "UpButton", "DownButton",
                "NextButton", "InsertButton", "DeleteButton" };
            Control? field = View.FindControl<T>(name);
            if (field is null)
            {
                var children = View.GetLogicalDescendants().OfType<Control>().ToArray();
                field = Array.IndexOf(text, name) is var t && t >= 0 ? children.OfType<TextBox>().ElementAt(t)
                    : Array.IndexOf(combo, name) is var c && c >= 0 ? children.OfType<ComboBox>().ElementAt(c)
                    : Array.IndexOf(check, name) is var h && h >= 0 ? children.OfType<CheckBox>().ElementAt(h)
                    : Array.IndexOf(button, name) is var b && b >= 0 ? children.OfType<Button>().Where(item => item is not ToggleButton).ElementAt(b)
                    : name == "MenuList" ? children.OfType<ListBox>().Single() : null;
            }
            field.Should().BeAssignableTo<T>(name);
            return (T)field!;
        }
        public void Dispose()
        {
            Window.Close();
            Application.Current!.Resources.MergedDictionaries.Remove(icons);
            Application.Current.Styles.Remove(automationNames);
            Application.Current.Styles.Remove(theme);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Blank_row_shows_the_defaults_and_focuses_caption(bool insert)
    {
        using var editor = new Editor();
        if (insert)
        {
            editor.Field<Button>("InsertButton").Focus();
            editor.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            editor.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
        }
        editor.Field<CheckBox>("EnabledField").IsChecked.Should().BeTrue();
        editor.Field<CheckBox>("VisibleField").IsChecked.Should().BeTrue();
        editor.Field<TextBox>("CaptionField").IsFocused.Should().BeTrue();
        editor.Field<TextBox>("CaptionField").CaretIndex.Should().Be(0);
    }

    [AvaloniaFact]
    public async Task Escape_discards_edits_through_the_key_route()
    {
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        var form = project.Forms.First();
        var serializer = new FormSerializer();
        var before = serializer.Serialize(form, "Form1.frm").frmText;
        var windows = Substitute.For<IWindowManager>();
        using var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), windows, Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>()).Initialize(form);
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            using var editor = new Editor((MenuEditorViewModel)call[0]);
            bool? accepted = null;
            editor.Model.CloseRequested += value => accepted = value;
            editor.View.GetVisualDescendants().OfType<TextBox>().First().Focus();
            editor.Window.KeyTextInput("File");
            editor.Model.SelectedMenu!.Caption.Should().Be("File");
            editor.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            accepted.Should().BeFalse();
            return Task.FromResult(accepted == true);
        });
        await designer.EditMenu();
        serializer.Serialize(form, "Form1.frm").frmText.Should().Be(before);
    }

    // #5457: collection changes must survive the real two-way ListBox selection binding.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_then_OK_removes_the_selected_menu_from_the_saved_form(bool submenu)
    {
        await EditSavedMenus(submenu, editor =>
        {
            SelectRow(editor, 1);
            Click(editor, editor.Field<Button>("DeleteButton"));
        }, saved => saved.Should().Contain("Begin VB.Menu mnuFile").And.NotContain("Begin VB.Menu " + (submenu ? "mnuOpen" : "mnuHelp")));
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_moves_keep_the_item_selected_and_save_the_order(bool up)
    {
        await EditSavedMenus(true, editor =>
        {
            SelectRow(editor, up ? 3 : 1);
            var selected = editor.Model.SelectedMenu;
            for (var press = 0; press < 2; press++)
            {
                Click(editor, editor.Field<Button>(up ? "UpButton" : "DownButton"));
                editor.Field<ListBox>("MenuList").SelectedItem.Should().BeSameAs(selected);
                editor.Model.SelectedMenu.Should().BeSameAs(selected);
            }
        }, saved =>
        {
            var names = up ? new[] { "mnuFile", "mnuEdit", "mnuOpen", "mnuHelp" }
                : new[] { "mnuFile", "mnuHelp", "mnuEdit", "mnuOpen" };
            saved.ReplaceLineEndings("\n").Split('\n').Where(line => line.Contains("Begin VB.Menu"))
                .Select(line => line.Trim()).Should().Equal(names.Select(name => "Begin VB.Menu " + name));
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Insert_and_Next_select_the_expected_row_through_the_view(bool insert)
    {
        using var editor = new Editor();
        editor.Model.SelectedMenu!.Caption = "File";
        SelectRow(editor, 0);
        Click(editor, editor.Field<Button>(insert ? "InsertButton" : "NextButton"));
        editor.Field<ListBox>("MenuList").SelectedIndex.Should().Be(1);
        editor.Model.SelectedMenu.Should().BeSameAs(editor.Model.FlatMenu[1]);
    }

    private static void SelectRow(Editor editor, int index)
    {
        editor.Window.UpdateLayout();
        var list = editor.Field<ListBox>("MenuList");
        Click(editor, list.ContainerFromIndex(index)!);
        list.SelectedIndex.Should().Be(index);
    }

    private static void Click(Editor editor, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), editor.Window)!.Value;
        editor.Window.MouseDown(point, MouseButton.Left);
        editor.Window.MouseUp(point, MouseButton.Left);
        editor.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task EditSavedMenus(bool submenu, Action<Editor> edit, Action<string> check)
    {
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        var form = project.Forms.First();
        var serializer = new FormSerializer();
        var windows = Substitute.For<IWindowManager>();
        using var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), windows, Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>()).Initialize(form);
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            var model = (MenuEditorViewModel)call[0];
            var names = submenu ? new[] { "mnuFile", "mnuOpen", "mnuHelp", "mnuEdit" }
                : new[] { "mnuFile", "mnuHelp", "mnuEdit" };
            foreach (var name in names)
            {
                model.SelectedMenu!.Caption = name;
                model.SelectedMenu.Name = name;
                model.SelectedMenu.Indent = 0;
                if (submenu && name != "mnuFile")
                    model.MoveRight();
                model.Next();
            }
            return Task.FromResult(true);
        });
        await designer.EditMenu();
        var before = serializer.Serialize(form, "Form1.frm").frmText;
        before.Should().Contain("Begin VB.Menu " + (submenu ? "mnuOpen" : "mnuHelp"));
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            using var editor = new Editor((MenuEditorViewModel)call[0]);
            bool? accepted = null;
            editor.Model.CloseRequested += value => accepted = value;
            edit(editor);
            Click(editor, editor.Field<Button>("OKButton"));
            accepted.Should().BeTrue();
            return Task.FromResult(accepted == true);
        });
        var exception = await Record.ExceptionAsync(() => designer.EditMenu());
        var saved = serializer.Serialize(form, "Form1.frm").frmText;
        exception.Should().BeNull("the editor must apply OK without throwing; saved form: {0}", saved);
        check(saved);
    }

    [AvaloniaFact]
    public void Enter_activates_the_measured_Next_default()
    {
        using var editor = new Editor();
        editor.Field<TextBox>("CaptionField").Focus();
        editor.Window.KeyTextInput("&File");
        editor.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        editor.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        editor.Model.FlatMenu.Should().HaveCount(2);
        editor.Model.SelectedMenu.Should().BeSameAs(editor.Model.FlatMenu[1]);
    }

    [AvaloniaFact]
    public void Shortcut_and_position_lists_follow_the_measured_choices()
    {
        using var editor = new Editor();
        var shortcuts = editor.Field<ComboBox>("ShortcutField");
        var labels = new List<string> { "(None)" };
        labels.AddRange(Enumerable.Range('A', 26).Select(c => "Ctrl+" + (char)c));
        foreach (var prefix in new[] { "", "Ctrl+", "Shift+", "Shift+Ctrl+" })
            labels.AddRange(Enumerable.Range(1, 12).Where(key => key != 10).Select(key => prefix + "F" + key));
        labels.AddRange(["Ctrl+Ins", "Shift+Ins", "Del", "Shift+Del", "Alt+Bksp"]);
        shortcuts.Items.Should().HaveCount(76);
        shortcuts.Items.Select(item => item!.GetType().GetProperty("Label")!.GetValue(item)).Should().Equal(labels);
        shortcuts.SelectedIndex.Should().Be(0);
        editor.Field<ComboBox>("NegotiatePositionField").Items.Cast<ComboBoxItem>().Select(item => item.Content)
            .Should().Equal("0 - None", "1 - Left", "2 - Middle", "3 - Right");
    }

    [AvaloniaTheory]
    [InlineData("en", "Shift+Ctrl+F12")]
    [InlineData("de", "Umschalt+Strg+F12")]
    [InlineData("fr", "Maj+Ctrl+F12")]
    public void Shortcut_labels_use_the_IDE_language_and_keep_the_saved_notation(string language, string label)
    {
        var localization = new LocalizationService();
        localization.Apply(language);
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        using var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), Substitute.For<IWindowManager>(), Substitute.For<ISettingsService>(),
            localization).Initialize(project.Forms.First());
        using var editor = new Editor(new MenuEditorViewModel(designer, localization), language);
        var shortcuts = editor.Field<ComboBox>("ShortcutField");
        shortcuts.Items.Should().HaveCount(76);
        var choice = shortcuts.Items[70]!;
        choice.GetType().GetProperty("Label")!.GetValue(choice).Should().Be(label);
        choice.GetType().GetProperty("Value")!.GetValue(choice).Should().Be("+^{F12}");
    }

    [AvaloniaFact]
    public void Tab_follows_the_measured_order()
    {
        using var editor = new Editor();
        var names = new[] { "CaptionField", "NameField", "IndexField", "ShortcutField", "HelpContextIDField",
            "NegotiatePositionField", "CheckedField", "EnabledField", "VisibleField", "WindowListField",
            "LeftButton", "RightButton", "UpButton", "DownButton", "NextButton", "InsertButton", "DeleteButton",
            "MenuList", "OKButton", "CancelButton", "CaptionField" };
        foreach (var name in names)
        {
            (name == "MenuList" ? editor.Field<Control>(name).IsKeyboardFocusWithin
                : editor.Field<Control>(name).IsFocused).Should().BeTrue(name);
            editor.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            editor.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        }
    }

    [AvaloniaTheory]
    [InlineData(Key.P, "CaptionField")]
    [InlineData(Key.M, "NameField")]
    [InlineData(Key.X, "IndexField")]
    [InlineData(Key.S, "ShortcutField")]
    [InlineData(Key.H, "HelpContextIDField")]
    [InlineData(Key.O, "NegotiatePositionField")]
    public void Label_access_key_focuses_its_field(Key key, string field)
    {
        using var editor = new Editor();
        editor.Field<Button>("OKButton").Focus();
        editor.Window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.None, null);
        editor.Window.KeyPress(key, RawInputModifiers.Alt, PhysicalKey.None, key.ToString().ToLowerInvariant());
        editor.Window.KeyRelease(key, RawInputModifiers.Alt, PhysicalKey.None, null);
        editor.Window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.None, null);
        editor.Field<Control>(field).IsFocused.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(Key.C, "CheckedField")]
    [InlineData(Key.E, "EnabledField")]
    [InlineData(Key.V, "VisibleField")]
    [InlineData(Key.W, "WindowListField")]
    [InlineData(Key.L, "LeftButton")]
    [InlineData(Key.R, "RightButton")]
    [InlineData(Key.U, "UpButton")]
    [InlineData(Key.B, "DownButton")]
    [InlineData(Key.N, "NextButton")]
    [InlineData(Key.I, "InsertButton")]
    [InlineData(Key.T, "DeleteButton")]
    public void Action_access_keys_reach_the_measured_control(Key key, string field)
    {
        using var editor = new Editor();
        editor.Model.SelectedMenu!.Caption = "File";
        editor.Field<Button>("OKButton").Focus();
        editor.Window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.None, null);
        editor.Window.KeyPress(key, RawInputModifiers.Alt, PhysicalKey.None, key.ToString().ToLowerInvariant());
        editor.Window.KeyRelease(key, RawInputModifiers.Alt, PhysicalKey.None, null);
        editor.Window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.None, null);
        // Insert intentionally returns the caret to Caption, just as opening the editor does.
        editor.Field<Control>(field == "InsertButton" ? "CaptionField" : field).IsFocused.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Automation_peers_name_fields_arrows_and_each_list_row()
    {
        using var editor = new Editor();
        editor.Model.SelectedMenu!.Caption = "&File";
        editor.Model.Next();
        editor.Model.MoveRight();
        editor.Model.SelectedMenu!.Caption = "&Open";
        editor.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        foreach (var name in new[] { "CaptionField", "NameField", "IndexField", "ShortcutField", "HelpContextIDField",
                     "NegotiatePositionField", "CheckedField", "EnabledField", "VisibleField", "WindowListField",
                     "LeftButton", "RightButton", "UpButton", "DownButton", "NextButton", "InsertButton", "DeleteButton",
                     "MenuList", "OKButton", "CancelButton" })
        {
            var announced = ControlAutomationPeer.CreatePeerForElement(editor.Field<Control>(name))!.GetName();
            announced.Should().NotBeNullOrWhiteSpace(name);
            announced.Should().NotContain("_", name);
        }
        var list = editor.Field<ListBox>("MenuList");
        ControlAutomationPeer.CreatePeerForElement(list.ContainerFromIndex(0)!)!.GetName().Should().Be("File");
        ControlAutomationPeer.CreatePeerForElement(list.ContainerFromIndex(1)!)!.GetName().Should().Be("Open, level 2");
        ControlAutomationPeer.CreatePeerForElement(list.ContainerFromIndex(2)!)!.GetName().Should().Be("(blank)");
        foreach (var direction in new[] { "Left", "Right", "Up", "Down" })
            ControlAutomationPeer.CreatePeerForElement(editor.Field<Button>(direction + "Button"))!.GetName().Should().Be(direction);
    }
}

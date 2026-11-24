using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.VisualTree;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;
using HexIDE.Utils;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

// Measured sequence: https://github.com/hexide-io/HexIDE/issues/4141 (2026-11-16 oracle comment).
public class MenuEditorViewModelTests
{
    // VB6 SP6 measurements: https://github.com/hexide-io/HexIDE/issues/853 (2026-11-23 table; oracle PR #5370).
    [AvaloniaTheory]
    [InlineData("mnuFile", null, "mnuFile", null, 0, "Menu control array element must have an index")]
    [InlineData("mnuFile", 0, "mnuFile", null, 1, "Menu control array element must have an index")]
    [InlineData("mnuFile", null, "mnuFile", 0, 0, "Menu control array element must have an index")]
    [InlineData("mnuFile", 0, "mnuFile", 0, 1, "Menu control array indexes must be in ascending order")]
    [InlineData("mnuFile", 1, "mnuFile", 0, 1, "Menu control array indexes must be in ascending order")]
    [InlineData("Command1", null, null, null, 0, "'Command1' is a control name")]
    [InlineData("1abc", null, null, null, 0, "Not a legal object name: '1abc'")]
    [InlineData("a b", null, null, null, 0, "Not a legal object name: 'a b'")]
    [InlineData("", null, null, null, 0, "Menu control must have a name")]
    public async Task OK_refuses_the_measured_names_and_selects_the_faulty_row(
        string firstName, int? firstIndex, string? secondName, int? secondIndex, int selected, string message)
    {
        var windows = Substitute.For<IWindowManager>();
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        var form = project.Forms.First();
        form.UpdateComponents(form.Components.Append(new ComponentInstance(CommandButtonComponentClass.Instance, "Command1")).ToList());
        using var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), windows, Substitute.For<ISettingsService>(),
            new LocalizationService()).Initialize(form);
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            var editor = (MenuEditorViewModel)call[0];
            var first = editor.SelectedMenu!;
            first.Caption = "File";
            first.Name = firstName;
            first.Index = firstIndex;
            if (secondName is not null)
            {
                editor.Next();
                editor.SelectedMenu!.Caption = "Open";
                editor.SelectedMenu.Name = secondName;
                editor.SelectedMenu.Index = secondIndex;
            }
            editor.Next();
            bool? closed = null;
            editor.CloseRequested += result => closed = result;
            editor.Accept();
            closed.Should().BeNull();
            editor.SelectedMenu.Should().BeSameAs(editor.FlatMenu[selected]);
            windows.Received(1).MessageBox(message, Arg.Any<string>(), MessageBoxButtons.Ok, MessageBoxIcon.Error);
            windows.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IWindowManager.MessageBox)).Should().Be(1);
            return Task.FromResult(false);
        });
        await designer.EditMenu();
        form.Components.Should().NotContain(c => c.BaseClass == MenuComponentClass.Instance);
    }

    // VB6 SP6, 2026-11-23 (#5428): a name repeated in a submenu, with no Index on either row, is refused as a
    // missing index with the first row selected. The submenu is what this adds to the same-level rows above.
    [AvaloniaFact]
    public async Task OK_refuses_a_name_repeated_in_a_submenu_without_an_index()
    {
        var windows = Substitute.For<IWindowManager>();
        var form = TestHelpers.CreateProjectWithForm("P", "Form1").Forms.First();
        using var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), windows, Substitute.For<ISettingsService>(),
            new LocalizationService()).Initialize(form);
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            var editor = (MenuEditorViewModel)call[0];
            editor.SelectedMenu!.Caption = "File";
            editor.SelectedMenu.Name = "mnuFile";
            editor.Next();
            editor.SelectedMenu!.Caption = "Open";
            editor.SelectedMenu.Name = "mnuFile";
            editor.MoveRight();
            editor.SelectedMenu.Indent.Should().Be(1);
            bool? closed = null;
            editor.CloseRequested += result => closed = result;
            editor.Accept();
            closed.Should().BeNull();
            editor.SelectedMenu.Should().BeSameAs(editor.FlatMenu[0]);
            windows.Received(1).MessageBox("Menu control array element must have an index", Arg.Any<string>(),
                MessageBoxButtons.Ok, MessageBoxIcon.Error);
            return Task.FromResult(false);
        });
        await designer.EditMenu();
        form.Components.Should().NotContain(c => c.BaseClass == MenuComponentClass.Instance);
    }

    // VB6 SP6 m8–m11: https://github.com/hexide-io/HexIDE/issues/5428 (2026-11-23).
    [AvaloniaTheory]
    [InlineData(0, false, 1, 1)] // m8: different levels
    [InlineData(0, true, 0, 2)] // m9: intervening sibling
    [InlineData(1, true, 0, 2)] // m11: contiguity wins over descending indexes
    public async Task OK_refuses_a_split_menu_array(int firstIndex, bool intervening, int lastIndent, int selected)
    {
        var windows = Substitute.For<IWindowManager>();
        var form = TestHelpers.CreateProjectWithForm("P", "Form1").Forms.First();
        using var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), windows, Substitute.For<ISettingsService>(),
            new LocalizationService()).Initialize(form);
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            var editor = (MenuEditorViewModel)call[0];
            editor.SelectedMenu!.Caption = "File";
            editor.SelectedMenu.Name = "mnuFile";
            editor.SelectedMenu.Index = firstIndex;
            editor.Next();
            if (intervening)
            {
                editor.SelectedMenu!.Caption = "Edit";
                editor.SelectedMenu.Name = "mnuEdit";
                editor.Next();
            }
            editor.SelectedMenu!.Caption = "Open";
            editor.SelectedMenu.Name = "mnuFile";
            editor.SelectedMenu.Index = firstIndex == 0 ? 1 : 0;
            if (lastIndent == 1)
                editor.MoveRight();
            bool? closed = null;
            editor.CloseRequested += result => closed = result;
            editor.Accept();
            closed.Should().BeNull();
            editor.SelectedMenu.Should().BeSameAs(editor.FlatMenu[selected]);
            windows.Received(1).MessageBox("Menu control array elements must be contiguous and within the same submenu",
                Arg.Any<string>(), MessageBoxButtons.Ok, MessageBoxIcon.Error);
            return Task.FromResult(false);
        });
        await designer.EditMenu();
        form.Components.Should().NotContain(c => c.BaseClass == MenuComponentClass.Instance);
    }

    // m10: the earlier element's own submenu does not interrupt consecutive siblings (#5428).
    [AvaloniaFact]
    public async Task OK_accepts_a_menu_array_with_an_elements_own_submenu_between_elements()
    {
        var windows = Substitute.For<IWindowManager>();
        var form = TestHelpers.CreateProjectWithForm("P", "Form1").Forms.First();
        using var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), windows, Substitute.For<ISettingsService>(),
            new LocalizationService()).Initialize(form);
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            var editor = (MenuEditorViewModel)call[0];
            editor.SelectedMenu!.Caption = "File";
            editor.SelectedMenu.Name = "mnuFile";
            editor.SelectedMenu.Index = 0;
            editor.Next();
            editor.SelectedMenu!.Caption = "Sub";
            editor.SelectedMenu.Name = "mnuSub";
            editor.MoveRight();
            editor.Next();
            editor.MoveLeft();
            editor.SelectedMenu!.Caption = "Open";
            editor.SelectedMenu.Name = "mnuFile";
            editor.SelectedMenu.Index = 1;
            editor.FlatMenu.Take(3).Select(m => m.Indent).Should().Equal(0, 1, 0);
            bool? closed = null;
            editor.CloseRequested += result => closed = result;
            editor.Accept();
            closed.Should().BeTrue();
            windows.ReceivedCalls().Should().NotContain(c => c.GetMethodInfo().Name == nameof(IWindowManager.MessageBox));
            return Task.FromResult(closed == true);
        });
        await designer.EditMenu();
        var text = new FormSerializer().Serialize(form, "Form1.frm").frmText.ReplaceLineEndings("\n");
        text.Should().Contain("   Begin VB.Menu mnuFile");
        text.Should().Contain("      Begin VB.Menu mnuSub");
        form.Components.Where(c => c.BaseClass == MenuComponentClass.Instance)
            .Select(PropertyText.ArrayIndexOf).Should().Equal(0, null, 1);
    }

    [AvaloniaTheory]
    [InlineData("Print", false)]
    [InlineData("If", false)]
    [InlineData("mnuFile", true)]
    public async Task OK_accepts_the_measured_names_and_writes_array_indexes(string name, bool array)
    {
        var windows = Substitute.For<IWindowManager>();
        var form = TestHelpers.CreateProjectWithForm("P", "Form1").Forms.First();
        using var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), windows, Substitute.For<ISettingsService>(),
            new LocalizationService()).Initialize(form);
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            var editor = (MenuEditorViewModel)call[0];
            editor.SelectedMenu!.Caption = "File";
            editor.SelectedMenu.Name = name;
            if (array)
            {
                editor.SelectedMenu.Index = 0;
                editor.Next();
                editor.SelectedMenu!.Caption = "Open";
                editor.SelectedMenu.Name = name;
                editor.SelectedMenu.Index = 1;
            }
            bool? closed = null;
            editor.CloseRequested += result => closed = result;
            editor.Accept();
            closed.Should().BeTrue();
            windows.ReceivedCalls().Should().NotContain(c => c.GetMethodInfo().Name == nameof(IWindowManager.MessageBox));
            return Task.FromResult(closed == true);
        });
        await designer.EditMenu();
        var menus = form.Components.Where(c => c.BaseClass == MenuComponentClass.Instance).ToArray();
        menus.Select(c => c.GetPropertyOrDefault(VBProperties.NameProperty)).Should().Equal(array ? [name, name] : [name]);
        if (array)
            menus.Select(PropertyText.ArrayIndexOf).Should().Equal(0, 1);
    }

    [AvaloniaFact]
    public async Task Existing_item_rename_is_checked_only_on_OK()
    {
        var windows = Substitute.For<IWindowManager>();
        var form = TestHelpers.CreateProjectWithForm("P", "Form1").Forms.First();
        var menu = new ComponentInstance(MenuComponentClass.Instance, "mnuFile")
            .SetProperty(VBProperties.CaptionProperty, "File");
        form.UpdateComponents(form.Components.Append(menu).ToList());
        using var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), windows, Substitute.For<ISettingsService>(),
            new LocalizationService()).Initialize(form);
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            var editor = (MenuEditorViewModel)call[0];
            editor.SelectedMenu = editor.FlatMenu[0];
            editor.SelectedMenu.Name = "1abc";
            editor.Next();
            windows.ReceivedCalls().Should().NotContain(c => c.GetMethodInfo().Name == nameof(IWindowManager.MessageBox));
            editor.FlatMenu[0].Name = "a b";
            bool? closed = null;
            editor.CloseRequested += result => closed = result;
            editor.Accept();
            closed.Should().BeNull();
            editor.SelectedMenu.Should().BeSameAs(editor.FlatMenu[0]);
            windows.Received(1).MessageBox("Not a legal object name: 'a b'", Arg.Any<string>(), MessageBoxButtons.Ok, MessageBoxIcon.Error);
            return Task.FromResult(false);
        });
        await designer.EditMenu();
        menu.GetPropertyOrDefault(VBProperties.NameProperty).Should().Be("mnuFile");
    }

    [Fact]
    public void Empty_editor_selects_a_blank_with_enabled_and_visible_checked()
    {
        var editor = new MenuEditorViewModel();
        editor.FlatMenu.Should().ContainSingle().Which.Should().BeSameAs(editor.SelectedMenu);
        editor.SelectedMenu!.Caption.Should().BeEmpty();
        editor.SelectedMenu.IsEnable.Should().BeTrue();
        editor.SelectedMenu.IsVisible.Should().BeTrue();
        editor.SelectedMenu.IsChecked.Should().BeFalse();
        editor.SelectedMenu.IsWindowsList.Should().BeFalse();
    }

    [Fact]
    public void Next_follows_the_measured_table_and_resets_the_trailing_indent()
    {
        var editor = new MenuEditorViewModel();
        editor.SelectedMenu.Should().NotBeNull();
        editor.SelectedMenu!.Caption = "&File";
        Rows(editor, ["&File", ""], [0, 0], 0);
        editor.Next();
        Rows(editor, ["&File", ""], [0, 0], 1);
        editor.MoveRight();
        Rows(editor, ["&File", ""], [0, 1], 1);
        editor.SelectedMenu!.Caption = "&Open";
        Rows(editor, ["&File", "&Open", ""], [0, 1, 0], 1);
        editor.Next();
        Rows(editor, ["&File", "&Open", ""], [0, 1, 1], 2);
        editor.SelectedMenu!.Caption = "&Save";
        Rows(editor, ["&File", "&Open", "&Save", ""], [0, 1, 1, 0], 2);
        editor.Next();
        Rows(editor, ["&File", "&Open", "&Save", ""], [0, 1, 1, 1], 3);
        editor.Next();
        Rows(editor, ["&File", "&Open", "&Save", ""], [0, 1, 1, 1], 0);
        editor.SelectedMenu = editor.FlatMenu[2];
        editor.MoveLeft();
        editor.Next();
        Rows(editor, ["&File", "&Open", "&Save", ""], [0, 1, 0, 0], 3);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepting_a_fresh_editor_writes_only_the_typed_item(bool typeCaption)
    {
        var windows = Substitute.For<IWindowManager>();
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        var form = project.Forms.First();
        using var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), windows, Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>()).Initialize(form);
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            var editor = (MenuEditorViewModel)call[0];
            var theme = new Avalonia.Themes.Simple.SimpleTheme();
            var icons = new ResourceInclude(new Uri("avares://HexIDE/"))
            {
                Source = new Uri("avares://HexIDE/Themes/IconGeometry.axaml")
            };
            Application.Current!.Styles.Add(theme);
            Application.Current.Resources.MergedDictionaries.Add(icons);
            var view = new MenuEditorView { DataContext = editor };
            var window = new Window { Content = view, Width = 600, Height = 450 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var fields = view.GetVisualDescendants().OfType<TextBox>().ToArray();
                if (typeCaption)
                {
                    fields[0].Focus();
                    window.KeyTextInput("&File");
                    fields[1].Focus();
                    window.KeyTextInput("mnuFile");
                }
                bool? accepted = null;
                editor.CloseRequested += result => accepted = result;
                var ok = view.GetVisualDescendants().OfType<Button>().First();
                ((IInvokeProvider)new ButtonAutomationPeer(ok)).Invoke();
                return Task.FromResult(accepted == true);
            }
            finally
            {
                window.Close();
                Application.Current.Resources.MergedDictionaries.Remove(icons);
                Application.Current.Styles.Remove(theme);
            }
        });

        await designer.EditMenu();

        var menus = form.Components.Where(c => c.BaseClass == MenuComponentClass.Instance).ToArray();
        menus.Should().HaveCount(typeCaption ? 1 : 0);
        var serialized = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        serialized.Split("Begin VB.Menu").Should().HaveCount(typeCaption ? 2 : 1);
        if (typeCaption)
        {
            serialized.Should().Contain("Begin VB.Menu mnuFile");
            menus[0].GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("&File");
            menus[0].GetPropertyOrDefault(VBProperties.NameProperty).Should().Be("mnuFile");
        }
    }

    [Theory]
    [InlineData("Delete")]
    [InlineData("MoveUp")]
    [InlineData("MoveDown")]
    public void Trailing_blank_stays_available_after_tree_operations(string operation)
    {
        var editor = new MenuEditorViewModel();
        editor.SelectedMenu.Should().NotBeNull();
        editor.SelectedMenu!.Caption = "File";
        editor.Next();
        switch (operation)
        {
            case "Delete": editor.Delete(); break;
            case "MoveUp": editor.MoveUp(); break;
            case "MoveDown": editor.MoveDown(); break;
        }
        Rows(editor, ["File", ""], [0, 0], 1);
    }

    [Fact]
    public void Unchanged_apply_preserves_the_property_bag_and_emits_no_notifications()
    {
        var menu = new ComponentInstance(MenuComponentClass.Instance, "mnuFile")
            .SetProperty(VBProperties.CaptionProperty, "&File");
        var before = menu.GetAllSetProperties();
        var writes = new List<string>();
        menu.OnComponentPropertyChanged += (_, property) => writes.Add(property.Name);

        new MenuViewModel(menu).Apply(menu);

        menu.GetAllSetProperties().Should().BeEquivalentTo(before);
        writes.Should().BeEmpty();
    }

    [Fact]
    public void New_item_writes_only_non_default_properties()
    {
        var menu = new ComponentInstance(MenuComponentClass.Instance, "mnuFile");
        var editor = new MenuViewModel { Name = "mnuFile", Caption = "&File" };

        editor.Apply(menu);

        menu.GetAllSetProperties().Keys.Select(p => p.Name).Should().BeEquivalentTo("(Name)", "Caption");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepting_existing_menu_changes_only_the_edited_property(bool checkItem)
    {
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        var form = project.Forms.First();
        var menu = new ComponentInstance(MenuComponentClass.Instance, "mnuFile")
            .SetProperty(VBProperties.CaptionProperty, "&File");
        form.UpdateComponents(form.Components.Append(menu).ToList());
        var windows = Substitute.For<IWindowManager>();
        var events = new EventBus();
        using var designer = new FormEditViewModel(null!, events, Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), windows, Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>()).Initialize(form);
        // Initialize loads the form's menu tree (#4140), so the editor opens on it.
        designer.TopLevelMenu.Should().ContainSingle(c => c.Instance == menu);
        var serializer = new FormSerializer();
        var before = serializer.Serialize(form, "Form1.frm").frmText;
        var writes = new List<string>();
        menu.OnComponentPropertyChanged += (_, property) => writes.Add(property.Name);
        var commits = 0;
        using var subscription = events.Subscribe<FormLayoutChangedEvent>(_ => commits++);
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            var editor = (MenuEditorViewModel)call[0];
            if (checkItem)
                editor.FlatMenu[0].IsChecked = true;
            return Task.FromResult(true);
        });

        await designer.EditMenu();

        var after = serializer.Serialize(form, "Form1.frm").frmText;
        if (checkItem)
        {
            writes.Should().Equal("Checked");
            after.ReplaceLineEndings("\n").Should().Be(before.ReplaceLineEndings("\n")
                .Replace("      Caption         =   \"&File\"\n", "      Caption         =   \"&File\"\n      Checked         =   -1  'True\n"));
            commits.Should().Be(1);
        }
        else
        {
            after.Should().Be(before);
            writes.Should().BeEmpty();
            commits.Should().Be(0);
            designer.IsModified.Should().BeFalse();
            designer.CanUndo.Should().BeFalse();
        }
    }

    private static void Rows(MenuEditorViewModel editor, string[] captions, int[] indents, int selected)
    {
        editor.FlatMenu.Select(m => m.Caption).Should().Equal(captions);
        editor.FlatMenu.Select(m => m.Indent).Should().Equal(indents);
        editor.SelectedMenu.Should().BeSameAs(editor.FlatMenu[selected]);
    }
}

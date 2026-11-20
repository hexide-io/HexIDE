using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using HexIDE.Automation;
using HexIDE.Desktop.Server;
using HexIDE.Integration.Tests.Views;

namespace HexIDE.Integration.Tests.Automation;

// Model and event order from #5066's brief (#5011's K1–K14, Kronos 2026-11-20).
public class MenuBarToolTests
{
    private const string OpenPath = "MenuBar/MenuItem[File]/MenuItem[Open]";
    private sealed class Harness : IDisposable
    {
        internal readonly HexIdeTools Tools = new(null!);
        internal readonly Window Ide = new() { Title = "IDE", Content = new Menu { Items = { new MenuItem { Header = "File" } } } };
        internal readonly Window Form = new() { Title = "Form1" };
        internal readonly NativeMenuItem Open = new("_Open");
        internal readonly NativeMenuItem File;
        internal readonly NativeMenu Sub = new();
        internal readonly NativeMenu App = new();
        internal readonly NativeMenuItem About = new("About HexIDE");
        internal readonly List<string> Events = [];
        internal readonly List<Window> Windows;
        private readonly IApplicationLifetime? previous = Application.Current!.ApplicationLifetime;
        private readonly string? name = Application.Current!.Name;
        private readonly NativeMenu? previousMenu = NativeMenu.GetMenu(Application.Current!);
        private readonly FieldInfo lifetimeField = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(IApplicationLifetime));
        internal Harness()
        {
            Windows = [Ide, Form];
            var lifetime = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
            lifetime.MainWindow.Returns(Ide);
            lifetime.Windows.Returns(Windows);
            lifetimeField.SetValue(Application.Current, lifetime);
            Application.Current!.Name = "HexIDE";
            Sub.Items.Add(Open);
            File = new NativeMenuItem("_File") { Menu = Sub };
            var menu = new NativeMenu();
            menu.Items.Add(File);
            NativeMenu.SetMenu(Form, menu);
            App.Items.Add(About);
            NativeMenu.SetMenu(Application.Current, App);
            Sub.NeedsUpdate += (_, _) => Events.Add("NeedsUpdate");
            Sub.Opening += (_, _) => Events.Add("Opening");
            Sub.Closed += (_, _) => Events.Add("Closed");
            Open.Click += (_, _) => Events.Add("Click");
            About.Click += (_, _) => Events.Add("About");
            Ide.Show();
            Form.Show();
            IdeDialogOwnershipTests.Activate(Form);
        }
        public void Dispose()
        {
            foreach (var window in Windows.AsEnumerable().Reverse()) window.Close();
            NativeMenu.SetMenu(Application.Current!, previousMenu);
            Application.Current!.Name = name;
            lifetimeField.SetValue(Application.Current, previous);
        }
    }

    [AvaloniaFact]
    public async Task Dump_lists_app_then_bar_window_and_nested_items()
    {
        using var h = new Harness();
        var result = await h.Tools.DumpVisualTreeAsync(window: "menubar");
        result.Error.Should().BeNull();
        result.Root!.Children.Select(n => n.Name).Should().Equal("HexIDE", "File");
        result.Root.Children[1].Children.Single().Path.Should().Be(OpenPath);
        result.Note.Should().Contain("Form1").And.Contain(h.Windows.Count(w => w.IsVisible && w.IsActive) == 1
            ? "single visible active window" : "auto").And.NotContain("Services");
        result.Note.Should().NotContain("without a submenu", "no top-level item lacks one here");
    }
    [AvaloniaFact]
    public async Task Inspect_reads_native_item()
    {
        using var h = new Harness();
        var result = await h.Tools.InspectElementAsync(OpenPath, "menubar");
        result.Error.Should().BeNull();
        result.Element!.Name.Should().Be("Open");
        result.Element.Providers.Should().Equal("invoke");
    }
    [AvaloniaFact]
    public async Task Invoke_opens_parent_then_clicks_once()
    {
        using var h = new Harness();
        (await h.Tools.InteractAsync(OpenPath, "invoke", window: "menubar")).Success.Should().BeTrue();
        h.Events.Should().Equal("NeedsUpdate", "Opening", "Click");
    }
    [AvaloniaFact]
    public async Task Invoke_app_opens_root_then_clicks_once()
    {
        using var h = new Harness();
        h.App.NeedsUpdate += (_, _) => h.Events.Add("NeedsUpdate");
        h.App.Opening += (_, _) => h.Events.Add("Opening");
        (await h.Tools.InvokeMenuItemAsync("HexIDE/About HexIDE", TestContext.Current.CancellationToken)).Success.Should().BeTrue();
        h.Events.Should().Equal("NeedsUpdate", "Opening", "About");
        var save = new NativeMenuItem("Save");
        var saveAs = new NativeMenuItem("Save...");
        var plugin = new NativeMenuItem("Plug__in");
        var options = new NativeMenuItem("Options…");
        h.App.Items.Add(save);
        h.App.Items.Add(saveAs);
        h.App.Items.Add(plugin);
        h.App.Items.Add(options);
        save.Click += (_, _) => h.Events.Add("Save");
        saveAs.Click += (_, _) => h.Events.Add("SaveAs");
        plugin.Click += (_, _) => h.Events.Add("Plugin");
        options.Click += (_, _) => h.Events.Add("Options");
        foreach (var name in new[] { "Save...", "Plug_in", "Options" })
            (await h.Tools.InvokeMenuItemAsync("HexIDE/" + name, TestContext.Current.CancellationToken)).Success.Should().BeTrue();
        h.Events.Where(e => e is "Save" or "SaveAs" or "Plugin" or "Options").Should().Equal("SaveAs", "Plugin", "Options");
    }
    [AvaloniaTheory]
    [InlineData("disabled")]
    [InlineData("hidden")]
    public async Task Unreachable_item_is_refused_without_events(string reason)
    {
        using var h = new Harness();
        h.Open.IsEnabled = reason != "disabled";
        h.Open.IsVisible = reason != "hidden";
        var result = await h.Tools.InteractAsync(OpenPath, "invoke", window: "menubar");
        result.Success.Should().BeFalse();
        result.Error.Should().Contain(reason);
        if (reason == "hidden")
            result.Error.Should().Contain("IsVisible is False").And.NotContain("without a submenu");
        h.Events.Should().BeEmpty();
    }

    // On Kronos a running form's Click handler unticked Wrap after the reply had read it as ticked (#5066's live check).
    [AvaloniaFact]
    public async Task Reply_reads_the_state_after_the_click_has_run()
    {
        using var h = new Harness();
        h.Open.ToggleType = MenuItemToggleType.CheckBox;
        h.Open.IsChecked = true;
        h.Open.Click += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => h.Open.IsChecked = !h.Open.IsChecked);
        var result = await h.Tools.InteractAsync(OpenPath, "invoke", window: "menubar");
        result.Success.Should().BeTrue();
        result.Detail.Should().Contain("checked False").And.NotContain("checked True");
    }

    [AvaloniaFact]
    public async Task Reply_names_no_tick_for_an_item_that_takes_none()
    {
        using var h = new Harness();
        var result = await h.Tools.InteractAsync(OpenPath, "invoke", window: "menubar");
        result.Detail.Should().StartWith($"invoke: '{OpenPath}'. Now: enabled True. Bar window: 'Form1'")
            .And.NotContain("checked").And.NotContain(". .");
    }
    [AvaloniaFact]
    public async Task Checked_item_reports_toggle_state()
    {
        using var h = new Harness();
        h.Open.ToggleType = MenuItemToggleType.CheckBox;
        h.Open.IsChecked = true;
        (await h.Tools.InspectElementAsync(OpenPath, "menubar")).Element!.ToggleState.Should().BeTrue();
    }
    [AvaloniaFact]
    public async Task Separator_is_only_in_unfiltered_tree()
    {
        using var h = new Harness();
        h.Sub.Items.Add(new NativeMenuItemSeparator());
        var all = await h.Tools.DumpVisualTreeAsync(interactiveOnly: false, window: "menubar", match: "NativeMenuItemSeparator");
        all.Matches.Should().ContainSingle();
        all.Matches![0].Providers.Should().BeEmpty();
        var filtered = await h.Tools.DumpVisualTreeAsync(window: "menubar");
        filtered.Root!.Children[1].Children.Should().ContainSingle();
    }
    [AvaloniaFact]
    public async Task Expand_and_collapse_raise_menu_events()
    {
        using var h = new Harness();
        (await h.Tools.InteractAsync("MenuBar/MenuItem[File]", "expand", window: "menubar")).Success.Should().BeTrue();
        (await h.Tools.InteractAsync("MenuBar/MenuItem[File]", "collapse", window: "menubar")).Success.Should().BeTrue();
        h.Events.Should().Equal("NeedsUpdate", "Opening", "Closed");
    }
    [AvaloniaFact]
    public async Task Empty_submenu_is_expanded_and_never_clicked()
    {
        using var h = new Harness();
        h.Sub.Items.Clear();
        h.File.Click += (_, _) => h.Events.Add("FileClick");
        var refusal = await h.Tools.InteractAsync("MenuBar/MenuItem[File]", "invoke", window: "menubar");
        refusal.Success.Should().BeFalse();
        refusal.Error.Should().Contain("expand");
        h.Events.Should().BeEmpty();
        (await h.Tools.InteractAsync("MenuBar/MenuItem[File]", "expand", window: "menubar")).Success.Should().BeTrue();
        h.Events.Should().Equal("NeedsUpdate", "Opening");
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Plain_or_modal_window_without_menus_contributes_nothing(bool modal)
    {
        using var h = new Harness();
        var plain = new Window { Title = "Plain" };
        h.Windows.Add(plain);
        if (modal) _ = plain.ShowDialog<bool>(h.Form);
        else plain.Show();
        IdeDialogOwnershipTests.Activate(plain);
        var result = await h.Tools.DumpVisualTreeAsync(window: "MENUBAR");
        result.Root!.Children.Should().ContainSingle().Which.Name.Should().Be("HexIDE");
        result.Note.Should().Contain("Plain").And.Contain("auto");
    }
    [AvaloniaFact]
    public async Task Empty_bar_explains_where_menus_are()
    {
        using var h = new Harness();
        NativeMenu.SetMenu(Application.Current!, null);
        NativeMenu.SetMenu(h.Form, null);
        var result = await h.Tools.DumpVisualTreeAsync(window: "menubar");
        result.Root!.Children.Should().BeEmpty();
        result.Note.Should().Contain("only on macOS").And.Contain("Form1").And.Contain("IDE");
    }
    [AvaloniaTheory]
    [InlineData("snapshot")]
    [InlineData("type")]
    [InlineData("hover")]
    [InlineData("key")]
    public async Task Window_input_and_pixels_refuse_menu_bar(string tool)
    {
        using var h = new Harness();
        var error = tool switch
        {
            "snapshot" => (await h.Tools.TakeSnapshotAsync(window: "menubar")).Error,
            "type" => (await h.Tools.TypeTextAsync(OpenPath, "text", window: "menubar")).Error,
            "hover" => (await h.Tools.HoverAsync(OpenPath, window: "menubar")).Error,
            _ => (await h.Tools.PressKeyAsync(OpenPath, "Enter", window: "menubar")).Error
        };
        error.Should().Contain("outside HexIDE's windows").And.Contain("dump_visual_tree").And.Contain("inspect_element").And.Contain("interact");
    }
    [AvaloniaTheory]
    [InlineData("Window/MenuItem[File]", "menubar")]
    [InlineData(OpenPath, "Form1")]
    public async Task Paths_refuse_the_wrong_scope(string path, string scope)
    {
        using var h = new Harness();
        (await h.Tools.InspectElementAsync(path, scope)).Error.Should().Contain("MenuBar").And.Contain("Window").And.Contain("menubar");
    }
    [AvaloniaFact]
    public async Task Native_items_remain_outside_window_tree()
    {
        using var h = new Harness();
        var result = await h.Tools.DumpVisualTreeAsync(window: "Form1", match: "Open");
        result.Error.Should().BeNull();
        result.Matches.Should().BeEmpty();
    }
    [AvaloniaFact]
    public async Task Top_level_leaf_is_hidden_and_refused()
    {
        using var h = new Harness();
        var menu = NativeMenu.GetMenu(h.Form)!;
        var leaf = new NativeMenuItem("Leaf");
        menu.Items.Add(leaf);
        leaf.Click += (_, _) => h.Events.Add("Leaf");
        var tree = await h.Tools.DumpVisualTreeAsync(window: "menubar");
        tree.Root!.Children.Last().IsHidden.Should().BeTrue();
        tree.Note.Should().Contain("macOS doesn't draw a top-level item without a submenu");
        tree.FlagsNote.Should().Contain("Native menu interaction refuses hidden items").And.NotContain("invoke_command");
        var result = await h.Tools.InteractAsync("MenuBar/MenuItem[Leaf]", "invoke", window: "menubar");
        result.Error.Should().Contain("hidden").And.Contain("without a submenu").And.NotContain("IsVisible");
        h.Events.Should().BeEmpty();
    }
    [AvaloniaFact]
    public async Task Duplicate_headers_use_type_indexes_and_round_trip()
    {
        using var h = new Harness();
        var second = new NativeMenuItem("Open");
        h.Sub.Items.Add(second);
        second.Click += (_, _) => h.Events.Add("Second");
        var tree = await h.Tools.DumpVisualTreeAsync(window: "menubar");
        tree.Root!.Children[1].Children.Select(n => n.Path).Should().Equal(
            "MenuBar/MenuItem[File]/MenuItem[#0]", "MenuBar/MenuItem[File]/MenuItem[#1]");
        (await h.Tools.InteractAsync(tree.Root.Children[1].Children[1].Path, "invoke", window: "menubar")).Success.Should().BeTrue();
        h.Events.Should().Equal("NeedsUpdate", "Opening", "Second");
    }
    [AvaloniaFact]
    public async Task Root_depth_and_match_use_native_tree()
    {
        using var h = new Harness();
        var shallow = await h.Tools.DumpVisualTreeAsync(maxDepth: 1, window: "menubar");
        shallow.Root!.Children[1].Truncated.Should().BeTrue();
        var scoped = await h.Tools.DumpVisualTreeAsync(root: "MenuBar/MenuItem[File]", window: "menubar", match: "Open");
        scoped.Matches.Should().ContainSingle().Which.Path.Should().Be(OpenPath);
        var full = await h.Tools.DumpVisualTreeAsync(root: "MenuBar/MenuItem[File]", window: "menubar");
        full.Root!.Children.Should().ContainSingle();
        for (var i = 0; i < 200; i++) h.Sub.Items.Add(new NativeMenuItem(new string('x', 200) + i));
        var fitted = await h.Tools.DumpVisualTreeAsync(window: "menubar");
        fitted.Note.Should().Contain("cut").And.Contain("Form1").And.Contain("Bar window");
    }
    [AvaloniaFact]
    public async Task Window_root_is_not_opened_and_app_root_is_opened()
    {
        using var h = new Harness();
        NativeMenu.GetMenu(h.Form)!.NeedsUpdate += (_, _) => h.Events.Add("Bar");
        h.App.NeedsUpdate += (_, _) => h.Events.Add("AppUpdate");
        h.App.Opening += (_, _) => h.Events.Add("AppOpening");
        (await h.Tools.InteractAsync(OpenPath, "invoke", window: "menubar")).Success.Should().BeTrue();
        (await h.Tools.InteractAsync("MenuBar/MenuItem[HexIDE]/MenuItem[About HexIDE]", "invoke", window: "menubar")).Success.Should().BeTrue();
        h.Events.Should().Equal("NeedsUpdate", "Opening", "Click", "AppUpdate", "AppOpening", "About");
    }
    [AvaloniaTheory]
    [InlineData("toggle", "invoke")]
    [InlineData("set_value", "invoke")]
    public async Task Unsupported_actions_name_applicable_verb(string action, string verb)
    {
        using var h = new Harness();
        var result = await h.Tools.InteractAsync(OpenPath, action, window: "menubar");
        result.Success.Should().BeFalse();
        result.Error.Should().Contain(verb);
        h.Events.Should().BeEmpty();
    }
    [AvaloniaFact]
    public async Task App_menu_title_falls_back_to_Application()
    {
        using var h = new Harness();
        Application.Current!.Name = "";
        var tree = await h.Tools.DumpVisualTreeAsync(window: "menubar");
        tree.Root!.Children[0].Name.Should().Be("Application");
        (await h.Tools.InvokeMenuItemAsync("Application/About HexIDE", TestContext.Current.CancellationToken)).Success.Should().BeTrue();
        h.Events.Should().Equal("About");
    }
    [AvaloniaFact]
    public async Task Unknown_menu_lists_in_window_then_app_menu()
    {
        using var h = new Harness();
        var result = await h.Tools.InvokeMenuItemAsync("Missing/Item", TestContext.Current.CancellationToken);
        result.Error.Should().Contain("It holds: File").And.Contain("App menu: HexIDE");
    }
    [AvaloniaFact]
    public async Task App_menu_without_in_window_menu_still_invokes()
    {
        using var h = new Harness();
        h.Ide.Content = null;
        (await h.Tools.InvokeMenuItemAsync("HexIDE/About HexIDE", TestContext.Current.CancellationToken)).Success.Should().BeTrue();
        (await h.Tools.InvokeMenuItemAsync("File/Open", TestContext.Current.CancellationToken)).Error.Should().Be("No menu bar found in main window");
    }

}

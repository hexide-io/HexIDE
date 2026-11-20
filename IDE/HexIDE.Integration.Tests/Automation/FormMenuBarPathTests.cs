using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Desktop.Server;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Automation;

// Tool-level measurements and reply design: https://github.com/hexide-io/HexIDE/issues/5102.
public class FormMenuBarPathTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Probe = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption = "Form1"
           ClientHeight = 3000
           ClientWidth = 4500
           Begin VB.TextBox Text1
              Height = 375
              Width = 1500
           End
           Begin VB.Menu mnuGame
              Caption = "&Game"
              Begin VB.Menu mnuNew
                 Caption = "&New"
              End
              Begin VB.Menu mnuLevel
                 Caption = "&Beginner"
                 Index = 0
              End
           End
           Begin VB.Menu mnuHelp
              Caption = "&Help"
              Begin VB.Menu mnuAbout
                 Caption = "&About"
              End
           End
        End
        Attribute VB_Name = "Form1"
        Private Sub Form_Load()
            Load mnuLevel(1)
        End Sub
        """;

    private sealed class Harness : IDisposable
    {
        private readonly IdeContext context = new(null!, null!, Substitute.For<HexIDE.Lsp.ILspClient>(),
            null!, null!, null!, null!, null!, null!, Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!);
        internal readonly HexIdeTools Tools;
        internal readonly Window Ide = new() { Title = "IDE" };
        internal readonly Window Form;
        internal readonly List<Window> Windows;
        private readonly bool previousSetting = FormNativeMenu.UseSystemMenuBar;
        private readonly IApplicationLifetime? previousLifetime = Application.Current!.ApplicationLifetime;
        private readonly NativeMenu? previousMenu = NativeMenu.GetMenu(Application.Current!);
        private readonly FieldInfo lifetimeField = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(IApplicationLifetime));

        internal Harness()
        {
            Tools = new HexIdeTools(context);
            FormNativeMenu.UseSystemMenuBar = true;
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            var form = new FormDeserializer().Deserialize(project, Probe, new NullSink())!;
            project.AddForm(form);
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            Form = window;
            Windows = [Ide, Form];
            var lifetime = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
            lifetime.MainWindow.Returns(Ide);
            lifetime.Windows.Returns(Windows);
            lifetimeField.SetValue(Application.Current, lifetime);
            NativeMenu.SetMenu(Application.Current!, null);
            Ide.Show();
            Dispatcher.UIThread.RunJobs();
            IdeDialogOwnershipTests.Activate(Form);
        }

        public void Dispose()
        {
            foreach (var window in Windows.AsEnumerable().Reverse()) window.Close();
            NativeMenu.SetMenu(Application.Current!, previousMenu);
            lifetimeField.SetValue(Application.Current, previousLifetime);
            FormNativeMenu.UseSystemMenuBar = previousSetting;
            context.Dispose();
        }
    }

    private static string IdReply(string id, string path) =>
        $"no child matching '#{id}' under 'Window'. Form1's menus are in the system menu bar, not the window: use window: \"menubar\", as {path} or MenuBar/#{id}";

    [AvaloniaTheory]
    [InlineData("mnuGame", "MenuBar/MenuItem[Game]")]
    [InlineData("mnuNew", "MenuBar/MenuItem[Game]/MenuItem[New]")]
    [InlineData("mnuLevel(1)", "MenuBar/MenuItem[Game]/MenuItem[#2]")]
    public async Task Window_id_failure_points_to_both_native_paths(string id, string path)
    {
        using var h = new Harness();
        (await h.Tools.InspectElementAsync("Window/#" + id, "Form1")).Error.Should().Be(IdReply(id, path));
    }

    [AvaloniaFact]
    public async Task Window_caption_failure_points_to_native_discovery()
    {
        using var h = new Harness();
        (await h.Tools.InspectElementAsync("Window/MenuItem[Game]", "Form1")).Error.Should().Be(
            "no child matching 'MenuItem[Game]' under 'Window'. Form1's menus are in the system menu bar, not the window: use window: \"menubar\"; dump_visual_tree(window: \"menubar\") lists them");
    }

    [AvaloniaFact]
    public async Task Auto_failure_keeps_its_window_note_before_the_menu_hint()
    {
        using var h = new Harness();
        (await h.Tools.InspectElementAsync("Window/#mnuGame", "auto")).Error.Should().Be(
            "no child matching '#mnuGame' under 'Window'. The path was looked for in 'Form1', the window \"auto\" picked over the IDE's own. "
            + IdReply("mnuGame", "MenuBar/MenuItem[Game]").Split(". ", 2)[1]);
    }

    [AvaloniaFact]
    public async Task Window_interaction_failure_points_to_native_paths()
    {
        using var h = new Harness();
        var result = await h.Tools.InteractAsync("Window/#mnuGame", "expand", window: "Form1");
        result.Success.Should().BeFalse();
        result.Error.Should().Be(IdReply("mnuGame", "MenuBar/MenuItem[Game]"));
    }

    [AvaloniaFact]
    public async Task Window_dump_root_failure_points_to_native_paths()
    {
        using var h = new Harness();
        (await h.Tools.DumpVisualTreeAsync(root: "Window/#mnuGame", window: "Form1")).Error.Should().Be(
            IdReply("mnuGame", "MenuBar/MenuItem[Game]"));
    }

    private static IEnumerable<UiNode> Flatten(UiNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    [AvaloniaFact]
    public async Task Native_dump_carries_each_menu_code_name_at_its_caption_path()
    {
        using var h = new Harness();
        var result = await h.Tools.DumpVisualTreeAsync(window: "menubar", interactiveOnly: false);
        result.Error.Should().BeNull();
        Flatten(result.Root!).Where(n => n.ControlType == "MenuItem").Select(n => (n.Path, n.AutomationId)).Should().Equal(
            ("MenuBar/MenuItem[Game]", "mnuGame"),
            ("MenuBar/MenuItem[Game]/MenuItem[New]", "mnuNew"),
            ("MenuBar/MenuItem[Game]/MenuItem[#1]", "mnuLevel(0)"),
            ("MenuBar/MenuItem[Game]/MenuItem[#2]", "mnuLevel(1)"),
            ("MenuBar/MenuItem[Help]", "mnuHelp"),
            ("MenuBar/MenuItem[Help]/MenuItem[About]", "mnuAbout"));
    }

    [AvaloniaFact]
    public async Task Native_id_inspection_finds_a_nested_item()
    {
        using var h = new Harness();
        var result = await h.Tools.InspectElementAsync("MenuBar/#mnuNew", "menubar");
        result.Error.Should().BeNull();
        result.Element!.Name.Should().Be("New");
        result.Element.AutomationId.Should().Be("mnuNew");
    }

    [AvaloniaFact]
    public async Task Native_id_invocation_opens_the_full_ancestor_chain_once()
    {
        using var h = new Harness();
        var openings = 0;
        ((NativeMenuItem)NativeMenu.GetMenu(h.Form)!.Items[0]).Menu!.Opening += (_, _) => openings++;
        var result = await h.Tools.InteractAsync("MenuBar/#mnuNew", "invoke", window: "menubar");
        result.Success.Should().BeTrue(result.Error);
        openings.Should().Be(1);
    }

    [AvaloniaFact]
    public async Task Id_paths_ignore_case_and_search_below_the_current_node()
    {
        using var h = new Harness();
        var result = await h.Tools.InspectElementAsync("MenuBar/#MNUGAME/#MNUNEW", "menubar");
        result.Error.Should().BeNull();
        result.Element!.AutomationId.Should().Be("mnuNew");
        (await h.Tools.InspectElementAsync("MenuBar/#mnuHelp/#mnuNew", "menubar")).Error.Should().Be(
            "No menu item '#mnuNew' under 'Help'. Discover paths with dump_visual_tree(window: \"menubar\").");
    }

    [AvaloniaFact]
    public async Task A_dump_rooted_at_an_id_builds_its_children_from_that_path()
    {
        using var h = new Harness();
        var result = await h.Tools.DumpVisualTreeAsync(root: "MenuBar/#mnuGame", window: "menubar");
        result.Error.Should().BeNull();
        result.Root!.Children.Select(n => n.Path).Should().Equal(
            "MenuBar/#mnuGame/MenuItem[New]", "MenuBar/#mnuGame/MenuItem[#1]", "MenuBar/#mnuGame/MenuItem[#2]");
        var indexed = await h.Tools.InspectElementAsync(result.Root.Children[2].Path, "menubar");
        indexed.Error.Should().BeNull();
        indexed.Element!.AutomationId.Should().Be("mnuLevel(1)");
    }

    [AvaloniaFact]
    public async Task The_hint_spells_the_id_as_the_menu_does()
    {
        using var h = new Harness();
        (await h.Tools.InspectElementAsync("Window/#MNUNEW", "Form1")).Error.Should().Be(
            "no child matching '#MNUNEW' under 'Window'. Form1's menus are in the system menu bar, not the window: use window: \"menubar\", as MenuBar/MenuItem[Game]/MenuItem[New] or MenuBar/#mnuNew");
    }

    [AvaloniaFact]
    public async Task Window_failure_names_a_different_bar_window()
    {
        using var h = new Harness();
        var other = new Window { Title = "Other" };
        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem("Other") { Menu = new NativeMenu() });
        NativeMenu.SetMenu(other, menu);
        h.Windows.Add(other);
        other.Show();
        IdeDialogOwnershipTests.Activate(other);
        (await h.Tools.InspectElementAsync("Window/#mnuGame", "Form1")).Error.Should().Be(
            "no child matching '#mnuGame' under 'Window'. Form1's menus are in the system menu bar, not the window. window: \"menubar\" reaches the bar window's menus, and the bar window is now 'Other', not Form1");
    }

    [AvaloniaFact]
    public async Task Unknown_window_id_keeps_its_original_failure()
    {
        using var h = new Harness();
        (await h.Tools.InspectElementAsync("Window/#nope", "Form1")).Error.Should().Be("no child matching '#nope' under 'Window'");
    }

    [AvaloniaFact]
    public async Task Non_menu_control_still_resolves_in_the_window()
    {
        using var h = new Harness();
        var result = await h.Tools.InspectElementAsync("Window/#Text1", "Form1");
        result.Error.Should().BeNull();
        result.Element!.AutomationId.Should().Be("Text1");
    }

    [AvaloniaFact]
    public async Task A_window_without_a_native_menu_keeps_its_original_failure()
    {
        using var h = new Harness();
        (await h.Tools.InspectElementAsync("Window/MenuItem[Game]", "ide")).Error.Should().Be(
            "no child matching 'MenuItem[Game]' under 'Window'");
    }

    [AvaloniaFact]
    public async Task An_unknown_native_id_keeps_its_original_failure()
    {
        using var h = new Harness();
        (await h.Tools.InspectElementAsync("MenuBar/#nope", "menubar")).Error.Should().Be(
            "No menu item '#nope' under ''. Discover paths with dump_visual_tree(window: \"menubar\").");
    }
}

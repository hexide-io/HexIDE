using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using static HexIDE.Integration.Tests.Views.ContainerRuntimeHarness;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Menus used from code at run time (#1126). Every expected line is what vb6.exe printed for the same statements on the
/// same menus, native and p-code alike: the probes and their logs are in docs/vb6-fidelity-oracle/menus-at-run-time-and-the-menu-bar.md, "Menus at run time,
/// and the menu bar".
/// </summary>
public class MenuMembersTests : WindowTestFixture
{
    // The probe's form: a Game menu holding New on F2, a separator, and the three-element array mnuLevel(0 To 2).
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Probe"
           Begin VB.Menu mnuGame
              Caption         =   "&Game"
              Begin VB.Menu mnuNew
                 Caption         =   "&New"
                 Shortcut        =   {F2}
              End
              Begin VB.Menu mnuSep1
                 Caption         =   "-"
              End
              Begin VB.Menu mnuLevel
                 Caption         =   "&Beginner"
                 Checked         =   -1  'True
                 Index           =   0
              End
              Begin VB.Menu mnuLevel
                 Caption         =   "&Intermediate"
                 Index           =   1
              End
              Begin VB.Menu mnuLevel
                 Caption         =   "&Expert"
                 Index           =   2
              End
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // ── on the menu itself ─────────────────────────────────────────────────────────────────────────

    private sealed class RecordingRoot : Window, IModuleExecutionRoot
    {
        public List<(string Name, IReadOnlyList<Vb6Value>? Args)> Calls { get; } = new();
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Calls.Add((name, args));
    }

    private static (Menu Bar, RecordingRoot Root, ModuleExecutionContext Ctx, ExecutionEnvironment Env) Hosted()
    {
        var (content, _, ctx, env) = Spawn(Form);
        var root = new RecordingRoot { Width = 400, Height = 300 };
        MergeAppResources(root);
        root.Content = content;
        root.Show();
        Dispatcher.UIThread.RunJobs();
        return (((DockPanel)content).Children.OfType<Menu>().Single(), root, ctx, env);
    }

    private static MenuItem Item(ItemsControl parent, string header) =>
        parent.Items.OfType<MenuItem>().First(i => (i.Header as string) == header);

    // #1158, with the twelve cases of #1159 (Codex's): choosing an item by pointer, Enter or automation must leave Checked
    // as the program set it until the item's own Click changes it. Avalonia's menu toggles a CheckBox item before it
    // raises Click; VBMenu puts the tick back in its class handler, which runs before any handler of the program's. The
    // Game menu's own Click, raised as it opens (#1344), is left out of the count.
    [AvaloniaTheory]
    [InlineData("invoke", false, false)]
    [InlineData("invoke", true, false)]
    [InlineData("invoke", false, true)]
    [InlineData("invoke", true, true)]
    [InlineData("click", false, false)]
    [InlineData("click", true, false)]
    [InlineData("click", false, true)]
    [InlineData("click", true, true)]
    [InlineData("Enter", false, false)]
    [InlineData("Enter", true, false)]
    [InlineData("Enter", false, true)]
    [InlineData("Enter", true, true)]
    public void Choosing_a_menu_preserves_Checked_until_its_handler_changes_it(string action, bool initial, bool change)
    {
        var (bar, root, ctx, env) = Hosted();
        try
        {
            var game = Item(bar, "&Game");
            var item = Item(game, initial ? "&Beginner" : "&Intermediate");
            var seen = new List<bool>();
            item.Click += (_, _) =>
            {
                seen.Add(item.IsChecked);
                if (change)
                    ContainerRuntimeHarness.Run(ctx, env,
                        $"mnuLevel({(initial ? 0 : 1)}).Checked = Not mnuLevel({(initial ? 0 : 1)}).Checked\r\n")
                        .GetAwaiter().GetResult();
            };
            game.Open();
            Dispatcher.UIThread.RunJobs();

            if (action == "click")
            {
                // The menu's pointer handler rather than a raised Click, delivered to the item: hit-testing a point is
                // order-dependent across headless tests, which change popup state.
                var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                var topLevel = TopLevel.GetTopLevel(item)!;
                var point = item.TranslatePoint(new Point(2, 2), topLevel)!.Value;
                item.RaiseEvent(new PointerPressedEventArgs(item, pointer, topLevel, point, 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                    KeyModifiers.None));
                item.RaiseEvent(new PointerReleasedEventArgs(item, pointer, topLevel, point, 1,
                    new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                    KeyModifiers.None, MouseButton.Left));
                pointer.Capture(null);
            }
            else if (action == "Enter")
            {
                item.Focus();
                item.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            }
            else
            {
                var result = UiAutomationDriver.Interact(item, action, null);
                result.Success.Should().BeTrue(result.Error);
            }
            Dispatcher.UIThread.RunJobs();

            seen.Should().Equal(new[] { initial }, "the handler must see the program's Checked value, exactly once");
            item.IsChecked.Should().Be(change ? !initial : initial);
            var chosen = root.Calls.Where(c => c.Name == "mnuLevel_Click").ToList();
            chosen.Should().ContainSingle();
            chosen[0].Args!.Single().Should().Be(new Vb6Value(initial ? 0 : 1));
        }
        finally { root.Close(); }
    }
}

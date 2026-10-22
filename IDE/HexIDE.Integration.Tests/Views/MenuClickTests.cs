using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// When a menu's Click is raised (#1344): as a menu that holds items opens, and for a shortcut, each menu's from the top
/// down to its item's, stopping at the first that is disabled or hidden. The menus are vb6.exe's probe's, and every step
/// prints what vb6.exe logged for the same keys, native and p-code alike (the oracle's "When a menu's Click is raised").
/// </summary>
public class MenuClickTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    // Top holds Alpha (Ctrl+Q), Sub, which holds Leaf (Ctrl+W), and Dis (Ctrl+E), disabled. Solo is a top-level menu with no
    // items; choosing it turns on, or off, Top's Click enabling Dis, as the probe's Timer did.
    private const string Probe =
        "VERSION 5.00\r\n"
        + "Begin VB.Form Form1\r\n"
        + "   Caption         =   \"mnprobe\"\r\n"
        + "   ClientHeight    =   3000\r\n"
        + "   ClientLeft      =   60\r\n"
        + "   ClientTop       =   750\r\n"
        + "   ClientWidth     =   4500\r\n"
        + "   ScaleHeight     =   3000\r\n"
        + "   ScaleWidth      =   4500\r\n"
        + "   Begin VB.Menu mnuTop\r\n"
        + "      Caption         =   \"&Top\"\r\n"
        + "      Begin VB.Menu mnuA\r\n"
        + "         Caption         =   \"&Alpha\"\r\n"
        + "         Shortcut        =   ^Q\r\n"
        + "      End\r\n"
        + "      Begin VB.Menu mnuSub\r\n"
        + "         Caption         =   \"&Sub\"\r\n"
        + "         Begin VB.Menu mnuLeaf\r\n"
        + "            Caption         =   \"&Leaf\"\r\n"
        + "            Shortcut        =   ^W\r\n"
        + "         End\r\n"
        + "      End\r\n"
        + "      Begin VB.Menu mnuDis\r\n"
        + "         Caption         =   \"&Dis\"\r\n"
        + "         Enabled         =   0   'False\r\n"
        + "         Shortcut        =   ^E\r\n"
        + "      End\r\n"
        + "   End\r\n"
        + "   Begin VB.Menu mnuSolo\r\n"
        + "      Caption         =   \"S&olo\"\r\n"
        + "   End\r\n"
        + "End\r\n"
        + "Attribute VB_Name = \"Form1\"\r\n"
        + "Option Explicit\r\n"
        + "Private enableInTop As Boolean\r\n"
        + "Private Sub mnuTop_Click()\r\n"
        + "    Debug.Print \"Top_Click dis=\" & mnuDis.Enabled\r\n"
        + "    If enableInTop Then mnuDis.Enabled = True\r\n"
        + "End Sub\r\n"
        + "Private Sub mnuA_Click()\r\n"
        + "    Debug.Print \"Alpha_Click\"\r\n"
        + "End Sub\r\n"
        + "Private Sub mnuSub_Click()\r\n"
        + "    Debug.Print \"Sub_Click\"\r\n"
        + "End Sub\r\n"
        + "Private Sub mnuLeaf_Click()\r\n"
        + "    Debug.Print \"Leaf_Click\"\r\n"
        + "End Sub\r\n"
        + "Private Sub mnuDis_Click()\r\n"
        + "    Debug.Print \"Dis_Click\"\r\n"
        + "End Sub\r\n"
        + "Private Sub mnuSolo_Click()\r\n"
        + "    enableInTop = Not enableInTop\r\n"
        + "    Debug.Print \"Solo_Click\"\r\n"
        + "End Sub\r\n"
        + "\r\n";

    private sealed class Rig(VBFormRuntime window, List<string> printed)
    {
        public VBMenu this[string name] =>
            window.GetLogicalDescendants().OfType<VBMenu>().Single(m => VBProps.GetName(m) == name);

        // Everything the program printed for one step.
        public string[] Step(Action step)
        {
            printed.Clear();
            step();
            for (var i = 0; i < 20; i++)
                Dispatcher.UIThread.RunJobs();
            return [.. printed];
        }

        public void Press(Key key) =>
            ((Control)window.Content!).RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.Control,
            });
    }

    private static void Run(Action<Rig> test)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, Probe, new NullSink())!);
        var printed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        var cancel = new CancellationTokenSource();
        try
        {
            _ = VBLoader.RunForm(project.StartupForm!, cancel.Token, out var window);
            for (var i = 0; i < 20; i++)
                Dispatcher.UIThread.RunJobs();
            test(new Rig(window, printed));
        }
        finally
        {
            cancel.Cancel();
            Dispatcher.UIThread.RunJobs();
            VBDebugConsole.Output -= print;
        }
    }

    [AvaloniaFact]
    public void A_menu_that_holds_items_raises_its_Click_as_it_opens()
    {
        Run(rig =>
        {
            rig.Step(() => rig["mnuTop"].IsSubMenuOpen = true).Should().Equal("Top_Click dis=False");
            rig.Step(() => rig["mnuSub"].IsSubMenuOpen = true).Should().Equal("Sub_Click");
            // Staying open, or closing, raises nothing more.
            rig.Step(() =>
            {
                rig["mnuSub"].IsSubMenuOpen = false;
                rig["mnuTop"].IsSubMenuOpen = false;
            }).Should().BeEmpty();
            // A top-level menu with no items raises its Click when chosen, as before.
            rig.Step(() => rig["mnuSolo"].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent))).Should().Equal("Solo_Click");
        });
    }

    [AvaloniaFact]
    public void A_shortcut_raises_each_menu_s_Click_from_the_top_down_to_its_item_s()
    {
        Run(rig =>
        {
            rig.Step(() => rig.Press(Key.Q)).Should().Equal("Top_Click dis=False", "Alpha_Click");
            rig.Step(() => rig.Press(Key.W)).Should().Equal("Top_Click dis=False", "Sub_Click", "Leaf_Click");
            // Dis is disabled, so only Top's Click is raised.
            rig.Step(() => rig.Press(Key.E)).Should().Equal("Top_Click dis=False");
        });
    }

    [AvaloniaFact]
    public void A_parent_s_Click_that_enables_the_item_lets_its_shortcut_through()
    {
        Run(rig =>
        {
            rig.Step(() => rig["mnuSolo"].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent))).Should().Equal("Solo_Click");
            // Top's Click enables Dis, and Dis's Enabled is asked only after it has run.
            rig.Step(() => rig.Press(Key.E)).Should().Equal("Top_Click dis=False", "Dis_Click");
        });
    }

    [AvaloniaFact]
    public void A_shortcut_stops_at_the_first_menu_that_is_disabled_or_hidden()
    {
        Run(rig =>
        {
            string[] With(string name, Action<VBMenu> change, Action<VBMenu> back, Key key)
            {
                change(rig[name]);
                try
                {
                    return rig.Step(() => rig.Press(key));
                }
                finally
                {
                    back(rig[name]);
                }
            }

            With("mnuTop", m => m.IsEnabled = false, m => m.IsEnabled = true, Key.Q).Should().BeEmpty();
            With("mnuTop", m => m.IsVisible = false, m => m.IsVisible = true, Key.Q).Should().BeEmpty();
            With("mnuA", m => m.IsVisible = false, m => m.IsVisible = true, Key.Q).Should().Equal("Top_Click dis=False");
            With("mnuSub", m => m.IsEnabled = false, m => m.IsEnabled = true, Key.W).Should().Equal("Top_Click dis=False");
            With("mnuSub", m => m.IsVisible = false, m => m.IsVisible = true, Key.W).Should().Equal("Top_Click dis=False");
            With("mnuLeaf", m => m.IsEnabled = false, m => m.IsEnabled = true, Key.W).Should().Equal("Top_Click dis=False", "Sub_Click");
        });
    }
}

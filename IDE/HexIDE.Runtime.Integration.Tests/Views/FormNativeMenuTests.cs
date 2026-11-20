using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #5065 and #5011 K1–K14: https://github.com/hexide-io/HexIDE/issues/5011#issuecomment-7031789606.
// Click/shortcut expectations follow #1344's measured menu chain; array operations follow #1126.
public class FormNativeMenuTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Probe = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption = "Native menus"
           ClientHeight = 3000
           ClientWidth = 4500
           Begin VB.Menu mnuTop
              Caption = "&Top"
              Begin VB.Menu mnuArray
                 Caption = "&Alpha"
                 Index = 0
                 Checked = -1
                 Shortcut = ^Q
              End
              Begin VB.Menu mnuSep
                 Caption = "-"
              End
              Begin VB.Menu mnuSub
                 Caption = "&Sub"
                 Begin VB.Menu mnuLeaf
                    Caption = "&Leaf"
                    Enabled = 0
                    Shortcut = ^W
                 End
              End
           End
           Begin VB.Menu mnuSolo
              Caption = "S&olo"
           End
        End
        Attribute VB_Name = "Form1"
        Private Sub mnuTop_Click()
            Debug.Print "Top"
            mnuLeaf.Enabled = True
        End Sub
        Private Sub mnuSub_Click()
            Debug.Print "Sub"
        End Sub
        Private Sub mnuLeaf_Click()
            Debug.Print "Leaf"
        End Sub
        Private Sub mnuSolo_Click()
            Debug.Print "Solo"
        End Sub
        Private Sub mnuArray_Click(Index As Integer)
            Debug.Print "Array" & Index
        End Sub
        Private Sub ChangeItems()
            mnuArray(0).Caption = "&Changed && saved"
            mnuArray(0).Enabled = False
            mnuArray(0).Checked = False
            mnuArray(0).Visible = False
        End Sub
        Private Sub WidenCaption()
            mnuTop.Caption = String(80, "W")
        End Sub
        Private Sub HideTop()
            mnuTop.Visible = False
        End Sub
        Private Sub ShowTop()
            mnuTop.Visible = True
        End Sub
        Private Sub HideMenus()
            mnuTop.Visible = False
            mnuSolo.Visible = False
        End Sub
        Private Sub AddItem()
            Load mnuArray(1)
        End Sub
        Private Sub RemoveItem()
            Unload mnuArray(1)
        End Sub
        Private Sub HideSeparator()
            mnuSep.Visible = False
        End Sub
        Private Sub ShowSeparator()
            mnuSep.Visible = True
        End Sub
        Private Sub Form_Resize()
            Debug.Print "Resize"
        End Sub
        """;

    private sealed class Rig(VBFormRuntime window, List<string> printed)
    {
        internal VBFormRuntime Window => window;
        internal Menu Bar => window.GetLogicalDescendants().OfType<Menu>().Single();
        internal NativeMenu Native => NativeMenu.GetMenu(window)!;
        internal NativeMenuItem Top => (NativeMenuItem)Native.Items[0];
        internal string[] Step(Action step)
        {
            printed.Clear();
            step();
            Pump();
            return [.. printed];
        }
        internal void Call(string name) => window.ExecuteSub(name);
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Run(Action<Rig> test, bool native = true)
    {
        var previous = FormNativeMenu.UseSystemMenuBar;
        FormNativeMenu.UseSystemMenuBar = native;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, Probe, new NullSink())!);
        var printed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        using var cancel = new CancellationTokenSource();
        try
        {
            _ = VBLoader.RunForm(project.StartupForm!, cancel.Token, out var window);
            Pump();
            test(new Rig(window, printed));
        }
        finally
        {
            cancel.Cancel();
            Pump();
            VBDebugConsole.Output -= print;
            FormNativeMenu.UseSystemMenuBar = previous;
        }
    }

    // Avalonia 12.1's menu bridge is PrivateApi; unlike the item's bridge it cannot be called in C# (#5065).
    private static MethodInfo MenuBridge(string raise)
    {
        var reason = $"Avalonia changed INativeMenuExporterEventsImplBridge; see how Avalonia.Native raises {raise} now";
        var bridge = typeof(NativeMenu).Assembly.GetType("Avalonia.Controls.INativeMenuExporterEventsImplBridge");
        bridge.Should().NotBeNull(reason);
        var method = bridge.GetMethod(raise);
        method.Should().NotBeNull(reason);
        return method;
    }

    private static MethodInfo OpeningBridge() => MenuBridge("RaiseOpening");
    private static MethodInfo NeedsUpdateBridge() => MenuBridge("RaiseNeedsUpdate");

    [Fact]
    public void Avalonia_menu_bridges_are_available()
    {
        OpeningBridge().Should().NotBeNull();
        NeedsUpdateBridge().Should().NotBeNull();
    }

    [AvaloniaFact]
    public void Mirror_preserves_tree_captions_state_and_empty_top_level_submenu()
    {
        Run(rig =>
        {
            rig.Native.Items.OfType<NativeMenuItem>().Select(i => i.Header).Should().Equal("Top", "Solo");
            var items = rig.Top.Menu!.Items;
            items.Should().HaveCount(3);
            items[1].Should().BeOfType<NativeMenuItemSeparator>();
            var alpha = (NativeMenuItem)items[0];
            alpha.Header.Should().Be("Alpha");
            alpha.IsChecked.Should().BeTrue();
            alpha.ToggleType.Should().Be(MenuItemToggleType.CheckBox);
            var sub = (NativeMenuItem)items[2];
            ((NativeMenuItem)sub.Menu!.Items[0]).IsEnabled.Should().BeFalse();
            ((NativeMenuItem)rig.Native.Items[1]).Menu!.Items.Should().BeEmpty();
            foreach (var item in new[] { rig.Top, alpha, sub, (NativeMenuItem)sub.Menu.Items[0] })
                item.Gesture.Should().BeNull();
            rig.Bar.Items.Should().HaveCount(2);
        });
    }

    private static string? IdOf(NativeMenuItemBase item) => item.GetValue(AutomationProperties.AutomationIdProperty);

    // Automation reads a form menu's code name from its native item (#5102), so the mirror copies it there.
    [AvaloniaFact]
    public void Native_items_carry_their_menus_code_names()
    {
        Run(rig =>
        {
            var sub = (NativeMenuItem)rig.Top.Menu!.Items[2];
            IdOf(rig.Top).Should().Be("mnuTop");
            IdOf(rig.Top.Menu.Items[0]).Should().Be("mnuArray(0)");
            IdOf(rig.Top.Menu.Items[1]).Should().Be("mnuSep", "a separator carries its name too");
            IdOf(sub).Should().Be("mnuSub");
            IdOf(sub.Menu!.Items[0]).Should().Be("mnuLeaf");
            IdOf(rig.Native.Items[1]).Should().Be("mnuSolo");
        });
    }

    [AvaloniaFact]
    public void A_loaded_array_element_carries_its_index_in_its_code_name()
    {
        Run(rig =>
        {
            rig.Step(() => rig.Call("AddItem"));
            IdOf(rig.Top.Menu!.Items[1]).Should().Be("mnuArray(1)");
        });
    }

    [AvaloniaFact]
    public void Native_window_omits_bar_height_and_keeps_its_load_time_setting()
    {
        double height = 0;
        Run(rig => { height = rig.Window.Height; rig.Bar.IsVisible.Should().BeTrue(); }, false);
        Run(rig =>
        {
            rig.Bar.IsVisible.Should().BeFalse();
            rig.Window.Height.Should().Be(height - VBLoader.MenuBarHeight);
            FormNativeMenu.UseSystemMenuBar = false;
            rig.Step(() => rig.Call("HideTop")).Should().BeEmpty();
            rig.Bar.IsVisible.Should().BeFalse();
            rig.Top.IsVisible.Should().BeFalse();
            ((NativeMenuItem)rig.Native.Items[1]).IsVisible.Should().BeTrue();
            rig.Step(() => rig.Call("HideMenus")).Should().BeEmpty();
            rig.Bar.IsVisible.Should().BeFalse();
            rig.Window.Height.Should().Be(height - VBLoader.MenuBarHeight);
            rig.Native.Items.OfType<NativeMenuItem>().Should().OnlyContain(i => !i.IsVisible);
            rig.Step(() => rig.Call("ShowTop")).Should().BeEmpty();
            rig.Top.IsVisible.Should().BeTrue();
            rig.Bar.IsVisible.Should().BeFalse();
            rig.Window.Height.Should().Be(height - VBLoader.MenuBarHeight);
        });
    }

    [AvaloniaFact]
    public void Native_click_dispatches_array_index_without_toggling_checked()
    {
        Run(rig =>
        {
            var item = (NativeMenuItem)rig.Top.Menu!.Items[0];
            rig.Step(() => ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked()).Should().Equal("Array0");
            item.IsChecked.Should().BeTrue();
        });
    }

    [AvaloniaFact]
    public void Opening_dispatches_holding_menu_before_showing_its_updated_children()
    {
        Run(rig =>
        {
            var sub = (NativeMenuItem)rig.Top.Menu!.Items[2];
            rig.Step(() => OpeningBridge().Invoke(rig.Top.Menu, null)).Should().Equal("Top");
            ((NativeMenuItem)sub.Menu!.Items[0]).IsEnabled.Should().BeTrue();
            rig.Step(() => OpeningBridge().Invoke(sub.Menu, null)).Should().Equal("Sub");
            rig.Step(() => OpeningBridge().Invoke(((NativeMenuItem)rig.Native.Items[1]).Menu, null)).Should().Equal("Solo");
        });
    }

    // AppKit raises NeedsUpdate on every menu the first time it searches them for a key equivalent, with no menu open
    // (#5065 review, Kronos). That must raise no Click: vb6.exe raises a menu's Click only as it opens (#1344).
    [AvaloniaFact]
    public void NeedsUpdate_alone_raises_no_Click()
    {
        Run(rig =>
        {
            var sub = (NativeMenuItem)rig.Top.Menu!.Items[2];
            rig.Step(() =>
            {
                NeedsUpdateBridge().Invoke(rig.Top.Menu, null);
                NeedsUpdateBridge().Invoke(sub.Menu, null);
                NeedsUpdateBridge().Invoke(((NativeMenuItem)rig.Native.Items[1]).Menu, null);
            }).Should().BeEmpty();
        });
    }

    [AvaloniaFact]
    public void Code_changes_update_the_existing_native_item()
    {
        Run(rig =>
        {
            var item = (NativeMenuItem)rig.Top.Menu!.Items[0];
            rig.Step(() => rig.Call("ChangeItems"));
            rig.Top.Menu.Items[0].Should().BeSameAs(item);
            item.Header.Should().Be("Changed & saved");
            item.IsEnabled.Should().BeFalse();
            item.IsVisible.Should().BeFalse();
            item.IsChecked.Should().BeFalse();
        });
    }

    [AvaloniaFact]
    public void Array_load_and_unload_keep_native_order_and_dispatch_new_index()
    {
        Run(rig =>
        {
            var first = rig.Top.Menu!.Items[0];
            rig.Step(() => rig.Call("AddItem"));
            rig.Top.Menu.Items.Should().HaveCount(4);
            rig.Top.Menu.Items[0].Should().BeSameAs(first);
            var added = (NativeMenuItem)rig.Top.Menu.Items[1];
            rig.Step(() => ((INativeMenuItemExporterEventsImplBridge)added).RaiseClicked()).Should().Equal("Array1");
            rig.Step(() => rig.Call("RemoveItem"));
            rig.Top.Menu.Items.Should().HaveCount(3);
            rig.Top.Menu.Items.Should().NotContain(added);
        });
    }

    [AvaloniaFact]
    public void Hidden_separator_leaves_and_returns_at_the_same_position()
    {
        Run(rig =>
        {
            var separator = rig.Top.Menu!.Items[1];
            rig.Step(() => rig.Call("HideSeparator"));
            rig.Top.Menu.Items.Should().HaveCount(2);
            rig.Top.Menu.Items.Should().NotContain(separator);
            rig.Step(() => rig.Call("ShowSeparator"));
            rig.Top.Menu.Items[1].Should().BeSameAs(separator);
        });
    }

    [AvaloniaFact]
    public void Native_caption_edits_do_not_refuse_a_change_that_would_wrap_the_in_window_bar()
    {
        Run(rig =>
        {
            rig.Step(() => rig.Call("WidenCaption")).Should().BeEmpty();
            rig.Top.Header.Should().Be(new string('W', 80));
            rig.Bar.IsVisible.Should().BeFalse();
        });
    }

    [AvaloniaFact]
    public void Native_menu_keeps_the_window_shortcut_chain()
    {
        var previous = FormNativeMenu.UseSystemMenuBar;
        FormNativeMenu.UseSystemMenuBar = true;
        try
        {
            var tests = new MenuClickTests();
            tests.A_shortcut_raises_each_menu_s_Click_from_the_top_down_to_its_item_s();
            tests.A_parent_s_Click_that_enables_the_item_lets_its_shortcut_through();
            tests.A_shortcut_stops_at_the_first_menu_that_is_disabled_or_hidden();
        }
        finally
        {
            FormNativeMenu.UseSystemMenuBar = previous;
        }
    }
}

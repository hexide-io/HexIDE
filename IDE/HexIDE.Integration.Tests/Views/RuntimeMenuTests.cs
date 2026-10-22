using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using static HexIDE.Integration.Tests.Views.ContainerRuntimeHarness;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Issue #85 — a form's menus never appeared when the project ran.
///
/// The bar was docked and left empty, while every menu WAS instantiated into a MenuItem and placed on the
/// form's canvas — where a menu has no Left, Top, Width or Height, so each one landed as a zero-sized child
/// at the origin. Net effect: no menu bar, and a stray invisible control per menu item.
///
/// The tree these need was already there. #83 records a menu's sub-items on the parent so the writer can
/// walk them; this is the other consumer of the same tree.
/// </summary>
public class RuntimeMenuTests
{
    /// <summary>Stands in for the running form, which is what dispatches a VB6 event procedure.</summary>
    private sealed class RecordingRoot : Window, IModuleExecutionRoot
    {
        public List<string> Executed { get; } = new();
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Executed.Add(name);
    }

    // A File menu of the shape VB6's own templates use: nested items, shortcuts, and a separator spelled as
    // a caption of one hyphen.
    private const string MenuForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.CommandButton Command1
              Caption         =   "On the form"
              Left            =   300
              Top             =   300
              Width           =   1200
              Height          =   375
           End
           Begin VB.TextBox Text1
              Left            =   300
              Top             =   900
              Width           =   1200
              Height          =   375
           End
           Begin VB.Menu mnuFile
              Caption         =   "&File"
              Begin VB.Menu mnuFileNew
                 Caption         =   "&New"
                 Shortcut        =   ^N
              End
              Begin VB.Menu mnuFileBar1
                 Caption         =   "-"
              End
              Begin VB.Menu mnuFileExit
                 Caption         =   "E&xit"
              End
           End
           Begin VB.Menu mnuHelp
              Caption         =   "&Help"
              Begin VB.Menu mnuHelpAbout
                 Caption         =   "&About"
                 Shortcut        =   {F1}
              End
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private const string NoMenuForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.CommandButton Command1
              Caption         =   "Only me"
              Left            =   300
              Top             =   300
              Width           =   1200
              Height          =   375
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static (Menu bar, Canvas canvas, RecordingRoot root) Run(string frm)
    {
        var (content, canvas, _, _) = Spawn(frm);
        var root = new RecordingRoot { Width = 400, Height = 300 };
        // The application's own dictionaries, so what these tests render is what a user sees. Without them
        // the menu draws against a theme set that exists nowhere else, which is how a separator shipped
        // twice — once invisible, once as a dot — under a green suite.
        MergeAppResources(root);
        root.Content = content;
        root.Show();
        Dispatcher.UIThread.RunJobs();
        var bar = ((DockPanel)content).Children.OfType<Menu>().Single();
        return (bar, canvas, root);
    }

    private static MenuItem Item(ItemsControl parent, string header) =>
        parent.Items.OfType<MenuItem>().First(i => (i.Header as string) == header);

    // ── the bar ──────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TheMenuBar_HoldsTheTopLevelMenus()
    {
        var (bar, _, _) = Run(MenuForm);

        bar.Items.OfType<MenuItem>().Select(i => i.Header as string).Should().Equal("&File", "&Help");
    }

    [AvaloniaFact]
    public void SubItems_NestUnderTheirParent()
    {
        var (bar, _, _) = Run(MenuForm);

        var file = Item(bar, "&File");
        file.Items.OfType<MenuItem>().Select(i => i.Header as string).Should().Equal("&New", "E&xit");
        Item(bar, "&Help").Items.OfType<MenuItem>().Select(i => i.Header as string).Should().Equal("&About");
    }

    [AvaloniaFact]
    public void ASingleHyphenCaption_BecomesASeparator()
    {
        var (bar, _, _) = Run(MenuForm);

        // VB6 spells a separator as a menu item whose caption is one hyphen. Rendering it as an ordinary
        // item would put a literal "-" in the middle of the File menu.
        var file = Item(bar, "&File");
        file.Items.Should().HaveCount(3);
        file.Items[1].Should().BeAssignableTo<Separator>();
    }

    [AvaloniaFact]
    public void ASeparator_HasAVisibleLineToDraw()
    {
        var (bar, _, _) = Run(MenuForm);
        var file = Item(bar, "&File");
        var separator = file.Items.OfType<Separator>().Single();

        file.Open();
        Dispatcher.UIThread.RunJobs();

        // Being a Separator is not enough, which is how this shipped broken: the object was in the menu and
        // the space was reserved, but the base theme paints it WhiteSmoke — invisible against a menu — and
        // with HexIDE's own dictionaries merged it collapses to zero height and is not drawn at all. Both
        // look exactly like a separator that was never added, and asserting only the type passes for both.
        var line = separator.GetVisualDescendants().OfType<Border>().FirstOrDefault();
        line.Should().NotBeNull("the separator must template into something that paints");
        line!.Background.Should().NotBeNull("a line with no brush is a line nobody can see");

        // And it has to be a RULE, not a mark. Asserting only that something is painted is not enough: the
        // IDE's Classic theme styles every Separator as a 1px VERTICAL toolbar divider, which meets the 1px
        // height here and renders as a two-pixel dot in the middle of the menu. It is brushed, it is present,
        // and it is not a separator.
        separator.Bounds.Width.Should().BeGreaterThan(separator.Bounds.Height * 10,
            "a menu separator spans the menu rather than marking a point in it");
    }

    [AvaloniaFact]
    public void Menus_AreNotPlacedOnTheFormCanvas()
    {
        var (_, canvas, _) = Run(MenuForm);

        // The other half of the defect. Every menu used to be instantiated and placed here, with no size and
        // no position, so a form with a twenty-item menu carried twenty invisible controls at the origin.
        canvas.Children.OfType<Control>().Select(NameOf).Should().Equal("Command1", "Text1");
    }

    [AvaloniaFact]
    public void AFormWithNoMenus_HasNoMenuBar()
    {
        var (bar, _, _) = Run(NoMenuForm);

        // An empty bar still occupies a strip and pushes the form's canvas down, so the same form would be
        // laid out differently from how VB6 lays it out.
        bar.IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void AnAmpersandInACaption_MarksAnAccessKeyRatherThanPrinting()
    {
        var (bar, _, _) = Run(MenuForm);
        var file = Item(bar, "&File");

        // VB6 marks a menu's access key with an ampersand: "&File" is drawn as File with the F underlined,
        // and Alt+F opens it. Until the header went through the access-text template, every menu on every
        // running form read "&File" with the ampersand printed.
        //
        // The underline itself follows the Windows convention rather than being painted permanently — it
        // appears once access keys are being shown, which is what pressing Alt does. That is what VB6 does
        // on a modern Windows too, since both defer to the same system setting.
        var access = file.GetVisualDescendants().OfType<AccessText>().FirstOrDefault();
        access.Should().NotBeNull("the caption must render through AccessText for Alt to reach it");

        // "_File" is AccessText's marker form: it draws "File" and underlines the F. What matters is that no
        // ampersand survives into what is drawn, and that the letter after the marker is the access key.
        access!.Text.Should().Be("_File");
        access.Text.Should().NotContain("&", "an ampersand in a VB6 caption marks a key, it is not printed");
    }

    // ── dispatch ─────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ClickingAMenuItem_RunsItsEventProcedure()
    {
        var (bar, _, root) = Run(MenuForm);

        Item(Item(bar, "&File"), "E&xit").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        root.Executed.Should().Equal("mnuFileExit_Click");
    }

    [AvaloniaFact]
    public void ClickingASubItem_DoesNotAlsoRunItsParents()
    {
        var (bar, _, root) = Run(MenuForm);

        // Click bubbles, so without a source check the parent menu's handler runs too. VB6 raises a menu's Click as it
        // opens (measured, #1344), not when one of its items is chosen, and nothing opened Help here.
        Item(Item(bar, "&Help"), "&About").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        root.Executed.Should().Equal("mnuHelpAbout_Click");
        root.Executed.Should().NotContain("mnuHelp_Click");
    }

    [AvaloniaFact]
    public void ASubItemDispatches_EvenThoughItLivesInAPopup()
    {
        var (bar, _, root) = Run(MenuForm);
        var about = Item(Item(bar, "&Help"), "&About");

        // The reason dispatch goes through the bar rather than through the item. A sub-item is realised in a
        // popup with its own visual root, so walking up from the item — which is how every other control
        // finds the form — leaves the window and finds nothing.
        about.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        root.Executed.Should().ContainSingle();
    }

    // VB6 leaves Checked to the program. Avalonia's menu flips a CheckBox item's IsChecked before it raises Click, so a
    // guard that read IsChecked inside OnClick read the flipped value and kept it: a person's click ticked the item.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClickingAMenuItemWithThePointer_LeavesItsCheckedAsTheProgramSetIt(bool programChecked)
    {
        var (bar, _, root) = Run(MenuForm);
        var file = Item(bar, "&File");
        var newItem = Item(file, "&New");
        ((HexIDE.Runtime.BuiltinControls.VBMenu)newItem).Checked = programChecked;
        file.Open();
        Dispatcher.UIThread.RunJobs();
        var popupRoot = TopLevel.GetTopLevel(newItem)!;
        var centre = newItem.TranslatePoint(new Point(newItem.Bounds.Width / 2, newItem.Bounds.Height / 2), popupRoot)!.Value;

        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(popupRoot, centre, MouseButton.Left);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(popupRoot, centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // File's Click as it opened, as VB6 raises it (measured, #1344), then the item's.
        root.Executed.Should().Equal("mnuFile_Click", "mnuFileNew_Click");
        newItem.IsChecked.Should().Be(programChecked);
    }

    // interact invoke clicks as Avalonia's menu does, tick included, so it meets the same guard (#947).
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvokingAMenuItemThroughAutomation_LeavesItsCheckedAsTheProgramSetIt(bool programChecked)
    {
        var (bar, _, root) = Run(MenuForm);
        var file = Item(bar, "&File");
        var newItem = (HexIDE.Runtime.BuiltinControls.VBMenu)Item(file, "&New");
        newItem.Checked = programChecked;
        HexIDE.Automation.UiAutomationDriver.Interact(file, "expand", null);
        Dispatcher.UIThread.RunJobs();

        var outcome = HexIDE.Automation.UiAutomationDriver.Interact(newItem, "invoke", null);
        Dispatcher.UIThread.RunJobs();

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().NotContain("ticked");
        // Expanding File raised its Click, as opening it does in VB6 (measured, #1344).
        root.Executed.Should().Equal("mnuFile_Click", "mnuFileNew_Click");
        newItem.IsChecked.Should().Be(programChecked);
        file.IsSubMenuOpen.Should().BeFalse();
    }

    // ── shortcuts ────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AShortcut_IsShownOnTheItem()
    {
        var (bar, _, _) = Run(MenuForm);

        Item(Item(bar, "&File"), "&New").InputGesture
            .Should().Be(new KeyGesture(Key.N, KeyModifiers.Control));
        Item(Item(bar, "&Help"), "&About").InputGesture
            .Should().Be(new KeyGesture(Key.F1));
    }

    [AvaloniaFact]
    public void PressingAShortcutWhileTypingInATextBox_RunsTheEventProcedure()
    {
        var (_, canvas, root) = Run(MenuForm);
        var text = canvas.Children.OfType<Control>().First(c => NameOf(c) == "Text1");

        text.Focus();
        Dispatcher.UIThread.RunJobs();
        // Focusing the TextBox raises its GotFocus (#1061). The shortcut is what is under test, so only what it runs counts.
        root.Executed.Clear();

        // The case the whole binding exists for. A shortcut that only works when the menu bar happens to
        // have focus is not a shortcut — VB6 fires Ctrl+S from wherever the user is typing.
        text.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.N,
            KeyModifiers = KeyModifiers.Control,
        });
        Dispatcher.UIThread.RunJobs();

        // The menu above the item first, as vb6.exe raises a shortcut's Clicks (measured, #1344).
        root.Executed.Should().Equal("mnuFile_Click", "mnuFileNew_Click");
    }

    [Theory]
    [InlineData("^N", Key.N, KeyModifiers.Control)]
    [InlineData("^{F4}", Key.F4, KeyModifiers.Control)]
    [InlineData("{F1}", Key.F1, KeyModifiers.None)]
    [InlineData("+{F1}", Key.F1, KeyModifiers.Shift)]
    [InlineData("^{INSERT}", Key.Insert, KeyModifiers.Control)]
    [InlineData("^{DEL}", Key.Delete, KeyModifiers.Control)]
    [InlineData("^+S", Key.S, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("%{F4}", Key.F4, KeyModifiers.Alt)]
    public void VB6ShortcutSyntax_IsUnderstood(string raw, Key key, KeyModifiers modifiers)
    {
        // VB6 writes these into the .frm in its own small syntax: a modifier prefix, then a character or a
        // braced key name.
        VBMenuShortcut.TryParse(raw, out var gesture).Should().BeTrue();
        gesture.Should().Be(new KeyGesture(key, modifiers));
    }

    [Theory]
    [InlineData("")]
    [InlineData("^")]
    [InlineData("{NOTAKEY}")]
    [InlineData("^{}")]
    public void SomethingThatIsNotAShortcut_IsDeclinedRatherThanGuessedAt(string raw)
    {
        VBMenuShortcut.TryParse(raw, out _).Should().BeFalse();
    }
}

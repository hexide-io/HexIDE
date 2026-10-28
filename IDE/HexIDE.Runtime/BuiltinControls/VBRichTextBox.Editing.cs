using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.RichText;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBRichTextBox
{
    private readonly Stack<(RtfDocument Document, int Start, int Length)> undo = new();
    private RtfDocument? lastInputDocument;
    private Vector scrollOffset;
    private int? pressedPosition;

    static VBRichTextBox()
    {
        AttachedEvents.AttachMouse<VBRichTextBox>(AttachedEvents.DblClick.InPlaceOfMouseDown);
        AttachedEvents.AttachClick<VBRichTextBox>();
        PointerPressedEvent.AddClassHandler<VBRichTextBox>((box, e) => box.Guarded(e, () => box.Press(e)));
        PointerReleasedEvent.AddClassHandler<VBRichTextBox>((box, e) => box.Guarded(e, () => box.Release(e)));
    }

    private void RememberEdit()
    {
        if (lastInputDocument != null && !ReferenceEquals(Document, lastInputDocument)) undo.Clear();
        undo.Push((Document, Start, Length));
    }

    private void UndoInput()
    {
        if (!CanEdit || undo.Count == 0) return;
        if (!ReferenceEquals(Document, lastInputDocument))
            throw new NotImplementedException("RichTextBox Undo after a code edit is not implemented (#1100)");
        var previous = undo.Pop();
        Document = previous.Document;
        lastInputDocument = Document;
        Select(previous.Start, previous.Length, true);
        Changed();
    }

    private bool Shortcut(KeyEventArgs e)
    {
        var command = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        if (e.KeyModifiers != command) return false;
        switch (e.Key)
        {
            case Key.A:
                Select(0, Document.Text.Length);
                RaiseKey(ComponentBaseClass.KeyPressEvent, 1);
                return true;
            case Key.B:
            case Key.U:
                RaiseKey(ComponentBaseClass.KeyPressEvent, e.Key == Key.B ? 2 : 21);
                return true;
            case Key.I:
                RaiseKey(ComponentBaseClass.KeyPressEvent, 9);
                InsertFromInput("\t");
                return true;
            case Key.E:
            case Key.R:
            case Key.L:
                if (CanEdit)
                {
                    RememberEdit();
                    Align(e.Key == Key.E ? "qc" : e.Key == Key.R ? "qr" : "ql");
                    lastInputDocument = Document;
                }
                return true;
            case Key.Z:
                UndoInput();
                return true;
            case Key.C:
                CopyInput();
                return true;
            case Key.V:
                PasteFromKey();
                return true;
            default:
                return false;
        }
    }

    private void CopyInput()
    {
        if (Length == 0) return;
        // Only the plain-text clipboard probe is measured. Do not silently flatten formatted selections.
        if (SelectedRuns.Any(r => r.Format != new RtfCharacterFormat() || r.Paragraph != new RtfParagraphFormat()))
            throw new NotImplementedException("RichTextBox formatted clipboard copying is not implemented (#1100)");
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            CopyText(clipboard, Document.Text.Substring(Start, Math.Min(Length, Document.Text.Length - Start)));
    }

    private static async void CopyText(Avalonia.Input.Platform.IClipboard clipboard, string text) =>
        await clipboard.SetTextAsync(text);

    // An async void that throws reaches the UI thread's context, so its refusal takes the same route as the handlers'.
    private async void PasteFromKey()
    {
        try { await PasteInput(); }
        catch (NotImplementedException refusal) { Refuse(refusal); }
    }

    internal async Task PasteInput()
    {
        if (!CanEdit) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if ((await clipboard.GetDataFormatsAsync()).Any(format => format != DataFormat.Text))
            throw new NotImplementedException("RichTextBox formatted clipboard pasting is not implemented (#1100)");
        if (await clipboard.TryGetTextAsync() is { } text) InsertFromInput(text);
    }

    private int HitPosition(Point point)
    {
        using var layout = CreateLayout();
        var hit = layout.HitTestPoint(point - new Point(2, 2) + scrollOffset);
        return Math.Clamp(hit.CharacterHit.FirstCharacterIndex + hit.CharacterHit.TrailingLength, 0, Document.Text.Length);
    }

    private void Press(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind is not
            (PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed)) return;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        if (e.KeyModifiers != KeyModifiers.None)
            throw new NotImplementedException("RichTextBox modified mouse selection is not implemented (#1100)");
        Focus();
        var position = HitPosition(e.GetPosition(this));
        if (e.ClickCount % 2 == 0 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var text = Document.Text;
            var from = position;
            var to = position;
            while (from > 0 && !char.IsWhiteSpace(text[from - 1])) from--;
            while (to < text.Length && !char.IsWhiteSpace(text[to])) to++;
            // Only an ASCII word with its trailing space has recorded selection endpoints.
            if (position >= text.Length || !char.IsAsciiLetter(text[position]) || to == text.Length
                || text[to] != ' ' || text[from..to].Any(c => !char.IsAsciiLetter(c)))
                throw new NotImplementedException("RichTextBox other word selection boundaries are not implemented (#1100)");
            to++;
            Select(from, to - from);
            pressedPosition = null;
        }
        else pressedPosition = position;
        e.Pointer.Capture(this);
    }

    private void Release(PointerReleasedEventArgs e)
    {
        if (pressedPosition is { } position)
        {
            pressedPosition = null;
            if (HitPosition(e.GetPosition(this)) != position)
            {
                e.Pointer.Capture(null);
                throw new NotImplementedException("RichTextBox drag selection is not implemented (#1100)");
            }
            Select(position, 0);
        }
        e.Pointer.Capture(null);
    }

    private void KeepCaretVisible()
    {
        if (Bounds.Width <= 4 || Bounds.Height <= 4) return;
        using var layout = CreateLayout();
        var caret = layout.HitTestTextPosition(Math.Min(selectionAnchor.HasValue ? selectionEnd : Start, Document.Text.Length));
        var x = scrollOffset.X;
        var y = scrollOffset.Y;
        if (caret.X < x) x = caret.X;
        else if (caret.Right > x + Bounds.Width - 4) x = caret.Right - Bounds.Width + 4;
        if (caret.Y < y) y = caret.Y;
        else if (caret.Bottom > y + Bounds.Height - 4) y = caret.Bottom - Bounds.Height + 4;
        scrollOffset = new Vector(Math.Max(0, x), Math.Max(0, y));
    }
}

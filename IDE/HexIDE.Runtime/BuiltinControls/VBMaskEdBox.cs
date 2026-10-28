using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>The MaskEdBox's measured text and selection members (#1098). Keyboard editing follows the measured insert and removal rules.</summary>
public sealed class VBMaskEdBox : TextBox, ICSharpPropertyBag
{
    protected override Type StyleKeyOverride => typeof(TextBox);
    internal MaskedEdBoxModel Model { get; } = new();

    internal Task ClipboardOperation { get; private set; } = Task.CompletedTask;

    public VBMaskEdBox()
    {
        IsReadOnly = true;
        Model.ValidationError += (text, at) =>
        {
            foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), "ValidationError"))
            {
                var args = new List<Vb6Value>();
                if (handler.Own && VBProps.GetIndex(this) is { } index) args.Add(new(index));
                args.Add(new(text));
                args.Add(new((long)at));
                handler.Root.ExecuteSub(handler.Name, args);
            }
        };
        Model.Changed += () =>
        {
            RefreshText();
            this.ExecuteSub(TextBoxComponentClass.ChangeEvent);
        };
    }

    private void ReadSelection()
    {
        Model.SelStart = Math.Min(SelectionStart, SelectionEnd);
        Model.SelLength = Math.Abs(SelectionEnd - SelectionStart);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (e.Handled || string.IsNullOrEmpty(e.Text)) return;
        e.Handled = true;
        ReadSelection();
        foreach (var character in e.Text)
            TextBoxKeyEvents.Deliver(this, ComponentBaseClass.KeyPressEvent, character, null, ascii =>
            {
                if (ascii != character) throw Refused("KeyAscii modification");
                Model.TypeCharacter(character);
                RefreshText();
            });
    }

    private void RaiseKey(KeyEventArgs e, EventClass ev)
    {
        var code = e.Key switch
        {
            >= Key.A and <= Key.Z => 65 + (int)e.Key - (int)Key.A,
            >= Key.D0 and <= Key.D9 => 48 + (int)e.Key - (int)Key.D0,
            Key.Back => 8, Key.Delete => 46, Key.Home => 36, Key.End => 35,
            Key.Left => 37, Key.Right => 39, Key.Space => 32,
            Key.LeftShift or Key.RightShift => 16, Key.OemMinus => 189,
            _ => 0,
        };
        if (code == 0) return;
        var shift = (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 2 : 0)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 4 : 0);
        TextBoxKeyEvents.Deliver(this, ev, code, shift, result =>
        {
            if (result != code) throw Refused("KeyCode modification");
        });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled) return;
        ReadSelection();
        if (CopyGesture?.Matches(e) == true || CutGesture?.Matches(e) == true || PasteGesture?.Matches(e) == true)
        {
            e.Handled = true;
            ClipboardKey(e);
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)
            || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) throw Refused("modified key input");
        // Delete changes the entry before KeyDown; Backspace changes it after KeyPress.
        if (e.Key == Key.Delete) Model.Remove(false);
        RaiseKey(e, ComponentBaseClass.KeyDownEvent);
        if (e.Key == Key.Back)
            TextBoxKeyEvents.Deliver(this, ComponentBaseClass.KeyPressEvent, 8, null, ascii =>
            {
                if (ascii != 8) throw Refused("KeyAscii modification");
                Model.Remove(true);
                RefreshText();
            });
        else if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            if (e.KeyModifiers != KeyModifiers.None) throw Refused("modified navigation");
            if (Model.SelLength != 0) throw Refused("navigation with a selection");
            Model.SelStart = e.Key switch
            {
                Key.Left => Math.Max(0, Model.SelStart - 1),
                Key.Right => Model.SelStart + 1,
                Key.Home => 0,
                _ => Model.DisplayText.Length,
            };
        }
        RefreshText();
        e.Handled = true;
    }

    private async void ClipboardKey(KeyEventArgs e)
    {
        ClipboardOperation = TransferClipboard(e);
        await ClipboardOperation;
    }

    private async Task TransferClipboard(KeyEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw Refused("clipboard without a window");
        if (PasteGesture?.Matches(e) == true)
        {
            var value = await clipboard.TryGetTextAsync() ?? throw Refused("non-text clipboard");
            Model.Paste(value);
        }
        else
        {
            var selected = Model.CopySelection(CutGesture?.Matches(e) == true);
            await clipboard.SetTextAsync(selected);
        }
        RefreshText();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Handled) return;
        RaiseKey(e, ComponentBaseClass.KeyUpEvent);
        e.Handled = true;
    }

    private void RefreshText()
    {
        // Text's PromptInclude reading is independent of the prompts displayed in the box.
        Text = Model.DisplayText;
        CaretIndex = Model.SelStart;
        SelectionStart = Model.SelStart;
        SelectionEnd = Model.SelStart + Model.SelLength;
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = default;
        var member = MaskedEdBoxComponentClass.Instance.Member(name);
        if (member is null)
        {
            if (name.StartsWith("Font", StringComparison.OrdinalIgnoreCase)) throw Refused(name);
            return false;
        }
        value = member.Name switch
        {
            "Text" => new(Model.Text),
            "ClipText" => new(Model.ClipText),
            "FormattedText" => new(Model.FormattedText),
            "Mask" => new(Model.Mask),
            "PromptChar" => new(Model.PromptChar),
            "PromptInclude" => new(Model.PromptInclude),
            "AllowPrompt" => new(Model.AllowPrompt),
            "AutoTab" => new(Model.AutoTab),
            "ClipMode" => new((long)Model.ClipMode),
            "MaxLength" when Model.MaxLength <= short.MaxValue && !Model.Mask.Any(c => c is '<' or '>' or '\\') => new(Model.MaxLength),
            "SelStart" => new((long)Model.SelStart),
            "SelLength" => new((long)Model.SelLength),
            "SelText" => new(Model.SelText),
            _ => throw Refused(name),
        };
        return true;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        var member = MaskedEdBoxComponentClass.Instance.Member(name);
        if (member is null)
        {
            if (name.StartsWith("Font", StringComparison.OrdinalIgnoreCase)) throw Refused(name);
            return false;
        }
        // Only assignments of the measured property type are pinned; conversions need a probe.
        switch (member.Name)
        {
            case "Text": Model.Text = String(value); break;
            case "Mask": Model.Mask = String(value); break;
            case "PromptChar": Model.PromptChar = String(value); break;
            case "PromptInclude": Model.PromptInclude = Boolean(value); break;
            case "AllowPrompt": Model.AllowPrompt = Boolean(value); break;
            case "AutoTab": Model.AutoTab = Boolean(value); break;
            case "ClipMode": Model.ClipMode = Integer(value); break;
            case "MaxLength":
                if (value.Type != Vb6Value.ValueType.Integer) throw Refused("MaxLength conversion");
                Model.MaxLength = Integer(value);
                break;
            case "SelText": Model.SetSelectedText(String(value)); break;
            case "SelStart": Model.SelStart = Integer(value); break;
            case "SelLength": Model.SelLength = Integer(value); break;
            default: throw Refused(name + " assignment");
        }
        RefreshText();
        return true;
    }

    private static string String(Vb6Value value) => value.Type == Vb6Value.ValueType.String
        ? (string)value.Value! : throw Refused("String conversion");
    private static bool Boolean(Vb6Value value) => value.Type == Vb6Value.ValueType.Boolean
        ? (bool)value.Value! : throw Refused("Boolean conversion");
    private static int Integer(Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long)
            throw Refused("integer conversion");
        var number = Convert.ToInt64(value.Value, System.Globalization.CultureInfo.InvariantCulture);
        return number is >= int.MinValue and <= int.MaxValue ? (int)number : throw Refused("large integer assignment");
    }
    internal static NotImplementedException Refused(string name) => new($"MaskEdBox {name} is not implemented (#1098)");
}

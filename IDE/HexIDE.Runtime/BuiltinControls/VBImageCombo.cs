using Avalonia.Controls.Primitives;
using Avalonia.Input;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.BuiltinControls;

// Loaded only: the designer keeps the original block until the writer is implemented (#1092).
internal sealed class VBImageCombo : TemplatedControl, ICSharpAsyncProxy, ICSharpPropertyBag
{
    internal ImageComboModel Model { get; }
    internal VBImageCombo(EventPump events, Func<Task> change) => Model = new ImageComboModel(events, change);

    internal void Load(VBSerializedComponent block)
    {
        VBProps.SetName(this, block.Name);
        AttachedProperties.SetFont(this, VBFont.Default);
        foreach (var (name, value) in block.Properties)
        {
            var number = value is string or Dictionary<string, object> ? 0 : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            switch (name.ToLowerInvariant())
            {
                case "text": Model.LoadText((string)value); break;
                case "locked": Model.TrySetProperty("Locked", new Vb6Value(number != 0)); break;
                case "indentation":
                    if (number is not (0 or 2)) throw ImageComboItems.Unmeasured();
                    Model.LoadIndentation((int)number);
                    break;
                case "left": Avalonia.Controls.Canvas.SetLeft(this, number / 15); break;
                case "top": Avalonia.Controls.Canvas.SetTop(this, number / 15); break;
                case "width": Width = number / 15; break;
                case "height": Height = number / 15; break;
                case "enabled": IsEnabled = number != 0; break;
                case "visible": IsVisible = number != 0; break;
                case "backcolor": BackColor = unchecked((int)(long)number); break;
                case "forecolor": ForeColor = unchecked((int)(long)number); break;
                case "mousepointer": MousePointer = (int)number; break;
                case "oledragmode": OLEDragMode = (int)number; break;
                case "oledropmode": OLEDropMode = (int)number; break;
                case "font":
                    var fields = (Dictionary<string, object>)value;
                    double Metric(string key) => Convert.ToDouble(fields[key], System.Globalization.CultureInfo.InvariantCulture);
                    AttachedProperties.SetFont(this, new VBFont((string)fields["Name"], Metric("Size"),
                        italic: Metric("Italic") != 0, charset: (int)Metric("Charset"),
                        underline: Metric("Underline") != 0, strikethrough: Metric("Strikethrough") != 0,
                        weight: (int)Metric("Weight")));
                    break;
                case "tabindex": KeyboardNavigation.SetTabIndex(this, (int)number); break;
                case "imagelist": // Binding is the next slice; do not silently report Nothing.
                case "_extentx": case "_extenty": case "_version": break;
                default: throw ImageComboItems.Unmeasured();
            }
        }
    }

    internal int BackColor { get; private set; } = unchecked((int)0x8000000F);
    internal int ForeColor { get; private set; } = unchecked((int)0x80000012);
    internal int MousePointer { get; private set; }
    internal int OLEDragMode { get; private set; }
    internal int OLEDropMode { get; private set; }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        // Leave the other owner members refused; this slice only connects the existing model.
        if (name.ToLowerInvariant() is "name" or "left" or "top" or "width" or "height" or "visible")
        {
            value = default;
            return false;
        }
        return Model.TryGetProperty(name, out value);
    }
    public bool TrySetProperty(string name, Vb6Value value) => Model.TrySetProperty(name, value);
    public void Call(string method, List<Vb6Value> args) => Model.Call(method, args);
    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args) => Model.CallAsync(method, args);
}

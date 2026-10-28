using Avalonia.Controls;
using Avalonia.LogicalTree;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

internal static class TabStripMembers
{
    internal static Vb6Value Read(VBTabStrip strip, string name, IReadOnlyList<Vb6Value> args)
    {
        if (args.Count > 1 || args.Count != 0 && !name.Equals("Tabs", StringComparison.OrdinalIgnoreCase))
            throw Refused(name + " indexed access");
        if (name.Equals("Tabs", StringComparison.OrdinalIgnoreCase))
            return args.Count == 0 ? new Vb6Value(strip.Tabs) : strip.Tabs.Item(args[0]);
        if (name.Equals("SelectedItem", StringComparison.OrdinalIgnoreCase))
            return strip.Model.SelectedItem is { } selected ? new Vb6Value(strip.Tabs.Wrap(selected)) : Vb6Value.Nothing;
        if (name.Equals("ImageList", StringComparison.OrdinalIgnoreCase))
            return Vb6Value.Nothing;
        if (strip.Values.TryGetValue(name, out var value))
            return value;
        if (name.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
            return new Vb6Value(strip.IsEnabled);
        throw Refused(name);
    }

    internal static bool IsOwn(string name) => TabStripComponentClass.Instance.Member(name) is not null;
    internal static NotImplementedException Refused(string name) => new($"TabStrip {name} is not implemented (#1089)");

    internal static async Task<bool> Set(object? target, string name, Vb6Value value,
        BasicInterpreter interpreter, ModuleInfo module, ExecutionEnvironment env)
    {
        if (target is TabStripTab tab && name.Equals("Selected", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Type != Vb6Value.ValueType.Boolean)
                throw Refused("Selected conversion");
            if ((bool)value.Value! || ReferenceEquals(tab.Owner.Model.SelectedItem, tab.Tab))
                await Select(tab.Owner, tab.Tab, interpreter, module, env);
            else
                throw Refused("unselected Tab.Selected = False");
            return true;
        }
        if (target is not VBTabStrip strip || !IsOwn(name))
            return false;
        strip.InvalidateVisual();
        name = TabStripComponentClass.Instance.Member(name)!.Name;
        if (name.StartsWith("Client", StringComparison.Ordinal))
            throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty);
        if (name == "SelectedItem")
        {
            if (value.Value is null && value.Type == Vb6Value.ValueType.Object)
                strip.Model.Select(null);
            else if (value.Value is TabStripTab selected)
            {
                if (!ReferenceEquals(strip, selected.Owner))
                    throw Refused("foreign Tab");
                await Select(strip, selected.Tab, interpreter, module, env);
            }
            else
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            return true;
        }
        if (name == "ImageList")
        {
            if (value.Type == Vb6Value.ValueType.String)
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            throw Refused(name);
        }
        if (name == "HotTracking")
            throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty);
        if (name == "Enabled")
        {
            strip.IsEnabled = Boolean(value);
            return true;
        }
        if (name == "TabMinWidth")
            throw Refused(name);
        if (!strip.Values.TryGetValue(name, out var old))
            throw Refused(name);
        if (old.Type == Vb6Value.ValueType.Boolean)
            strip.Values[name] = new Vb6Value(Boolean(value));
        else
        {
            // Conversions beyond the measured numeric inputs remain refused.
            if (!Vb6Value.TryNumericToDouble(value, out var numeric) || value.Type == Vb6Value.ValueType.Date || !double.IsFinite(numeric))
                throw Refused(name + " conversion");
            if (numeric != Math.Truncate(numeric))
                throw Refused(name + " fractional conversion");
            var number = numeric;
            var fixedSize = name is "TabFixedWidth" or "TabFixedHeight";
            if (fixedSize && (number < short.MinValue || number > short.MaxValue))
                throw new VBRunTimeException(VBStandardError.Overflow);
            var max = name switch { "Placement" => 3, "TabStyle" or "OLEDropMode" => 1, "MousePointer" => 99,
                "TabFixedWidth" or "TabFixedHeight" => short.MaxValue, _ => 2 };
            if (number < 0 || number > max || name == "MousePointer" && number > 15 && number != 99)
                throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
            strip.Values[name] = fixedSize ? new Vb6Value((int)number) : new Vb6Value((long)number);
            if (name == "Placement" && number == 2)
                strip.Values["MultiRow"] = new Vb6Value(true);
            if (name is "Style" or "TabStyle")
            {
                // Style changes select tab 1 and raise Click only (measured, group 2).
                strip.Model.Select(null);
                if (strip.Model.Tabs.Count > 0)
                    await Select(strip, strip.Model.Tabs[0], interpreter, module, env);
            }
        }
        return true;
    }

    private static bool Boolean(Vb6Value value) => value.Type == Vb6Value.ValueType.Boolean ? (bool)value.Value!
        : throw Refused("Boolean conversion");

    internal static async Task SelectFromKeyboard(VBTabStrip strip, TabStripModel.Tab tab, VBWindowContext context, bool arrowKey)
    {
        if (VBProps.GetIndex(strip) is not null)
            throw Refused("control-array events");
        var interpreter = context.Interpreter!;
        if (!strip.Model.Tabs.Contains(tab))
            throw Refused("removed Tab");
        if (strip.Model.SelectedItem is not null)
        {
            var state = interpreter.ExecutionContext.State;
            var location = state.Alloc(new Vb6Value(0));
            await interpreter.RaiseFormEvent(context.FormInstance!, $"{VBProps.GetName(strip)}_BeforeClick",
                new CallArg[] { new(state[location], location) });
            if (Convert.ToDouble(state[location].Value, System.Globalization.CultureInfo.InvariantCulture) != 0)
            {
                if (!arrowKey)
                    throw Refused("cancelled keyboard selection");
                return;
            }
        }
        strip.Model.Select(null);
        strip.Model.Select(tab);
        await interpreter.RaiseFormEvent(context.FormInstance!, $"{VBProps.GetName(strip)}_Click");
    }

    internal static async Task Mouse(VBTabStrip strip, TabStripModel.Tab tab, VBWindowContext context,
        bool press, bool second, bool right, bool control, IReadOnlyList<CallArg> args)
    {
        if (VBProps.GetIndex(strip) is not null)
            throw Refused("control-array events");
        var interpreter = context.Interpreter!;
        var prefix = VBProps.GetName(strip);
        if (!press)
        {
            await interpreter.RaiseFormEvent(context.FormInstance!, $"{prefix}_MouseUp", args);
            await interpreter.RaiseFormEvent(context.FormInstance!, $"{prefix}_Click");
            return;
        }
        if (second)
            return;
        await interpreter.RaiseFormEvent(context.FormInstance!, $"{prefix}_MouseDown", args);
        if (right || !strip.Model.Tabs.Contains(tab))
            return;
        var multi = (long)strip.Values["Style"].Value! == 1 && (bool)strip.Values["MultiSelect"].Value!;
        if (!multi && !ReferenceEquals(strip.Model.SelectedItem, tab) && strip.Model.SelectedItem is not null)
        {
            var state = interpreter.ExecutionContext.State;
            var location = state.Alloc(new Vb6Value(0));
            await interpreter.RaiseFormEvent(context.FormInstance!, $"{prefix}_BeforeClick",
                new CallArg[] { new(state[location], location) });
            if (Convert.ToDouble(state[location].Value, System.Globalization.CultureInfo.InvariantCulture) != 0)
                return;
        }
        strip.Model.SelectFromMouse(tab, multi, control);
    }

    private static async Task Select(VBTabStrip strip, TabStripModel.Tab tab,
        BasicInterpreter interpreter, ModuleInfo module, ExecutionEnvironment env)
    {
        if (VBProps.GetIndex(strip) is not null)
            throw Refused("control-array events");
        if (!strip.Model.Tabs.Contains(tab))
            throw Refused("removed Tab");
        var cancel = false;
        if (strip.Model.SelectedItem is not null)
        {
            var state = interpreter.ExecutionContext.State;
            var location = state.Alloc(new Vb6Value(0));
            await Raise("BeforeClick", [new CallArg(state[location], location)]);
            cancel = Convert.ToDouble(state[location].Value, System.Globalization.CultureInfo.InvariantCulture) != 0;
        }
        strip.Model.BeforeClick = () => cancel;
        try { strip.Model.Select(tab); }
        finally { strip.Model.BeforeClick = null; }
        await Raise("Click", []);

        async Task Raise(string eventName, IReadOnlyList<CallArg> args)
        {
            var handler = $"{VBProps.GetName(strip)}_{eventName}";
            if (strip.FindLogicalAncestorOfType<IFormView>() is { Context: { Interpreter: { } ownerInterpreter, FormInstance: { } form } })
                await ownerInterpreter.RaiseFormEvent(form, handler, args);
            else if (module.PrePass.Procedures.TryGetValue(handler, out var proc))
                await interpreter.RunProcedure(module, proc, args, env);
        }
    }
}

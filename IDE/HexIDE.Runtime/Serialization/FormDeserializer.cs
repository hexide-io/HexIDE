using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Serialization;

public class FormDeserializer
{
    private static ComponentBaseClass[] AllSupportedComponents =
    [
        CheckBoxComponentClass.Instance,
        ComboBoxComponentClass.Instance,
        CommandButtonComponentClass.Instance,
        FormComponentClass.Instance,
        FrameComponentClass.Instance,
        HScrollBarComponentClass.Instance,
        LabelComponentClass.Instance,
        ListBoxComponentClass.Instance,
        MenuComponentClass.Instance,
        OptionButtonComponentClass.Instance,
        PictureBoxComponentClass.Instance,
        ShapeComponentClass.Instance,
        TextBoxComponentClass.Instance,
        TimerComponentClass.Instance,
        VScrollBarComponentClass.Instance,
    ];

    private static Dictionary<string, ComponentBaseClass> componentsByTypeNames;

    private static readonly HashSet<string> VisualRootTypes =
    [
        "VB.Form", "VB.UserControl", "VB.PropertyPage"
    ];

    // Property names that are handled specially (aliases, skipped, or form-level metadata).
    // These are considered "known" so they are not preserved as unknowns.
    private static readonly HashSet<string> SpecialCasedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ClientTop", "ClientLeft", "ClientWidth", "ClientHeight",
        "ScaleHeight", "ScaleWidth", "LockControls"
    };

    static FormDeserializer()
    {
        componentsByTypeNames = new();
        foreach (var component in AllSupportedComponents)
        {
            componentsByTypeNames[component.VBTypeName] = component;
        }
        componentsByTypeNames["VB.UserControl"]  = FormComponentClass.Instance;
        componentsByTypeNames["VB.PropertyPage"] = FormComponentClass.Instance;
    }

    public FormDefinition? Deserialize(ProjectDefinition owner, string source, IDeserializeErrorSink errorSink,
        IReadOnlyDictionary<int, byte[]>? frxBlobs = null,
        IReadOnlyDictionary<string, ComponentBaseClass>? extraComponents = null)
    {
        VBSerializedComponent rootComponent;
        string code;
        List<string> headerLines;
        try
        {
            var vb = new VbFrmFormatDeserializer();
            (rootComponent, code) = vb.Deserialize(source);
            headerLines = vb.HeaderLines;
            // vb.MaxBeginDepth is deliberately not used: it counts every Begin, and the gate below needs
            // to know which of that nesting was menus. Only the component walk can tell.
        }
        catch (Exception ex)
        {
            errorSink.LogError($"Failed to parse form file: {ex.Message}");
            return null;
        }

        if (!VisualRootTypes.Contains(rootComponent.Type))
        {
            errorSink.LogError($"This is not a valid form file");
            return null;
        }

        var form = new FormDefinition(owner, Array.Empty<ComponentInstance>(), "");
        form.HeaderLines.AddRange(headerLines);
        form.RecordLoadedCompanionBlobCount(frxBlobs?.Count ?? 0);
        var components = new List<ComponentInstance>();
        var maxUnreproducibleDepth = 0;
        // Blobs that actually reached the model, by reference — the dictionary hands out the same array
        // instance each time, and two properties may legitimately cite one offset, so counting references
        // rather than assignments is what makes "fewer out than in" mean what it says.
        var capturedBlobs = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);

        // depth: the form itself is 1, its direct children 2, and so on — the same counting the
        // unfaithful-save gate uses. parent is null only for the form.
        void LoadRecur(VBSerializedComponent serializedComponent, ComponentInstance? parent, int depth)
        {
            if (!componentsByTypeNames.TryGetValue(serializedComponent.Type, out var componentClass) &&
                (extraComponents == null || !extraComponents.TryGetValue(serializedComponent.Type, out componentClass)))
            {
                // Unknown component type — reconstruct raw text and preserve for round-trip
                var subtreeLines = ReconstructRawSubtree(serializedComponent, 1);
                // The subtree's TEXT survives, but any blob it points at is never collected, so a
                // regenerated companion would omit it. Record the loss so the save leaves the file alone.
                if (subtreeLines.Any(FrxDeserializer.IsFrxReference))
                    form.MarkUnmodelledBinaryProperty();
                form.UnknownChildSubtreeTexts.Add(string.Join("\r\n", subtreeLines));
                errorSink.LogError($"Class {serializedComponent.Type} of control {serializedComponent.Name} is not a supported control class — preserved as unknown.");
                return;
            }

            var instance = new ComponentInstance(componentClass, serializedComponent.Name);
            // Every component stays in the flat list, menus included. The tree below is additive, so
            // nothing that reads FormDefinition.Components needs to know it exists.
            components.Add(instance);

            var isMenu = componentClass is MenuComponentClass;
            var parentIsMenu = parent?.BaseClass is MenuComponentClass;

            // A .frm nests a menu tree as nested Begin VB.Menu blocks. Record the link on the parent,
            // which is where the designer already keeps it, so the save path can walk the same tree.
            if (isMenu && parentIsMenu)
            {
                var subItems = parent!.GetPropertyOrDefault(MenuComponentClass.SubItemsProperty)
                               ?? new List<ComponentInstance>();
                subItems.Add(instance);
                parent.SetProperty(MenuComponentClass.SubItemsProperty, subItems);
            }

            // Depth the writer cannot yet reproduce. Menu-under-menu and menu-under-form are excluded
            // because the tree above now carries them; anything else — a control inside a Frame, say —
            // still flattens on save, so the form must stay read-only (#84).
            if (!(isMenu && (parent is null || parentIsMenu)))
                maxUnreproducibleDepth = Math.Max(maxUnreproducibleDepth, depth);

            foreach (var serializedProperty in serializedComponent.Properties)
            {
                var propertyName = serializedProperty.Key;
                if (propertyName == "ClientTop")
                    propertyName = "Top";
                if (propertyName == "ClientLeft")
                    propertyName = "Left";
                if (propertyName == "ClientWidth")
                    propertyName = "Width";
                if (propertyName == "ClientHeight")
                    propertyName = "Height";
                if (propertyName == "ScaleHeight" || propertyName == "ScaleWidth")
                    continue;

                // LockControls is a form-level metadata flag, not a component property.
                if (propertyName == "LockControls" && componentClass is FormComponentClass)
                {
                    var lv = serializedProperty.Value;
                    if (lv is int li) form.LockControls = li != 0;
                    else if (lv is double ld) form.LockControls = (int)ld != 0;
                    continue;
                }

                if (!componentClass.PropertiesByName.TryGetValue(propertyName, out var propertyClass))
                    continue; // unknown property — preserved verbatim by OrderedRawProperties loop below

                var val = serializedProperty.Value;
                void InvalidValue() => errorSink.LogError($"Property {serializedProperty.Key} in {serializedComponent.Name} had an invalid value.");
                if (propertyClass.PropertyType == typeof(string))
                {
                    if (val is not string)
                    {
                        InvalidValue();
                        continue;
                    }
                    instance.SetUntypedProperty(propertyClass, val);
                }
                else if (propertyClass.PropertyType == typeof(bool))
                {
                    if (val is int i)
                        instance.SetUntypedProperty(propertyClass, i != 0);
                    else if (val is double d)
                        instance.SetUntypedProperty(propertyClass, (int)d != 0);
                    else
                    {
                        InvalidValue();
                        continue;
                    }
                }
                else if (propertyClass.PropertyType == typeof(int))
                {
                    if (val is int i)
                        instance.SetUntypedProperty(propertyClass, i);
                    else if (val is double d)
                        instance.SetUntypedProperty(propertyClass, (int)d);
                    else
                    {
                        InvalidValue();
                        continue;
                    }
                }
                else if (propertyClass.PropertyType == typeof(float))
                {
                    if (val is int i)
                        instance.SetUntypedProperty(propertyClass, (float)i);
                    else if (val is float f)
                        instance.SetUntypedProperty(propertyClass, f);
                    else if (val is double d)
                        instance.SetUntypedProperty(propertyClass, (float)d);
                    else
                        InvalidValue();
                }
                else if (propertyClass.PropertyType == typeof(double))
                {
                    var multiply = propertyClass == VBProperties.LeftProperty ||
                                   propertyClass == VBProperties.TopProperty ||
                                   propertyClass == VBProperties.WidthProperty ||
                                   propertyClass == VBProperties.HeightProperty
                        ? 1.0 / VBScaleModeExtensions.PixelToTwips
                        : 1;
                    if (val is int i)
                        instance.SetUntypedProperty(propertyClass, multiply * (double)i);
                    else if (val is float f)
                        instance.SetUntypedProperty(propertyClass, multiply * (double)f);
                    else if (val is double d)
                        instance.SetUntypedProperty(propertyClass, multiply * d);
                    else
                        InvalidValue();
                }
                else if (propertyClass.PropertyType.IsEnum)
                {
                    if (val is int i)
                        instance.SetUntypedProperty(propertyClass, Enum.ToObject(propertyClass.PropertyType, i));
                    else if (val is double d)
                        instance.SetUntypedProperty(propertyClass, Enum.ToObject(propertyClass.PropertyType, (int)d));
                    else
                        InvalidValue();
                }
                else if (propertyClass.PropertyType == typeof(VBColor))
                {
                    if (val is not VBColor color)
                    {
                        InvalidValue();
                        continue;
                    }
                    instance.SetUntypedProperty(propertyClass, color);
                }
                else if (propertyClass.PropertyType == typeof(byte[]))
                {
                    if (val is string frxRef && FrxDeserializer.IsFrxReference(frxRef))
                    {
                        if (frxBlobs != null)
                        {
                            var blob = FrxDeserializer.TryExtractBlob(frxRef, frxBlobs);
                            if (blob != null)
                            {
                                instance.SetUntypedProperty(propertyClass, blob);
                                capturedBlobs.Add(blob);
                            }
                            else
                                errorSink.LogError($"Property {serializedProperty.Key} in {serializedComponent.Name}: .frx offset not found.");
                        }
                        // If no frx data provided, silently skip — blob will remain null (default)
                    }
                    else
                    {
                        // Not a .frx reference — ignore (binary properties must come from .frx)
                    }
                }
                else if (propertyClass.PropertyType == typeof(VBFont))
                {
                    if (val is not Dictionary<string, object> fontProps ||
                        !fontProps.TryGetValue("Name", out var fontName) ||
                        !fontProps.TryGetValue("Size", out var fontSize) ||
                        !fontProps.TryGetValue("Weight", out var fontWeight) ||
                        !fontProps.TryGetValue("Italic", out var italic))
                    {
                        InvalidValue();
                        continue;
                    }

                    // A non-numeric BeginProperty Font metric (e.g. `Weight = Bold`) is malformed input — fall back to
                    // the VB6 default rather than letting Convert.ToInt32 throw a FormatException that fails the whole
                    // form load (and crashed `Standalone --check` instead of reporting the form as FAIL).
                    static int MetricOr(object? v, int fallback)
                    {
                        try { return Convert.ToInt32(v); }
                        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return fallback; }
                    }
                    var fontNameStr = fontName as string ?? "MS Sans Serif";
                    var font = new VBFont(
                        fontNameStr,
                        MetricOr(fontSize, 8),
                        bold: MetricOr(fontWeight, 400) >= 700,
                        italic: MetricOr(italic, 0) != 0);
                    instance.SetUntypedProperty(propertyClass, font);
                }
            }

            // Collect unknown raw property lines for round-trip preservation
            var knownNames = BuildKnownPropertyNames(componentClass);
            foreach (var (name, rawLines) in serializedComponent.OrderedRawProperties)
            {
                if (knownNames.Contains(name))
                    continue;
                if (rawLines.Any(l => FrxDeserializer.IsFrxReference(l)))
                {
                    // The property is dropped, so a save can no longer reproduce the blob it referenced.
                    // Record that on the form: the save path uses it to leave the companion binary
                    // untouched rather than truncating or deleting the user's images.
                    form.MarkUnmodelledBinaryProperty();
                    errorSink.LogError($"Unknown binary-referencing property '{name}' in '{serializedComponent.Name}' cannot be preserved in phase 1 — deferred to binary round-trip phase.");
                    continue;
                }
                instance.UnknownRawPropertyLines.AddRange(rawLines);
            }

            foreach (var nested in serializedComponent.SubComponents)
                LoadRecur(nested, instance, depth + 1);
        }

        LoadRecur(rootComponent, null, 1);
        form.RecordUnreproducibleNestingDepth(maxUnreproducibleDepth);

        // The refusal gate, decided here rather than from the parser's raw Begin depth, because only the
        // walk above knows which nesting was menu nesting. Menus now round-trip, so they no longer hold a
        // form read-only; a control inside a Frame or PictureBox still does, until #84.
        if (maxUnreproducibleDepth > 2)
            form.MarkUnfaithfulToSave(UnfaithfulSaveCause.NestedContainers,
                "it contains controls nested inside a container, which HexIDE would flatten onto the form on save");

        // Binary content the writer cannot re-emit. Two ways it goes missing, and only the first announces
        // itself: a property on an unmodelled control (flagged during the walk), and a property that IS
        // named on a modelled control but whose CLR type is not byte[], which is dropped with no diagnostic
        // at all — ODBC Log In's ComboBox List is exactly that. The count comparison is the safety net for
        // the silent case, and it is why this is not simply "did anything set the flag".
        //
        // The bytes themselves are never lost: WriteCompanionBinary leaves the companion alone. What is
        // lost is the reference, so the picture disappears from the control while the file that holds it
        // stays on disk. That is a save which looks like it worked.
        var loadedBlobCount = frxBlobs?.Count ?? 0;
        if (form.HasUnmodelledBinaryProperties || capturedBlobs.Count < loadedBlobCount)
            form.MarkUnfaithfulToSave(UnfaithfulSaveCause.UnreproducibleBinaryContent,
                $"it references companion binary content HexIDE cannot re-emit "
                + $"({capturedBlobs.Count} of {loadedBlobCount} blob(s) reached the model)");

        form.UpdateCode(code);
        form.UpdateComponents(components);
        if (rootComponent.Type != "VB.Form")
            form.UpdateRootTypeName(rootComponent.Type);
        return form;
    }

    private static HashSet<string> BuildKnownPropertyNames(ComponentBaseClass componentClass)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in componentClass.Properties)
            names.Add(prop.Name);
        names.UnionWith(SpecialCasedPropertyNames);
        return names;
    }

    private static List<string> ReconstructRawSubtree(VBSerializedComponent component, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 3);
        var lines = new List<string>();
        lines.Add($"{indent}Begin {component.Type} {component.Name}");

        foreach (var (_, rawLines) in component.OrderedRawProperties)
        {
            if (rawLines.Any(l => FrxDeserializer.IsFrxReference(l)))
                continue; // skip binary-referencing lines (deferred to binary phase)
            lines.AddRange(rawLines);
        }

        foreach (var sub in component.SubComponents)
            lines.AddRange(ReconstructRawSubtree(sub, indentLevel + 1));

        lines.Add($"{indent}End");
        return lines;
    }
}

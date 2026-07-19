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
        try
        {
            var vb = new VbFrmFormatDeserializer();
            (rootComponent, code) = vb.Deserialize(source);
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
        var components = new List<ComponentInstance>();

        void LoadRecur(VBSerializedComponent serializedComponent)
        {
            if (!componentsByTypeNames.TryGetValue(serializedComponent.Type, out var componentClass) &&
                (extraComponents == null || !extraComponents.TryGetValue(serializedComponent.Type, out componentClass)))
            {
                // Unknown component type — reconstruct raw text and preserve for round-trip
                var subtreeLines = ReconstructRawSubtree(serializedComponent, 1);
                form.UnknownChildSubtreeTexts.Add(string.Join("\r\n", subtreeLines));
                errorSink.LogError($"Class {serializedComponent.Type} of control {serializedComponent.Name} is not a supported control class — preserved as unknown.");
                return;
            }

            var instance = new ComponentInstance(componentClass, serializedComponent.Name);
            components.Add(instance);

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
                                instance.SetUntypedProperty(propertyClass, blob);
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

                    var fontNameStr = fontName as string ?? "MS Sans Serif";
                    var font = new VBFont(
                        fontNameStr,
                        Convert.ToInt32(fontSize),
                        bold: Convert.ToInt32(fontWeight) >= 700,
                        italic: Convert.ToInt32(italic) != 0);
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
                    errorSink.LogError($"Unknown binary-referencing property '{name}' in '{serializedComponent.Name}' cannot be preserved in phase 1 — deferred to binary round-trip phase.");
                    continue;
                }
                instance.UnknownRawPropertyLines.AddRange(rawLines);
            }

            foreach (var nested in serializedComponent.SubComponents)
                LoadRecur(nested);
        }

        LoadRecur(rootComponent);

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

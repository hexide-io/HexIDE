using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Serialization;

public class FormSerializer
{
    /// <summary>
    /// Serializes a form to .frm text and optional .frx binary content.
    /// <paramref name="formFileName"/> is the base file name (e.g. "Form1.frm") used to build .frx references.
    /// </summary>
    public (string frmText, byte[]? frxContent) Serialize(FormDefinition element, string formFileName)
        => SerializeCore(element, element.Code, formFileName);

    /// <summary>
    /// Serializes a UserControl FormPart with explicit code (e.g. module.Code) to .ctl text.
    /// Use this overload when the code lives in a ModuleDefinition rather than the FormDefinition.
    /// </summary>
    public (string frmText, byte[]? frxContent) Serialize(FormDefinition element, string code, string formFileName)
        => SerializeCore(element, code, formFileName);

    private (string frmText, byte[]? frxContent) SerializeCore(FormDefinition element, string code, string formFileName)
    {
        // Collect all byte[] blobs first so we can assign offsets
        var blobs = new List<byte[]>();
        void CollectBlobs(ComponentInstance inst)
        {
            foreach (var prop in inst.BaseClass.Properties)
            {
                if (prop.PropertyType == typeof(byte[]) && inst.TryGetBoxedProperty(prop, out var boxed) && boxed is byte[] blob)
                    blobs.Add(blob);
            }
        }
        foreach (var component in element.Components)
            CollectBlobs(component);

        Dictionary<byte[], int>? offsetMap = null;
        byte[]? frxContent = null;
        if (blobs.Count > 0)
            (frxContent, offsetMap) = FrxSerializer.Write(blobs);

        var sourceExt = System.IO.Path.GetExtension(formFileName).ToLowerInvariant();
        var companionExt = sourceExt switch { ".ctl" => ".ctx", ".pag" => ".pgx", _ => ".frx" };
        var frxName = System.IO.Path.ChangeExtension(formFileName, companionExt);
        VbFrmFormatSerializer vb = new VbFrmFormatSerializer();

        var form = element.Components.Single(x => x.BaseClass is FormComponentClass);

        vb.Begin(element.RootVBTypeName, form.GetPropertyOrDefault(VBProperties.NameProperty)!);

        WriteAllProperties(vb, form, frxName, offsetMap);
        if (element.LockControls)
            vb.WriteProperty("LockControls", typeof(bool), true);
        WriteFormMeasurements(vb, form);

        foreach (var component in element.Components)
        {
            if (component != form)
            {
                vb.Begin(component.BaseClass.VBTypeName, component.GetPropertyOrDefault(VBProperties.NameProperty)!);
                WriteAllProperties(vb, component, frxName, offsetMap);
                vb.End();
            }
        }

        foreach (var subtreeText in element.UnknownChildSubtreeTexts)
        {
            foreach (var line in subtreeText.Split(["\r\n", "\n"], StringSplitOptions.None))
                vb.WriteVerbatimLine(line);
        }

        vb.End();

        vb.WriteCode(code);

        return (vb.GetOutput(), frxContent);
    }

    private void WriteFormMeasurements(VbFrmFormatSerializer vb, ComponentInstance form)
    {
        if (form.TryGetProperty(VBProperties.WidthProperty, out var width))
        {
            vb.WriteProperty("ClientWidth", VBProperties.WidthProperty.PropertyType, width * VBScaleModeExtensions.PixelToTwips);
            vb.WriteProperty("ScaleWidth", VBProperties.WidthProperty.PropertyType, width * VBScaleModeExtensions.PixelToTwips);
        }
        if (form.TryGetProperty(VBProperties.HeightProperty, out var height))
        {
            vb.WriteProperty("ClientHeight", VBProperties.HeightProperty.PropertyType, height * VBScaleModeExtensions.PixelToTwips);
            vb.WriteProperty("ScaleHeight", VBProperties.HeightProperty.PropertyType, height * VBScaleModeExtensions.PixelToTwips);
        }
        if (form.TryGetProperty(VBProperties.TopProperty, out var top))
        {
            vb.WriteProperty("ClientTop", VBProperties.TopProperty.PropertyType, top * VBScaleModeExtensions.PixelToTwips);
        }
        if (form.TryGetProperty(VBProperties.LeftProperty, out var left))
        {
            vb.WriteProperty("ClientLeft", VBProperties.LeftProperty.PropertyType, left * VBScaleModeExtensions.PixelToTwips);
        }
    }

    private void WriteAllProperties(VbFrmFormatSerializer vb, ComponentInstance instance,
        string frxName, Dictionary<byte[], int>? offsetMap)
    {
        foreach (var prop in instance.BaseClass.Properties)
        {
            if (prop == VBProperties.NameProperty)
                continue;

            // Invisible controls (e.g. Timer) have no Width/Height/Visible in VB6 — emitting them makes
            // vb6.exe reject the .frm ("property name Width ... is invalid"). They keep Left/Top.
            if (!instance.BaseClass.IsVisual &&
                (prop == VBProperties.WidthProperty
                 || prop == VBProperties.HeightProperty
                 || prop == VBProperties.VisibleProperty))
                continue;

            if (prop == VBProperties.TopProperty ||
                prop == VBProperties.LeftProperty ||
                prop == VBProperties.WidthProperty ||
                prop == VBProperties.HeightProperty)
            {
                if (instance.TryGetProperty<double>((PropertyClass<double>)prop, out var measurement))
                    vb.WriteProperty(prop.Name, prop.PropertyType, measurement * VBScaleModeExtensions.PixelToTwips);
            }
            else if (prop.PropertyType == typeof(byte[]))
            {
                if (instance.TryGetBoxedProperty(prop, out var boxed) && boxed is byte[] blob && offsetMap != null)
                {
                    if (offsetMap.TryGetValue(blob, out var offset))
                    {
                        // Write "FormName.frx":HHHH reference
                        var hexOffset = offset.ToString("X4");
                        vb.WriteRawProperty(prop.Name, $"\"{frxName}\":{hexOffset}");
                    }
                }
            }
            else
            {
                if (instance.TryGetBoxedProperty(prop, out var boxedValue))
                {
                    vb.WriteProperty(prop.Name, prop.PropertyType, boxedValue);
                }
            }
        }

        foreach (var line in instance.UnknownRawPropertyLines)
            vb.WriteVerbatimLine(line);
    }
}
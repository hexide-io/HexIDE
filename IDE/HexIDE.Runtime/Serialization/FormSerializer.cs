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

        // OCX declarations sit between VERSION and the root Begin. Re-emitted verbatim so a project
        // depending on a control HexIDE cannot host is not corrupted by a save.
        foreach (var headerLine in element.HeaderLines)
            vb.WriteVerbatimLine(headerLine);

        vb.Begin(element.RootVBTypeName, form.GetPropertyOrDefault(VBProperties.NameProperty)!);

        WriteAllProperties(vb, form, frxName, offsetMap);
        if (element.LockControls)
            vb.WriteProperty("LockControls", typeof(bool), true);
        WriteFormMeasurements(vb, form);

        // A menu that some other menu claims as a sub-item is written by that parent, not from the flat
        // list — otherwise every nested menu appears twice, once in the tree and once as a sibling.
        var claimedByAParent = new HashSet<ComponentInstance>();
        foreach (var component in element.Components)
        {
            if (component.GetPropertyOrDefault(MenuComponentClass.SubItemsProperty) is { } subItems)
                foreach (var child in subItems)
                    claimedByAParent.Add(child);
        }

        // Begin/End maintain the indent level themselves, so recursing here is all the nesting needs:
        // VB6's three-space step per level falls out of it.
        void WriteComponentTree(ComponentInstance component)
        {
            vb.Begin(component.BaseClass.VBTypeName, component.GetPropertyOrDefault(VBProperties.NameProperty)!);
            WriteAllProperties(vb, component, frxName, offsetMap);

            if (component.GetPropertyOrDefault(MenuComponentClass.SubItemsProperty) is { } subItems)
                foreach (var child in subItems)
                    WriteComponentTree(child);

            vb.End();
        }

        foreach (var component in element.Components)
        {
            if (component == form || claimedByAParent.Contains(component))
                continue;

            WriteComponentTree(component);
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

    /// <summary>
    /// Pixels back to twips, rounded.
    ///
    /// The inbound conversion divides by 15, which is not exactly representable in binary floating point:
    /// 6684 twips becomes 445.6 px, and 445.6 × 15 comes back as 6683.999999999999. Written verbatim that
    /// is both ugly and unstable — the value loses another ULP on every subsequent save, so the file never
    /// reaches a fixed point and source control never quiets down.
    ///
    /// Rounding at the write boundary fixes both without pretending the value is an integer. VB6 does
    /// write genuinely fractional twips (About Dialog.frm: <c>ScaleWidth = 5380.766</c>), and six decimal
    /// places is far finer than a twip — 1/1440 inch — will ever need, so those survive unchanged.
    ///
    /// The deeper fix is to store twips natively and convert only for layout, which would make the
    /// conversion lossless rather than merely tidy. That touches the runtime window sizing and the
    /// designer, so it is deliberately not done here.
    /// </summary>
    private static double ToTwips(double pixels) =>
        Math.Round(pixels * VBScaleModeExtensions.PixelToTwips, 6);

    private void WriteFormMeasurements(VbFrmFormatSerializer vb, ComponentInstance form)
    {
        if (form.TryGetProperty(VBProperties.WidthProperty, out var width))
        {
            vb.WriteProperty("ClientWidth", VBProperties.WidthProperty.PropertyType, ToTwips(width));
            vb.WriteProperty("ScaleWidth", VBProperties.WidthProperty.PropertyType, ToTwips(width));
        }
        if (form.TryGetProperty(VBProperties.HeightProperty, out var height))
        {
            vb.WriteProperty("ClientHeight", VBProperties.HeightProperty.PropertyType, ToTwips(height));
            vb.WriteProperty("ScaleHeight", VBProperties.HeightProperty.PropertyType, ToTwips(height));
        }
        if (form.TryGetProperty(VBProperties.TopProperty, out var top))
        {
            vb.WriteProperty("ClientTop", VBProperties.TopProperty.PropertyType, ToTwips(top));
        }
        if (form.TryGetProperty(VBProperties.LeftProperty, out var left))
        {
            vb.WriteProperty("ClientLeft", VBProperties.LeftProperty.PropertyType, ToTwips(left));
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
                    vb.WriteProperty(prop.Name, prop.PropertyType, ToTwips(measurement));
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
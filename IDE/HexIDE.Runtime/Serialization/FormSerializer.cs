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

        // One sorted block for the root, spanning all three sources of its properties. They used to be
        // written in the order they appear here, which is the order of three unrelated lists rather than
        // any order VB6 has ever produced.
        vb.BeginSortedProperties();
        WriteAllProperties(vb, form, frxName, offsetMap);
        if (element.LockControls)
            vb.WriteProperty("LockControls", typeof(bool), true);
        WriteFormMeasurements(vb, form, element.OuterRect, element.Scale);
        vb.EndSortedProperties();

        // A menu nests through SubItems, a control through ContainedControls. One helper, so the writer has
        // a single notion of "this component's children" without the two mechanisms being merged: probing
        // SubItems on every component regardless of class is what would make a Frame writable as a menu
        // item the moment anything populated that property on it.
        static IReadOnlyList<ComponentInstance> ChildrenOf(ComponentInstance component)
        {
            if (component.BaseClass is not MenuComponentClass)
                return component.ContainedControls;
            if (component.GetPropertyOrDefault(MenuComponentClass.SubItemsProperty) is { } subItems)
                return subItems;
            return [];
        }

        // A component some other component claims as a child is written by that parent, not from the flat
        // list — otherwise every nested component appears twice, once in the tree and once as a sibling.
        //
        // The form is excluded from the claim on purpose. Its own children are written by the root walk
        // below, which draws from the flat list rather than from the form's ContainedControls, so a form
        // the designer built — where nothing has recorded a containment link yet — still writes its
        // controls instead of writing none of them.
        var claimedByAParent = new HashSet<ComponentInstance>();
        foreach (var component in element.Components)
        {
            if (component == form)
                continue;
            foreach (var child in ChildrenOf(component))
                claimedByAParent.Add(child);
        }

        void WriteVerbatimBlock(string text)
        {
            foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.None))
                vb.WriteVerbatimLine(line);
        }

        // Modelled children and preserved verbatim blocks, interleaved at the position each block held when
        // it was read. Position among siblings is z-order, so writing every preserved block at the end
        // reorders the form even when it reproduces the block itself byte for byte.
        void WriteChildren(ComponentInstance container, IReadOnlyList<ComponentInstance> children)
        {
            var preserved = container.PreservedChildSubtrees;
            var childIndex = 0;
            var preservedIndex = 0;

            for (var ordinal = 0; ordinal < children.Count + preserved.Count; ordinal++)
            {
                // <= rather than ==, so an ordinal that no longer lines up — two blocks recorded at one
                // position, or a stale one left by an edit — still advances instead of stalling.
                if (preservedIndex < preserved.Count && preserved[preservedIndex].Ordinal <= ordinal)
                    WriteVerbatimBlock(preserved[preservedIndex++].Text);
                else if (childIndex < children.Count)
                    WriteComponentTree(children[childIndex++]);
            }

            // Whatever the ordinals could not place is still written rather than silently dropped.
            for (; preservedIndex < preserved.Count; preservedIndex++)
                WriteVerbatimBlock(preserved[preservedIndex].Text);
            for (; childIndex < children.Count; childIndex++)
                WriteComponentTree(children[childIndex]);
        }

        // Nothing in the model may be written twice, and nothing may be walked twice. The containment
        // mutator refuses a cycle, so reaching one here means the model was corrupted some other way — and
        // the two ways this recursion fails without the check are both bad: a cycle every member of which
        // is claimed by a parent silently vanishes from the output, producing a plausible-looking .frm with
        // controls missing, and a cycle below an unclaimed root recurses until the stack ends, which is a
        // StackOverflowException .NET cannot catch — the process dies mid-save.
        var written = new HashSet<ComponentInstance>();

        // Begin/End maintain the indent level themselves, so recursing here is all the nesting needs:
        // VB6's three-space step per level falls out of it.
        void WriteComponentTree(ComponentInstance component)
        {
            if (!written.Add(component))
                throw new InvalidOperationException(
                    $"Component '{component.GetPropertyOrDefault(VBProperties.NameProperty)}' is reachable "
                  + "more than once while writing the form — the containment tree is cyclic or has a "
                  + "component claimed by two parents. Refusing to write a corrupted .frm.");

            vb.Begin(component.BaseClass.VBTypeName, component.GetPropertyOrDefault(VBProperties.NameProperty)!);
            vb.BeginSortedProperties();
            WriteAllProperties(vb, component, frxName, offsetMap);
            vb.EndSortedProperties();
            WriteChildren(component, ChildrenOf(component));
            vb.End();
        }

        var rootChildren = new List<ComponentInstance>();
        foreach (var component in element.Components)
        {
            if (component == form || claimedByAParent.Contains(component))
                continue;
            rootChildren.Add(component);
        }

        WriteChildren(form, rootChildren);

        // The other half of the cycle guard, and the half that matters more. A component claimed by a
        // parent that is itself never reached — the shape a cycle makes — is not written twice, it is not
        // written AT ALL: the root loop skips it as claimed, and nothing else ever asks for it. That
        // produces a valid-looking .frm with controls missing from it, which is the worst outcome in the
        // taxonomy: a save that appears to have worked.
        var unwritten = element.Components.Where(c => c != form && !written.Contains(c)).ToList();
        if (unwritten.Count > 0)
            throw new InvalidOperationException(
                "These components are in the form but no parent wrote them, so the containment tree is "
              + "cyclic or disconnected: "
              + string.Join(", ", unwritten.Select(c => c.GetPropertyOrDefault(VBProperties.NameProperty)))
              + ". Refusing to write a .frm with controls silently missing from it.");

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

    /// <summary>
    /// The designer root's geometry — both rectangles of it, which is why nothing else may write the
    /// root's Left/Top/Width/Height.
    ///
    /// The model holds the CLIENT rectangle. <paramref name="outerRect"/> carries the offset to the outer
    /// window rectangle for a form whose file declared one, and is null for the nineteen-in-twenty-two
    /// that did not. Writing an outer rectangle for those would be inventing geometry the author never
    /// recorded; deriving one would mean claiming to know a window frame that belongs to whichever
    /// machine next opens the form.
    /// </summary>
    private void WriteFormMeasurements(VbFrmFormatSerializer vb, ComponentInstance form,
        RootOuterRect? outerRect, RootScale? scale)
    {
        // ScaleWidth/ScaleHeight are in ScaleMode's units, not in twips. Deriving from the client
        // rectangle is what keeps them right across a resize, and it reproduces twenty of VB6's own
        // twenty-two designer files exactly. The two it cannot are the ones declaring ScaleMode = 0 'User,
        // where the pair is a coordinate system the developer chose rather than a measurement — those are
        // written back as they were read, which is what VB6 itself does with them.
        var scaleMode = (VBScaleMode)(scale?.Mode ?? (int)VBScaleMode.Twip);
        var isUserScale = scaleMode == VBScaleMode.User;
        var (horizontalTwipsPerUnit, verticalTwipsPerUnit) = scaleMode.TwipsPerUnit();

        if (form.TryGetProperty(VBProperties.WidthProperty, out var width))
        {
            vb.WriteProperty("ClientWidth", VBProperties.WidthProperty.PropertyType, ToTwips(width));
            var scaleWidth = isUserScale && scale?.Width is { } declared
                ? declared
                : Math.Round(ToTwips(width) / horizontalTwipsPerUnit, 6);
            vb.WriteProperty("ScaleWidth", VBProperties.WidthProperty.PropertyType, scaleWidth);
        }
        if (form.TryGetProperty(VBProperties.HeightProperty, out var height))
        {
            vb.WriteProperty("ClientHeight", VBProperties.HeightProperty.PropertyType, ToTwips(height));
            var scaleHeight = isUserScale && scale?.Height is { } declared
                ? declared
                : Math.Round(ToTwips(height) / verticalTwipsPerUnit, 6);
            vb.WriteProperty("ScaleHeight", VBProperties.HeightProperty.PropertyType, scaleHeight);
        }
        if (form.TryGetProperty(VBProperties.TopProperty, out var top))
        {
            vb.WriteProperty("ClientTop", VBProperties.TopProperty.PropertyType, ToTwips(top));
        }
        if (form.TryGetProperty(VBProperties.LeftProperty, out var left))
        {
            vb.WriteProperty("ClientLeft", VBProperties.LeftProperty.PropertyType, ToTwips(left));
        }

        if (outerRect is null)
            return;

        // Client plus the offset the file itself recorded, rather than client plus a frame HexIDE
        // computed. A resize in the designer therefore moves both rectangles together and keeps the
        // frame the author saved, on a machine whose window metrics may be nothing like theirs.
        void WriteOuter(string name, PropertyClass<double> property, double offsetTwips)
        {
            if (form.TryGetProperty(property, out var clientPixels))
                vb.WriteProperty(name, property.PropertyType, ToTwips(clientPixels) + offsetTwips);
        }

        WriteOuter("Height", VBProperties.HeightProperty, outerRect.HeightOffsetTwips);
        WriteOuter("Left", VBProperties.LeftProperty, outerRect.LeftOffsetTwips);
        WriteOuter("Top", VBProperties.TopProperty, outerRect.TopOffsetTwips);
        WriteOuter("Width", VBProperties.WidthProperty, outerRect.WidthOffsetTwips);
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
                // The designer root's geometry belongs to WriteFormMeasurements alone, which knows about
                // both of its rectangles. Writing it here as well emitted the CLIENT numbers a second
                // time under the OUTER names, so every saved form claimed a window whose frame was zero
                // pixels wide — four properties VB6 never wrote, contradicting the four it did.
                if (instance.BaseClass is FormComponentClass)
                    continue;

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
                    // Enums carry VB6's own name for the value as a trailing comment. Vb6EnumNames returns
                    // null for anything it cannot name — an unattributed member, or a number outside the
                    // enum, which a designer file may perfectly well contain — and the writer then emits a
                    // bare value. A missing comment is a difference; a wrong one is a lie.
                    vb.WriteProperty(prop.Name, prop.PropertyType, boxedValue, Vb6EnumNames.For(boxedValue));
                }
            }
        }

        // Under their own names, so a preserved property sorts among the modelled ones instead of being
        // appended after them. VB6 has no notion of "the ones HexIDE understood" — it writes one
        // alphabetical run.
        foreach (var raw in instance.UnknownRawProperties)
            vb.WriteVerbatimProperty(raw.Name, raw.Lines);
    }
}
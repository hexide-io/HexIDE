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

    /// <summary>
    /// The designer half alone: the first line through the root <c>End</c> inclusive, and nothing after it.
    /// </summary>
    /// <remarks>
    /// <b>The same render the save makes, stopped before the code.</b> The code body is the serializer's
    /// last write and goes out verbatim with no separator in front of it, so a render given no code IS the
    /// designer half — which is also exactly the span <c>FormDeserializer</c> hands to
    /// <c>FormDefinition.DesignerText</c> when it reads a file. <c>DesignerHalfIsTheRenderMinusTheCode</c>
    /// pins that, because a separator added here later would silently move the split everywhere.
    ///
    /// <para>
    /// Used to refresh the header the code window shows in front of a form's code after a committed
    /// designer change (hexide-io/HexIDE#273 task 3.3). It carries no fidelity check of its own — neither
    /// does <see cref="Serialize(FormDefinition, string)"/>, which is why <c>SerializeFormToFile</c> gates
    /// on <c>CanSaveFaithfully</c> before calling it, and why the refresh gates on the same thing.
    /// </para>
    /// </remarks>
    public string SerializeDesignerText(FormDefinition element, string formFileName)
        => SerializeCore(element, "", formFileName).frmText;

    private (string frmText, byte[]? frxContent) SerializeCore(FormDefinition element, string code, string formFileName)
    {
        // Every citation goes out as a placeholder, and the companion is laid out from the finished text, in the
        // order it cites its records (#60). Collecting the records up front instead had to repeat the order the
        // text is written in, and once got two of them the wrong way round: see CompanionCitations.
        var citations = new CompanionCitations();

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
        WriteAllProperties(vb, form, citations);
        if (element.LockControls)
            vb.WriteProperty("LockControls", typeof(bool), true);
        WriteFormMeasurements(vb, form, element.OuterRect, element.Scale);
        vb.EndSortedProperties();

        // Its lines as they were read, but for any citation of the companion, which follows its record.
        void WriteVerbatimBlock(ComponentInstance.PreservedSubtree block)
        {
            foreach (var line in block.Text.Split(["\r\n", "\n"], StringSplitOptions.None))
                vb.WriteVerbatimLine(citations.Cite(line, block.Records));
        }

        // Modelled children and preserved verbatim blocks, interleaved at the position each block holds:
        // where it was read, or where the designer has since moved a control past it (#1498). Position among
        // siblings is z-order, so writing every preserved block at the end reorders the form even when it
        // reproduces the block itself byte for byte.
        void WriteChildren(ComponentInstance container, IReadOnlyList<ComponentInstance> children)
        {
            var preserved = container.PreservedChildSubtrees;
            var before = CarriedOrder.ChildrenBefore(children.Count, preserved);
            var preservedIndex = 0;
            for (var childIndex = 0; childIndex <= children.Count; childIndex++)
            {
                while (preservedIndex < preserved.Count && before[preservedIndex] <= childIndex)
                    WriteVerbatimBlock(preserved[preservedIndex++]);
                if (childIndex < children.Count)
                    WriteComponentTree(children[childIndex]);
            }
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
            WriteAllProperties(vb, component, citations);
            vb.EndSortedProperties();
            WriteChildren(component, ChildrenOf(component));
            vb.End();
        }

        WriteChildren(form, RootChildren(element.Components, form));

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

        // Resolved before the code goes after it, so nothing in the code can be read as a placeholder.
        var (designer, frxContent) = citations.Resolve(vb.GetOutput(), frxName);
        return (designer + code, frxContent);
    }

    // A menu nests through SubItems, a control through ContainedControls. One helper, so the writer has
    // a single notion of "this component's children" without the two mechanisms being merged: probing
    // SubItems on every component regardless of class is what would make a Frame writable as a menu
    // item the moment anything populated that property on it.
    private static IReadOnlyList<ComponentInstance> ChildrenOf(ComponentInstance component)
    {
        if (component.BaseClass is not MenuComponentClass)
            return component.ContainedControls;
        if (component.GetPropertyOrDefault(MenuComponentClass.SubItemsProperty) is { } subItems)
            return subItems;
        return [];
    }

    /// <summary>
    /// The children the root's block holds, in the order they are written: every component no other component claims
    /// as a child, in the order the list holds them. The designer places carried blocks among these (#1498), so it asks
    /// here rather than working them out again.
    /// </summary>
    /// <remarks>
    /// A component some other component claims as a child is written by that parent, not from the flat list,
    /// otherwise every nested component appears twice, once in the tree and once as a sibling. The root is excluded
    /// from the claim on purpose: its own children come from the flat list rather than from its ContainedControls,
    /// so a form the designer built, where nothing has recorded a containment link yet, still writes its controls
    /// instead of writing none of them.
    /// </remarks>
    public static IReadOnlyList<ComponentInstance> RootChildren(IReadOnlyList<ComponentInstance> components, ComponentInstance root)
    {
        var claimedByAParent = new HashSet<ComponentInstance>();
        foreach (var component in components)
        {
            if (component == root)
                continue;
            foreach (var child in ChildrenOf(component))
                claimedByAParent.Add(child);
        }
        return components.Where(component => component != root && !claimedByAParent.Contains(component)).ToList();
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

    private void WriteAllProperties(VbFrmFormatSerializer vb, ComponentInstance instance, CompanionCitations citations)
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

            // VB6's designer never writes a Dropdown List's Text, and its run time ignores one (measured, #926).
            if (prop == VBProperties.TextProperty && instance.BaseClass is ComboBoxComponentClass
                && ComboBoxComponentClass.IsDropdownList(instance))
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
                if (instance.TryGetBoxedProperty(prop, out var boxed) && boxed is byte[] blob)
                    vb.WriteRawProperty(prop.Name, citations.Cite(blob));
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
        //
        // One of them may be a known property whose value HexIDE could not read (a palette colour, #786). It is kept
        // only while nothing replaced it: once the designer sets that property, the modelled value is written and the
        // raw line is not, or the file would carry the property twice.
        foreach (var raw in instance.UnknownRawProperties)
        {
            if (instance.BaseClass.Properties.Any(p =>
                    string.Equals(p.Name, raw.Name, StringComparison.OrdinalIgnoreCase) && instance.TryGetBoxedProperty(p, out _)))
                continue;
            vb.WriteVerbatimProperty(raw.Name, raw.Lines.Select(line => citations.Cite(line, raw.Records)).ToList());
        }
    }
}
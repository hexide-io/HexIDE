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
        LineComponentClass.Instance,
        ListBoxComponentClass.Instance,
        MenuComponentClass.Instance,
        OptionButtonComponentClass.Instance,
        PictureBoxComponentClass.Instance,
        ShapeComponentClass.Instance,
        ImageComponentClass.Instance,
        TextBoxComponentClass.Instance,
        TimerComponentClass.Instance,
        VScrollBarComponentClass.Instance,
        Components.InBox.TreeViewComponentClass.Instance,
    ];

    private static Dictionary<string, ComponentBaseClass> componentsByTypeNames;

    private static readonly HashSet<string> VisualRootTypes =
    [
        "VB.Form", "VB.UserControl", "VB.PropertyPage"
    ];

    // The designer root's client rectangle, as the model keeps it.
    private static readonly Dictionary<string, PropertyClass> RootClientAxes = new()
    {
        ["Left"] = VBProperties.LeftProperty, ["Top"] = VBProperties.TopProperty,
        ["Width"] = VBProperties.WidthProperty, ["Height"] = VBProperties.HeightProperty,
    };

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
        // Each its own list, which VB6's Properties window gives it (#1585).
        componentsByTypeNames["VB.UserControl"]  = UserControlRootClass.Instance;
        componentsByTypeNames["VB.PropertyPage"] = PropertyPageRootClass.Instance;
        // An in-box control under each library that declares it: MSComctlLib.ProgressBar and ComctlLib.ProgressBar (#1082).
        foreach (var inBox in InBoxComponents)
            foreach (var library in inBox.Libraries)
                componentsByTypeNames[library.TypeName(inBox.ControlClassName)] = inBox;
    }

    /// <summary>
    /// The class a ProgID names, in any case, as <c>Controls.Add</c> takes one: <c>"vb.commandbutton"</c> is a CommandButton
    /// (measured, #1556).
    /// </summary>
    internal static bool TryGetComponentClass(string progId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ComponentBaseClass? componentClass)
    {
        foreach (var (typeName, component) in componentsByTypeNames)
            if (string.Equals(typeName, progId, StringComparison.OrdinalIgnoreCase))
            {
                componentClass = component;
                return true;
            }
        componentClass = null;
        return false;
    }

    /// <summary>The in-box ActiveX controls HexIDE implements (#1082).</summary>
    internal static readonly Components.InBox.InBoxComponentClass[] InBoxComponents =
    [
        Components.InBox.ProgressBarComponentClass.Instance,
        Components.InBox.TabStripComponentClass.Instance,
        Components.InBox.StatusBarComponentClass.Instance,
        Components.InBox.ImageListComponentClass.Instance,
        Components.InBox.ToolbarComponentClass.Instance,
        Components.InBox.SSTabComponentClass.Instance,
        Components.InBox.CommonDialogComponentClass.Instance,
        Components.InBox.MaskedEdBoxComponentClass.Instance,
        RichTextBoxComponentClass.Instance,
    ];

    /// <summary>
    /// Whether a control the designer carries as a block is one HexIDE runs (#2327): a CommonDialog (#1084), or a Toolbar
    /// (#1088) or an UpDown (#1093) that holds no controls and is no control array's element. Loading one warns of
    /// nothing, and the designer's box says it is view-only there rather than unsupported. A nested or arrayed Toolbar or
    /// UpDown is still refused at run time.
    /// </summary>
    public static bool RunsWhenCarried(string type, bool holdsControls, bool inArray) =>
        type.Equals("MSComDlg.CommonDialog", StringComparison.OrdinalIgnoreCase)
        || !holdsControls && !inArray
            && (type.Equals("MSComCtl2.UpDown", StringComparison.OrdinalIgnoreCase)
                || componentsByTypeNames.TryGetValue(type, out var known) && known is Components.InBox.ToolbarComponentClass);

    public FormDefinition? Deserialize(ProjectDefinition owner, string source, IDeserializeErrorSink errorSink,
        IReadOnlyDictionary<int, byte[]>? frxBlobs = null,
        IReadOnlyDictionary<string, ComponentBaseClass>? extraComponents = null)
    {
        VBSerializedComponent rootComponent;
        string code;
        List<string> headerLines;
        string designerText;
        try
        {
            var vb = new VbFrmFormatDeserializer();
            (rootComponent, code) = vb.Deserialize(source);
            TabStripPersistence.Validate(rootComponent);
            headerLines = vb.HeaderLines;
            designerText = vb.DesignerText;
            // The parser no longer reports a raw Begin depth. It counted every Begin without knowing what
            // any of them were, which stopped being a usable gate signal once menu nesting became
            // reproducible — only the component walk below can tell menu nesting from container nesting.
        }
        catch (Exception ex)
        {
            errorSink.LogError("Str.ProjectLoad.ParseFailed", "Failed to parse form file: {0}", ex.Message);
            return null;
        }

        if (!VisualRootTypes.Contains(rootComponent.Type))
        {
            errorSink.LogError("Str.ProjectLoad.InvalidForm", "This is not a valid form file");
            return null;
        }

        var form = new FormDefinition(owner, Array.Empty<ComponentInstance>(), "");
        form.HeaderLines.AddRange(headerLines);
        // The designer half verbatim, beside the components parsed out of it (#273 task 3.1). Set here
        // rather than at the fidelity block below because `vb` is scoped to the try above, and this is
        // where the other verbatim carry-over from the same read already lands.
        form.RecordDesignerText(designerText);
        form.RecordLoadedCompanionBlobCount(frxBlobs?.Count ?? 0);
        var components = new List<ComponentInstance>();
        var maxUnreproducibleDepth = 0;
        // Blobs that actually reached the model, by reference — the dictionary hands out the same array
        // instance each time, and two properties may legitimately cite one offset, so counting references
        // rather than assignments is what makes "fewer out than in" mean what it says.
        var capturedBlobs = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
        // Preserved blocks are recorded on the container they came from, so reading them back per container
        // walks containers in turn rather than walking the file. This counter is what restores the file's
        // own order for the flat view.
        var preservedSubtreesSeen = 0;

        // depth: the form itself is 1, its direct children 2, and so on — the same counting the
        // unfaithful-save gate uses. parent is null only for the form.
        // ordinal: this component's index among its parent's children, modelled and unmodelled alike. It is
        // what lets the writer put a preserved block back between the right two siblings.
        void LoadRecur(VBSerializedComponent serializedComponent, ComponentInstance? parent, int depth, int ordinal)
        {
            var known = componentsByTypeNames.TryGetValue(serializedComponent.Type, out var componentClass)
                || extraComponents != null && extraComponents.TryGetValue(serializedComponent.Type, out componentClass);
            // The designer carries Toolbar blocks verbatim; the runtime explicitly reads their property bags.
            if (!known || componentClass is null || componentClass is Components.InBox.ToolbarComponentClass
                && (extraComponents is null || !extraComponents.ContainsKey(serializedComponent.Type)
                    || serializedComponent.SubComponents.Count != 0
                    || serializedComponent.Properties.Keys.Any(name => name.Equals("Index", StringComparison.OrdinalIgnoreCase))))
            {
                // Unknown component type — reconstruct raw text and preserve for round-trip.
                //
                // The indent is the component's real place in the file, not a fixed level: the property
                // lines inside the block are replayed exactly as they were read, so a Begin/End pair
                // generated at some other level would sit at odds with its own contents. A component at
                // depth d was written at indent (d-1)*3, which is the step VB6 uses.
                // The records its lines cite go with it, so a save puts them back and rewrites those citations to
                // wherever they land (#60). Web Browser.frm's ImageList holds six pictures this way. They used to be
                // dropped with the lines citing them, which held the form read-only.
                var subtreeRecords = new Dictionary<int, byte[]>();
                var everyCitationResolved = true;
                var subtreeLines = ReconstructRawSubtree(
                    serializedComponent, depth - 1, frxBlobs, subtreeRecords, ref everyCitationResolved);
                capturedBlobs.UnionWith(subtreeRecords.Values);
                // A record it cites is not in the companion, so a save could not put it back.
                if (!everyCitationResolved)
                    form.MarkUnmodelledBinaryProperty();
                // Recorded on the component it was read from, so the writer can put it back inside that
                // container rather than just inside the root's closing End. The root is always a modelled
                // class — VisualRootTypes is checked above and every member of it maps to
                // FormComponentClass — so parent is never null here.
                parent!.AddPreservedChildSubtree(ordinal, preservedSubtreesSeen++, string.Join("\r\n", subtreeLines),
                    subtreeRecords.Count > 0 ? subtreeRecords : null);
                // A control HexIDE runs is kept the same way and saves byte for byte, so there is nothing to warn of: the
                // warning used to tell the user, as the project opened, that a control which works did not (#1084, #2327).
                if (!RunsWhenCarried(serializedComponent.Type, serializedComponent.SubComponents.Count != 0,
                        serializedComponent.Properties.Keys.Any(name => name.Equals("Index", StringComparison.OrdinalIgnoreCase))))
                    errorSink.LogError("Str.ProjectLoad.UnknownControl", "Class {0} of control {1} is not a supported control class — preserved as unknown.", serializedComponent.Type, serializedComponent.Name);

                // No depth contribution. The block is recorded on the component it was read from, indented
                // to its real load depth, and re-emitted at the ordinal it held among that component's
                // children — so it comes back exactly where it was, at any depth, under any parent class.
                //
                // This used to count, and correctly: while modelled siblings were still being flattened to
                // form level, a form holding both was not reproducible even though the preserved half was.
                // That is what Splash Screen.frm is — an unmodelled VB.Image beside eight modelled Labels
                // inside a Frame — and it is why this line existed until the writer learned to nest.
                //
                // Any blob the block references is a separate matter, flagged just above: the text survives
                // but the .frx-referencing property lines are dropped, which is the binary cause, not this
                // one.
                return;
            }

            var instance = new ComponentInstance(componentClass, serializedComponent.Name);
            if (componentClass is Components.InBox.StatusBarComponentClass)
            {
                instance.SetProperty(Components.InBox.StatusBarComponentClass.SavedPanelsProperty, serializedComponent);
                instance.SetProperty(Components.InBox.StatusBarComponentClass.PanelPicturesProperty, frxBlobs);
            }
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

            // The containment link, recorded where the parent is already in hand. Two exclusions, both
            // deliberate:
            //
            // A menu is never contained. It is not drawn inside anything, and its own tree is the SubItems
            // list above; putting a top-level menu into the form's ContainedControls would make every later
            // walk — the writer's, the runtime's canvas placement, the designer's origin walk — have to
            // special-case it back out again.
            //
            // A parent that is not one of the three container classes gets no link either. The .frm format
            // permits writing a control nested under a ListBox and VB6 loads it without complaint, so that
            // is corrupt input rather than an exotic container: leaving the link unrecorded is what keeps
            // the depth counter below seeing the nesting, so the refusal gate still fires on it.
            if (!isMenu && parent is not null && ContainerClasses.IsContainer(parent.BaseClass))
                instance.SetContainer(parent);

            // Depth HexIDE cannot reproduce. Two kinds of nesting are now excluded, and both are excluded
            // for the same reason: something records the parent and something walks it back out.
            //
            // A menu under a menu, or under the form, rides on the parent's SubItems.
            //
            // A control under a container rides on the containment link recorded just above — and that link
            // is only recorded when the parent is genuinely a Form, PictureBox or Frame. So the condition is
            // simply "was a link recorded": a control nested under a ListBox gets none, still contributes its
            // depth, and still holds the form read-only, which is the right answer for input the format
            // permits and VB6 accepts silently. A component nested under an add-in-registered class is the
            // same case.
            //
            // The old arm for a null parent is gone. LoadRecur is called with null exactly once, for the
            // root, and a root that is not a visual root type has already been rejected — so `parent is
            // null` could only ever describe the form itself, which contributes nothing anyway.
            var ridesOnARecordedTree = isMenu ? parentIsMenu || parent is null : instance.Container is not null;
            if (!ridesOnARecordedTree)
                maxUnreproducibleDepth = Math.Max(maxUnreproducibleDepth, depth);

            // The root's two rectangles, gathered as the properties go past so the offset between them can
            // be worked out once the whole block has been read. Both stay in twips, exactly as the file
            // wrote them: the model's own copy goes through a divide-by-15 into pixels, and taking the
            // difference on that side would fold two roundings into a number whose entire job is to be
            // added back on save. Empty for every component that is not the root.
            var rootClientRect = new Dictionary<string, double>(StringComparer.Ordinal);
            var rootOuterRect = new Dictionary<string, double>(StringComparer.Ordinal);

            // The root's declared scale, likewise gathered as the properties go past. ScaleMode defaults to
            // Twip when the file omits it, which is what VB6 omitting a default-valued property means.
            int? rootScaleMode = null;
            double? rootScaleWidth = null;
            double? rootScaleHeight = null;

            // Known properties whose value could not be read, kept as raw lines below.
            var unparsedKnownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var serializedProperty in serializedComponent.Properties)
            {
                // One spelling, before anything compares it. VB6 reads a property name in any case: measured,
                // `caption`, `TEXT` and `backcolor` on controls, and on a form's own root `clientwidth`,
                // `scalewidth` and a Font block's `name` and `size`, all load. The special cases below compare
                // exact spellings, so a lowercase `width` on the root used to miss the outer-rectangle branch and
                // bind as the client width once property lookup ignored case (#819).
                var propertyName = CanonicalPropertyName(serializedProperty.Key, componentClass);

                // The two rectangles. A designer root records its CLIENT rectangle as Client*, and may
                // record its OUTER window rectangle beside it as the plain four — Dialog.frm is the file
                // in VB6's Template tree that does, and its two rectangles differ by the window frame.
                //
                // The model keeps the CLIENT rectangle, which is what the controls on the form are
                // positioned inside. The outer four are captured and then dropped rather than applied:
                // letting them through set the same four properties a second time, so the model ended up
                // holding whichever rectangle the file happened to write last — the client one for
                // twenty-one files and the outer one for Dialog.frm. See FormDefinition.OuterRect.
                var clientAxis = propertyName switch
                {
                    "ClientLeft" => "Left",
                    "ClientTop" => "Top",
                    "ClientWidth" => "Width",
                    "ClientHeight" => "Height",
                    _ => null,
                };
                if (clientAxis is not null)
                {
                    if (parent is null && TryReadNumber(serializedProperty.Value, out var clientTwips))
                        rootClientRect[clientAxis] = clientTwips;
                    propertyName = clientAxis;
                }
                else if (parent is null && propertyName is "Left" or "Top" or "Width" or "Height")
                {
                    if (TryReadNumber(serializedProperty.Value, out var outerTwips))
                        rootOuterRect[propertyName] = outerTwips;
                    continue;
                }
                // ScaleMode is captured but NOT consumed: it falls through to be preserved as a raw line,
                // comment and all, exactly as before. Capturing it here is only so the writer can tell
                // what units the Scale* pair beside it is in.
                if (propertyName == "ScaleMode" && parent is null && TryReadNumber(serializedProperty.Value, out var mode))
                    rootScaleMode = (int)mode;

                // Scale* is taken off the ROOT only. On a container it is content: ScaleMode is preserved
                // verbatim (it is not in SpecialCasedPropertyNames), so dropping Scale* wrote back a
                // container declaring a user-defined scale with no scale — and a container's scale is
                // exactly what gives its VB.Line children their units. Falling through preserves those.
                //
                // On the root the pair is recorded rather than dropped. The writer used to regenerate it
                // from the form's own width and height in twips regardless of the declared ScaleMode,
                // which is right only when that mode IS twips; and it cannot be derived at all under a
                // user scale, where the numbers are a coordinate system the developer chose.
                if ((propertyName == "ScaleHeight" || propertyName == "ScaleWidth") && parent is null)
                {
                    if (TryReadNumber(serializedProperty.Value, out var scale))
                    {
                        if (propertyName == "ScaleWidth") rootScaleWidth = scale;
                        else rootScaleHeight = scale;
                    }
                    continue;
                }

                // LockControls is a form-level metadata flag, not a component property.
                if (propertyName == "LockControls" && componentClass is FormComponentClass)
                {
                    var lv = serializedProperty.Value;
                    if (lv is int li) form.LockControls = li != 0;
                    else if (lv is double ld) form.LockControls = (int)ld != 0;
                    continue;
                }

                // A UserControl's or PropertyPage's root lists no Left or Top, as VB6's Properties window gives it none
                // (#1585), but its file's ClientLeft and ClientTop are its geometry all the same, as a form's are.
                if (!componentClass.PropertiesByName.TryGetValue(propertyName, out var propertyClass)
                    && !(clientAxis is not null && parent is null && RootClientAxes.TryGetValue(propertyName, out propertyClass)))
                    continue; // unknown property — preserved verbatim by OrderedRawProperties loop below

                var val = serializedProperty.Value;
                void InvalidValue()
                {
                    errorSink.LogError("Str.ProjectLoad.InvalidValue", "Property {0} in {1} had an invalid value.", serializedProperty.Key, serializedComponent.Name);
                    // Kept as written, so a save puts it back rather than losing it: a palette colour
                    // (&H02C0C0C0&) is a value VB6 accepts that HexIDE cannot hold yet (#786).
                    unparsedKnownNames.Add(serializedProperty.Key);
                }
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
                                errorSink.LogError("Str.ProjectLoad.MissingOffset", "Property {0} in {1}: .frx offset not found.", serializedProperty.Key, serializedComponent.Name);
                        }
                        // No companion at all: skip, leaving the blob null. Silent HERE by design — this
                        // arm cannot tell a missing file from a form that never had one, and refusing to
                        // load is worse than opening read-only. The citation is counted regardless, by
                        // the gate below, so the form is held unsaveable rather than quietly stripped on
                        // the next Ctrl+S. That count is what makes this skip safe (#146); before it, this
                        // line was the whole of the loss.
                    }
                    else
                    {
                        // Not a .frx reference — ignore (binary properties must come from .frx)
                    }
                }
                else if (propertyClass.PropertyType == typeof(object))
                {
                    // A Variant-typed property — Tag, which ComponentBaseClass puts on EVERY component.
                    // There was no branch for it here, so the value fell through every arm of this switch
                    // and was silently discarded: `Tag = "2407"` in VB6's own Mover ListBox.frm went in and
                    // never came out. Stored exactly as parsed; the writer re-dispatches on the runtime
                    // type, so a string comes back quoted and a number bare.
                    instance.SetUntypedProperty(propertyClass, val);
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
                    // Size is fractional and the others were never read at all. VB6 writes Size = 9.6 and
                    // Charset = 0 in its own templates; rounding the first and inventing the second is how a
                    // save changed the font of a form nobody had edited.
                    static double SizeOr(object? v, double fallback)
                    {
                        try { return Convert.ToDouble(v); }
                        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return fallback; }
                    }
                    var fontNameStr = fontName as string ?? "MS Sans Serif";
                    var font = new VBFont(
                        fontNameStr,
                        SizeOr(fontSize, 8),
                        italic: MetricOr(italic, 0) != 0,
                        // Absent keys keep VB6's own defaults rather than a HexIDE invention: Charset 0 is
                        // ANSI, and a font VB6 did not mark is neither underlined nor struck through.
                        charset: fontProps.TryGetValue("Charset", out var cs) ? MetricOr(cs, 0) : 0,
                        underline: fontProps.TryGetValue("Underline", out var ul) && MetricOr(ul, 0) != 0,
                        strikethrough: fontProps.TryGetValue("Strikethrough", out var st) && MetricOr(st, 0) != 0,
                        weight: MetricOr(fontWeight, VBFont.NormalWeight));
                    instance.SetUntypedProperty(propertyClass, font);
                }
            }

            // The offset between the root's two rectangles, once both have been read. Recorded only when
            // the file declared an outer rectangle at all — nineteen of VB6's twenty-two designer files
            // declare none, and a save must not invent one for them. An axis the file left out of either
            // rectangle offsets by zero, which reproduces the outer number it did write.
            if (parent is null && rootOuterRect.Count > 0)
            {
                double Offset(string axis) =>
                    rootOuterRect.TryGetValue(axis, out var outer)
                        ? outer - (rootClientRect.TryGetValue(axis, out var client) ? client : outer)
                        : 0;

                form.OuterRect = new RootOuterRect(
                    Offset("Left"), Offset("Top"), Offset("Width"), Offset("Height"));
            }

            if (parent is null && (rootScaleMode is not null || rootScaleWidth is not null || rootScaleHeight is not null))
                form.Scale = new RootScale(
                    rootScaleMode ?? (int)VBScaleMode.Twip, rootScaleWidth, rootScaleHeight);

            // Collect unknown raw property lines for round-trip preservation, and the lines of known properties whose
            // value could not be read. Dropping those lost them on the next save: a palette colour on a TextBox, the
            // moment TextBox modelled BackColor (#795).
            var knownNames = BuildKnownPropertyNames(componentClass, includeFormLevelNames: parent is null);
            foreach (var (name, rawLines) in serializedComponent.OrderedRawProperties)
            {
                if (knownNames.Contains(name) && !unparsedKnownNames.Contains(name))
                    continue;
                // Carried with the records it cites, so a save puts them back and rewrites its citations to wherever
                // they land (#60).
                var records = new Dictionary<int, byte[]>();
                if (!AddCitedRecords(rawLines, frxBlobs, records))
                {
                    // A record it cites is not in the companion, so a save could not put it back. The property is
                    // dropped, and the form records it: the save path then leaves the companion untouched rather
                    // than truncating or deleting the user's images.
                    form.MarkUnmodelledBinaryProperty();
                    errorSink.LogError("Str.ProjectLoad.MissingRecord", "Property '{0}' in '{1}' cites a record its companion does not hold, so the form cannot be saved.", name, serializedComponent.Name);
                    continue;
                }
                capturedBlobs.UnionWith(records.Values);
                instance.UnknownRawProperties.Add(
                    new ComponentInstance.UnknownRawProperty(name, rawLines, records.Count > 0 ? records : null));
            }
            if (componentClass is Components.InBox.MaskedEdBoxComponentClass)
            {
                MaskedEdBoxPersistence.Loaded(instance, serializedComponent);
                if (!MaskedEdBoxPersistence.CanWrite(serializedComponent))
                    form.MarkUnfaithfulToSave(UnfaithfulSaveCause.UnimplementedControlPersistence,
                        "MaskEdBox persistence is not implemented (#1098).");
            }
            if (componentClass is RichTextBoxComponentClass)
            {
                try { _ = RichTextBoxPersistence.RtfToWrite(instance); }
                catch (NotImplementedException ex)
                {
                    form.MarkUnfaithfulToSave(UnfaithfulSaveCause.UnreproducibleBinaryContent, ex.Message);
                }
            }
            if (componentClass is Components.InBox.TabStripComponentClass)
            {
                instance.SetProperty(TabStripPersistence.SavedProperty, serializedComponent);
                if (!TabStripPersistence.CanWrite(serializedComponent)
                    || serializedComponent.Properties.GetValueOrDefault("_ExtentX") is not int
                    || serializedComponent.Properties.GetValueOrDefault("_ExtentY") is not int)
                    form.MarkUnfaithfulToSave(UnfaithfulSaveCause.UnimplementedControlPersistence,
                        "TabStrip persistence is not implemented (#1089).");
            }
            // An in-box control is written back under the type name it was read under, and with the extents it was read with
            // until it is sized (#1082).
            if (componentClass is Components.InBox.InBoxComponentClass inBox)
                inBox.Loaded(instance, serializedComponent.Type);
            else if (componentClass is Components.InBox.TreeViewComponentClass)
            {
                instance.SetProperty(Components.InBox.InBoxComponentClass.LoadedTypeNameProperty, serializedComponent.Type);
                instance.SetProperty(Components.InBox.InBoxComponentClass.ExtentBasisProperty,
                    new Components.InBox.InBoxComponentClass.ExtentBasis(
                        instance.GetPropertyOrDefault(VBProperties.WidthProperty), instance.GetPropertyOrDefault(VBProperties.HeightProperty)));
            }
            if (componentClass is Components.InBox.ImageListComponentClass
                && serializedComponent.Properties.TryGetValue("Images", out var imagesValue)
                && imagesValue is Dictionary<string, object> imageProperties)
            {
                var pictures = new List<Components.InBox.ImageListPicture>();
                foreach (var entry in imageProperties)
                    if (entry.Value is Dictionary<string, object> image
                        && image.TryGetValue("Picture", out var citation) && citation is string reference
                        && frxBlobs is not null && FrxDeserializer.TryExtractBlob(reference, frxBlobs) is { } record)
                    {
                        pictures.Add(new Components.InBox.ImageListPicture(record,
                            image.GetValueOrDefault("Key") as string ?? "",
                            image.GetValueOrDefault("Object.Tag") as string ?? ""));
                    }
                // Unknown group members stay in the carried block, rather than disappearing on the next save.
                var knownImages = imageProperties.Keys.All(k => k == "NumListImages" || k.StartsWith("ListImage", StringComparison.Ordinal))
                    && imageProperties.Values.OfType<Dictionary<string, object>>()
                        .All(image => image.Keys.All(k => k is "Picture" or "Key" or "Object.Tag")
                            && (!image.TryGetValue("Key", out var key) || key is string)
                            && (!image.TryGetValue("Object.Tag", out var tag) || tag is string));
                if (knownImages && imageProperties.GetValueOrDefault("NumListImages") is int count && pictures.Count == count)
                    instance.SetProperty(Components.InBox.ImageListComponentClass.PicturesProperty, pictures);
            }

            if (componentClass is Components.InBox.SSTabComponentClass)
            {
                instance.SetProperty(Components.InBox.SSTabSavedState.Property,
                    new Components.InBox.SSTabSavedState(serializedComponent.Properties));
                form.MarkUnfaithfulToSave(UnfaithfulSaveCause.SSTabPageMetadata,
                    "SSTab page metadata cannot yet be regenerated after designer edits (#1090)");
            }

            // A missing Font block takes a value copy of the form's font, never the container's (#2062).
            if (parent is not null && rootComponent.Type == "VB.Form" &&
                componentClass.Properties.Contains(VBProperties.FontProperty) &&
                !serializedComponent.Properties.Keys.Any(k => k.Equals("Font", StringComparison.OrdinalIgnoreCase)) &&
                components[0].TryGetProperty(VBProperties.FontProperty, out var formFont))
                instance.SetProperty(VBProperties.FontProperty, formFont);

            for (var i = 0; i < serializedComponent.SubComponents.Count; i++)
                LoadRecur(serializedComponent.SubComponents[i], instance, depth + 1, i);
        }

        LoadRecur(rootComponent, null, 1, 0);
        form.RecordUnreproducibleNestingDepth(maxUnreproducibleDepth);

        // The refusal gate, decided here rather than from the parser's raw Begin depth, because only the walk
        // above knows which nesting rides on a tree something walks back out.
        //
        // What is left after #84 is nesting under a class that is NOT a container: the format permits writing
        // a control inside a ListBox, and VB6 loads such a file without complaint, so it is corrupt input
        // rather than an exotic container. HexIDE has nowhere to host it and no link recorded for it, so a
        // save would re-parent it onto the form with its container-relative coordinates intact. A component
        // nested under a class an add-in registered is the same case, for the same reason: HexIDE cannot host
        // arbitrary children inside a control it did not build.
        //
        // Menus and real containers no longer reach here at all.
        if (maxUnreproducibleDepth > 2)
            form.MarkUnfaithfulToSave(UnfaithfulSaveCause.NestedContainers,
                "it nests a control inside a class that is not a container, which HexIDE would re-parent onto the form on save");

        // Binary content the writer cannot re-emit. A record HexIDE does not model is carried now, with the
        // property or preserved block citing it (#60), so what is left is a citation the companion cannot honour,
        // and a property that IS named on a modelled control but whose CLR type is not byte[]. The first is
        // flagged during the walk. The second is dropped with no diagnostic at all, as ODBC Log In's ComboBox List
        // was until List was modelled as its record. The count comparison is the safety net for that silent case,
        // and it is why this is not simply "did anything set the flag".
        //
        // The bytes themselves are never lost: WriteCompanionBinary leaves the companion alone. What is
        // lost is the reference, so the picture disappears from the control while the file that holds it
        // stays on disk. That is a save which looks like it worked.
        // Counted against what the SOURCE cites, not against what the companion happened to yield.
        //
        // This used to be `frxBlobs?.Count ?? 0`, which asks the companion how much it holds — so with no
        // companion at all the test became `capturedBlobs.Count < 0`, always false, and a form separated
        // from its .frx was never flagged. A plain Ctrl+S then rewrote it with every citation dropped, and
        // the result reopened as faithful because the citations were the only thing that would have flagged
        // it. That is #146, and it hit the ORIGINAL file rather than a copy.
        //
        // The citations are in the .frm, so they are always available — the check no longer depends on the
        // file that may be the missing one. It also closes a case nobody had noticed: a TRUNCATED companion,
        // where an out-of-range offset is filtered out of the blob dictionary AND fails to extract, so the
        // two counts used to agree at a number lower than the form actually cites.
        var citedBlobCount = FrxDeserializer.CitedOffsets(source).Distinct().Count();
        // Kept on the model as well: the save path needs it to tell a companion this form referenced from
        // one it never did, and only the latter must survive a save that produces no blobs (#148).
        form.RecordCitedCompanionBlobCount(citedBlobCount);
        if (form.HasUnmodelledBinaryProperties || capturedBlobs.Count < citedBlobCount)
            form.MarkUnfaithfulToSave(UnfaithfulSaveCause.UnreproducibleBinaryContent,
                $"it references companion binary content HexIDE cannot re-emit "
                + $"({capturedBlobs.Count} of {citedBlobCount} blob(s) reached the model)");

        // Rendering also serves dirty checks; only the save path may refuse missing extents (#1631).
        if (components.Any(c => c.BaseClass is UserControlComponentClass uc && uc.Setting("InvisibleAtRuntime")
            && (!c.UnknownRawProperties.Any(p => p.Name.Equals("_ExtentX", StringComparison.OrdinalIgnoreCase))
                || !c.UnknownRawProperties.Any(p => p.Name.Equals("_ExtentY", StringComparison.OrdinalIgnoreCase)))))
            form.MarkUnfaithfulToSave(UnfaithfulSaveCause.MissingUserControlExtents,
                "it contains an invisible UserControl without both recorded extents; saving its size is not implemented");

        form.UpdateCode(code);
        form.UpdateComponents(components);
        if (rootComponent.Type != "VB.Form")
            form.UpdateRootTypeName(rootComponent.Type);
        return form;
    }

    /// <summary>
    /// Property names this component does not need preserving verbatim, because the model already carries
    /// them. <paramref name="includeFormLevelNames"/> adds <see cref="SpecialCasedPropertyNames"/>, every
    /// member of which is meaningful only on the designer root: the Client* aliases, LockControls, and the
    /// Scale* pair the writer regenerates from the form's own size. On a container those same names are
    /// content, and claiming to know them is what silently dropped them.
    /// </summary>
    private static HashSet<string> BuildKnownPropertyNames(ComponentBaseClass componentClass, bool includeFormLevelNames)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in componentClass.Properties)
            names.Add(prop.Name);
        if (includeFormLevelNames)
            names.UnionWith(SpecialCasedPropertyNames);
        return names;
    }

    /// <summary>The names the loop special-cases, each in the spelling it compares against.</summary>
    private static readonly string[] SpecialCaseSpellings =
    [
        "ClientLeft", "ClientTop", "ClientWidth", "ClientHeight", "Left", "Top", "Width", "Height",
        "ScaleMode", "ScaleHeight", "ScaleWidth", "LockControls",
    ];

    /// <summary>
    /// A property name as the model spells it: a special-cased name first, then one of this component's
    /// properties, and otherwise as written, so an unknown name is preserved exactly.
    /// </summary>
    private static string CanonicalPropertyName(string name, ComponentBaseClass componentClass)
    {
        foreach (var spelling in SpecialCaseSpellings)
        {
            if (string.Equals(name, spelling, StringComparison.OrdinalIgnoreCase))
                return spelling;
        }
        return componentClass.PropertiesByName.TryGetValue(name, out var property) ? property.Name : name;
    }

    /// <summary>
    /// A .frm's numeric property values arrive as <c>int</c> or <c>double</c> depending on whether the
    /// literal had a decimal point — <c>ClientWidth = 6030</c> against <c>ScaleWidth = 5380.766</c> — so
    /// anything reading one before it knows the target property's CLR type has to accept both.
    /// </summary>
    private static bool TryReadNumber(object? value, out double number)
    {
        switch (value)
        {
            case int i: number = i; return true;
            case float f: number = f; return true;
            case double d: number = d; return true;
            default: number = 0; return false;
        }
    }

    /// <summary>
    /// Adds the companion records <paramref name="lines"/> cite to <paramref name="records"/>, keyed by the offset
    /// each cites. False when one of them is not in the companion, or there is no companion.
    /// </summary>
    private static bool AddCitedRecords(
        IEnumerable<string> lines, IReadOnlyDictionary<int, byte[]>? frxBlobs, Dictionary<int, byte[]> records)
    {
        var resolved = true;
        foreach (var line in lines)
        {
            if (FrxDeserializer.Citation(line) is not { } citation)
                continue;
            if (frxBlobs is not null && frxBlobs.TryGetValue(citation.Offset, out var record))
                records[citation.Offset] = record;
            else
                resolved = false;
        }
        return resolved;
    }

    /// <summary>
    /// The block an unmodelled control was read from, with the records its lines cite added to
    /// <paramref name="records"/>. When one of them is not in the companion, the property citing it is left out and
    /// <paramref name="resolved"/> is cleared.
    /// </summary>
    private static List<string> ReconstructRawSubtree(VBSerializedComponent component, int indentLevel,
        IReadOnlyDictionary<int, byte[]>? frxBlobs, Dictionary<int, byte[]> records, ref bool resolved)
    {
        var indent = new string(' ', indentLevel * 3);
        var lines = new List<string>();
        // Trailing space, as VB6 writes it. This line is REGENERATED rather than preserved — the block's
        // property lines are replayed verbatim but its Begin and End are rebuilt at the right indent — so
        // it is the one part of an unmodelled subtree that has to reproduce VB6's formatting itself.
        lines.Add($"{indent}Begin {component.Type} {component.Name} ");

        foreach (var (_, rawLines) in component.OrderedRawProperties)
        {
            // A record it cites is not in the companion. Dropped, as a carried property is, so nothing in the model
            // cites a record it does not hold; the form is held read-only.
            if (!AddCitedRecords(rawLines, frxBlobs, records))
            {
                resolved = false;
                continue;
            }
            lines.AddRange(rawLines);
        }

        foreach (var sub in component.SubComponents)
            lines.AddRange(ReconstructRawSubtree(sub, indentLevel + 1, frxBlobs, records, ref resolved));

        lines.Add($"{indent}End");
        return lines;
    }
}

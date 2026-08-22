using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using HexIDE.Controls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Integration.Tests.Controls;

/// <summary>
/// Issue #118 — the Properties window names an enum value the way VB6 does.
///
/// It used to name it the way C# does, off <c>Enum.GetNames</c>, which is a different string for 33 of the
/// values it shows. Most were a missing space or hyphen (<i>DashDotDot</i> for <i>Dash-Dot-Dot</i>); one was
/// a different word (<i>Grayscale</i> for <i>Grayed</i>); and one enum was not a spelling difference at all,
/// because <c>VBAlign</c>'s members are named <c>vbAlignTop</c> — so a Frame's Align property offered the
/// user "1 - vbAlignTop".
///
/// <para>
/// <b>Where the expectations come from.</b> A VB6 designer file records an enum as its number plus a comment
/// naming it — <c>Align = 1  'Align Top</c> — and those comments are written by VB6, so VB98's own Template
/// tree is a corpus of VB6's spellings. Every name asserted below was mined from it rather than recalled.
/// (The names that the corpus does NOT reach are not asserted here; they are carried by the same attributes
/// the serializer uses, so the designer and the file agree about them either way, which is the property that
/// actually matters and is what <see cref="EveryRenderedEnumValue_HasAVb6Name"/> guards.)
/// </para>
/// </summary>
public class PropertyEnumBoxNamingTests
{
    private static IReadOnlyList<string> OptionsFor(Type enumType)
    {
        var box = new PropertyEnumBox { PropertyType = enumType };
        return box.Options!.Select(o => o.Text).ToList();
    }

    /// <summary>
    /// (enum type, underlying value, the name VB6 writes) — every row mined from a comment in VB98's
    /// Template tree, so each is a string VB6 itself produced.
    /// </summary>
    public static TheoryData<Type, int, string> CorpusNames => new()
    {
        { typeof(VBAlign),            1, "Align Top" },        // Align           =   1  'Align Top
        { typeof(VBTextAlignment),    1, "Right Justify" },    // Alignment       =   1  'Right Justify
        { typeof(VBAppearance),       0, "Flat" },             // Appearance      =   0  'Flat
        { typeof(BackStyles),         0, "Transparent" },      // BackStyle       =   0  'Transparent
        { typeof(VBBorder),           0, "None" },             // BorderStyle     =   0  'None
        { typeof(VBBorder),           1, "Fixed Single" },     // BorderStyle     =   1  'Fixed Single
        { typeof(BorderStyles),       6, "Inside Solid" },     // BorderStyle     =   6  'Inside Solid
        { typeof(FillStyles),         0, "Solid" },            // FillStyle       =   0  'Solid
        { typeof(VBCursorType),      99, "Custom" },           // MousePointer    =  99  'Custom
        { typeof(ShapeTypes),         2, "Oval" },             // Shape           =   2  'Oval
        { typeof(VBStartupPosition),  2, "CenterScreen" },     // StartUpPosition =   2  'CenterScreen
        { typeof(VBStartupPosition),  3, "Windows Default" },  // StartUpPosition =   3  'Windows Default
    };

    [AvaloniaTheory]
    [MemberData(nameof(CorpusNames))]
    public void ARenderedEnumValue_IsNamedTheWayVB6NamesIt(Type enumType, int value, string vb6Name)
    {
        // CenterScreen has no space and Windows Default does: VB6's spacing is not a rule, it is a list, and
        // that is exactly why these are mined rather than derived.
        OptionsFor(enumType).Should().Contain($"{value} - {vb6Name}");
    }

    [AvaloniaFact]
    public void AlignNoLongerOffersItsCSharpMemberNames()
    {
        // The worst of the 33, and the one a user meets first — Align is on every Frame and PictureBox.
        var options = OptionsFor(typeof(VBAlign));

        options.Should().Equal("0 - None", "1 - Align Top", "2 - Align Bottom", "3 - Align Left", "4 - Align Right");
        options.Should().NotContain(o => o.Contains("vbAlign"));
    }

    [AvaloniaFact]
    public void NoOptionAnywhereStillReadsAsACSharpIdentifier()
    {
        // A cheap net over all of them: VB6 writes English words with spaces and hyphens, never a `vb` prefix
        // and never a leading underscore (VBAppearance._3D, which a member name cannot start with a digit).
        foreach (var enumType in RenderedEnumTypes())
            foreach (var option in OptionsFor(enumType))
            {
                var name = option.Split(" - ", 2)[1];
                name.Should().NotStartWith("vb", "an option on {0} is showing its C# member name", enumType.Name);
                name.Should().NotStartWith("_", "an option on {0} is showing its C# member name", enumType.Name);
            }
    }

    [AvaloniaFact]
    public void EveryRenderedEnumValue_HasAVb6Name()
    {
        // The invariant, rather than a list that goes stale: an enum the Properties window can show must be
        // able to name every one of its values the way VB6 does. Without this, adding a member without the
        // attribute silently falls back to the C# name for that one value only — the same defect as #118, at
        // one row instead of thirty-three, and invisible until someone opens that dropdown.
        var unnamed = new List<string>();
        foreach (var enumType in RenderedEnumTypes())
            foreach (var value in Enum.GetValues(enumType))
                if (Vb6EnumNames.For(value) is null)
                    unnamed.Add($"{enumType.Name}.{value}");

        unnamed.Should().BeEmpty("every enum value the Properties window renders needs a [Vb6Name]");
    }

    /// <summary>Every enum type behind a VB6 property — which is what the Properties window can put in a
    /// PropertyEnumBox.</summary>
    private static IEnumerable<Type> RenderedEnumTypes()
    {
        // PropertiesByName is filled by the PropertyClass constructor, so the declaring class has to have
        // been initialised before it is read. Nothing else in this test touches VBProperties.
        RuntimeHelpers.RunClassConstructor(typeof(VBProperties).TypeHandle);

        return VBProperties.PropertiesByName.Values
            .SelectMany(list => list)
            .Select(p => p.PropertyType)
            .Where(t => t.IsEnum)
            .Distinct();
    }
}

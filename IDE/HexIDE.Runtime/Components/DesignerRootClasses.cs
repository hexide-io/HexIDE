using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

/// <summary>
/// The root of a UserControl's designer. It was a form's component, so its Properties window listed a form's properties,
/// Caption, Icon and the window's buttons among them, which VB6's UserControl has none of (#1585). Its list is the
/// properties HexIDE models of the 51 VB6's Properties window gives a new UserControl (measured): the rest of those, and
/// any line a loaded file holds that isn't listed here, are kept as the file's own text.
/// </summary>
public sealed class UserControlRootClass : FormComponentClass
{
    private UserControlRootClass() : base([BackColorProperty,
        // A UserControl saved with Enabled = False is disabled where it is placed (#1101).
        EnabledProperty,
        FontProperty,
        ForeColorProperty,
        HeightProperty,
        KeyPreviewProperty,
        WidthProperty])
    {
    }

    public override string Name => "UserControl";
    public override string VBTypeName => "VB.UserControl";

    public static new UserControlRootClass Instance { get; } = new();
}

/// <summary>
/// The root of a PropertyPage's designer: the properties HexIDE models of the 33 VB6's Properties window gives a new
/// PropertyPage (measured, #1585). It has a Caption, the page's tab, and no Enabled, Icon or window buttons.
/// </summary>
public sealed class PropertyPageRootClass : FormComponentClass
{
    private PropertyPageRootClass() : base([BackColorProperty,
        CaptionProperty,
        FontProperty,
        ForeColorProperty,
        HeightProperty,
        KeyPreviewProperty,
        WidthProperty])
    {
    }

    public override string Name => "PropertyPage";
    public override string VBTypeName => "VB.PropertyPage";

    public static new PropertyPageRootClass Instance { get; } = new();
}

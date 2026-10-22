using System.Collections.Generic;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class MenuComponentClass : ComponentBaseClass
{
    // No bounds: a menu has no Left, Top, Width or Height in VB6, and a value set there was written into the .frm under
    // a name VB6 gives no menu (#923 review).
    public MenuComponentClass() : base([CaptionProperty, EnabledProperty, CheckedProperty, WindowListProperty], null, hasBounds: false)
    {
    }

    public override string Name => "Menu";
    public override string VBTypeName => "VB.Menu";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        return new VBMenu()
        {
            Header = instance.GetPropertyOrDefault(CaptionProperty),
            // VB6 marks a menu's access key with an ampersand — "&File" is drawn as File with the F
            // underlined, and Alt+F opens it. Without this the caption renders with a literal ampersand in
            // it, which is what every menu looked like until menus were drawn at all. A menu answers only to the
            // first character its caption marks, where a button answers to all of them (#920).
            HeaderTemplate = AccessTextDataTemplate.MenuAccess,
            IsEnabled = instance.GetPropertyOrDefault(EnabledProperty),
            Checked = instance.GetPropertyOrDefault(CheckedProperty),
            // Every item can be ticked from code (#1126), so every item draws its tick when Checked; VBMenu keeps a click
            // from toggling it.
            ToggleType = MenuItemToggleType.CheckBox,
            IsVisible = instance.GetPropertyOrDefault(VisibleProperty)
        };
    }

    public static PropertyClass<List<ComponentInstance>?> SubItemsProperty = new PropertyClass<List<ComponentInstance>?>("SubItems", "", PropertyCategory.Internal);

    public static ComponentBaseClass Instance { get; } = new MenuComponentClass();
}
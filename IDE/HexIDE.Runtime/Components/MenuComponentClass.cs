using System.Collections.Generic;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class MenuComponentClass : ComponentBaseClass
{
    public MenuComponentClass() : base([CaptionProperty, EnabledProperty, CheckedProperty, WindowListProperty])
    {
    }

    public override string Name => "Menu";
    public override string VBTypeName => "VB.Menu";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        return new MenuItem()
        {
            Header = instance.GetPropertyOrDefault(CaptionProperty),
            // VB6 marks a menu's access key with an ampersand — "&File" is drawn as File with the F
            // underlined, and Alt+F opens it. Without this the caption renders with a literal ampersand in
            // it, which is what every menu looked like until menus were drawn at all. The same template does
            // this for the button, check box and option button.
            HeaderTemplate = AccessTextDataTemplate.Access,
            IsEnabled = instance.GetPropertyOrDefault(EnabledProperty),
            IsChecked = instance.GetPropertyOrDefault(CheckedProperty),
            ToggleType = instance.GetPropertyOrDefault(CheckedProperty) ? MenuItemToggleType.CheckBox : MenuItemToggleType.None,
            IsVisible = instance.GetPropertyOrDefault(VisibleProperty)
        };
    }

    public static PropertyClass<List<ComponentInstance>?> SubItemsProperty = new PropertyClass<List<ComponentInstance>?>("SubItems", "", PropertyCategory.Internal);

    public static ComponentBaseClass Instance { get; } = new MenuComponentClass();
}
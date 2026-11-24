using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.Components;

namespace HexIDE.VisualDesigner;

internal static class MenuPropertyNames
{
    public const string SubItems = "SubItems";
    public const string Shortcut = "Shortcut";
    public const string NegotiatePosition = "NegotiatePosition";

    public static PropertyClass<List<ComponentInstance>?> SubItemsProperty =>
        (PropertyClass<List<ComponentInstance>?>)VBProperties.PropertiesByName[SubItems].Single();

    public static PropertyClass<string> ShortcutProperty(IComponentClass menuClass) =>
        (PropertyClass<string>)menuClass.PropertiesByName[Shortcut];

    public static PropertyClass<int> NegotiatePositionProperty(IComponentClass menuClass) =>
        (PropertyClass<int>)menuClass.PropertiesByName[NegotiatePosition];
}

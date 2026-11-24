using System.Globalization;
using Avalonia.Headless.XUnit;
using HexIDE.Converters;
using HexIDE.Localization;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Integration.Tests.Views;

public class VBColorDescriptionConverterTests
{
    [AvaloniaFact]
    public void System_color_name_uses_the_active_pack()
    {
        var localization = new LocalizationService();
        localization.Apply("pseudo");
        try
        {
            var color = VBColor.FromSystemColor(VbSystemColor.Btnface);
            var name = VBColorDescriptionConverter.Instance.Convert(
                color, typeof(string), null, CultureInfo.InvariantCulture);

            name.Should().BeOfType<string>().Which.Should().StartWith("⟦").And.EndWith("⟧");
            name.Should().Be(localization.GetString("Str.SystemColor.Btnface"));
        }
        finally
        {
            localization.Apply("en");
        }
    }
}

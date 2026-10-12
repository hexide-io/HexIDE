using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// <c>AddItem</c>'s Item is a String, so a list holds the text CStr makes of what was added (#772). It used to hold
/// the value itself, which the list then showed in .NET's form: <c>List1.AddItem CSng(1) / 3</c> listed
/// <c>0.33333334</c> where VB6 lists <c>0.3333333</c>.
/// </summary>
public class AddItemTextTests
{
    private const string FormWithList = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.ListBox List1
              Left            =   120
              Top             =   120
              Width           =   2000
              Height          =   1200
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [AvaloniaTheory]
    [InlineData("List1.AddItem CSng(1) / 3", "0.3333333")]
    [InlineData("List1.AddItem 0.1 + 0.2", "0.3")]
    [InlineData("List1.AddItem #1/2/2003#", "02/01/2003")]
    public async Task A_list_holds_the_text_CStr_makes_of_the_item(string code, string expected)
    {
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(FormWithList);
        using (VbLocale.Use(CultureInfo.GetCultureInfo("en-GB")))   // the oracle's locale
            await ContainerRuntimeHarness.Run(ctx, env, code + "\n");

        var list = (ItemsControl)ContainerRuntimeHarness.Child(canvas, "List1");
        list.Items.Cast<object?>().Should().Equal([expected]);
    }
}

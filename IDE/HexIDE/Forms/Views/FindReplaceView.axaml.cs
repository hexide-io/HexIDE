using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;

namespace HexIDE.Forms.Views;

public partial class FindReplaceView : UserControl
{
    public FindReplaceView()
    {
        InitializeComponent();

        // Direction's access key focuses its group, and the group hands focus to its selected option, where Tab would
        // have entered it, as a Windows option group does (#2738, #4142).
        DirectionGroup.GotFocus += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, DirectionGroup))
                return;
            var options = DirectionGroup.Children.OfType<RadioButton>().ToArray();
            (options.FirstOrDefault(option => option.IsChecked == true) ?? options.FirstOrDefault())
                ?.Focus(NavigationMethod.Tab);
        };
    }
}

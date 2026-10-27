using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media;
using AvaloniaEdit;
using HexIDE.Controls;
using HexIDE.IDE;

namespace HexIDE.Integration.Tests.Controls;

public class BracketDepthRenderingTests
{
    [AvaloniaFact]
    public void ColoursFollowDepthEditsResourcesAndOption()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.ColorBracketsByDepth.Returns(true);
        var editor = new TextEditor { Text = "(((()))) )", Foreground = Brushes.Black };
        editor.Resources["BracketDepth1Brush"] = Brushes.Blue;
        editor.Resources["BracketDepth2Brush"] = Brushes.Green;
        editor.Resources["BracketDepth3Brush"] = Brushes.Purple;
        editor.Resources["BracketUnmatchedBrush"] = Brushes.Red;
        using var colorizer = new BracketDepthColorizer(editor, editor, settings);
        editor.TextArea.TextView.LineTransformers.Add(colorizer);
        var window = new Window { Width = 500, Height = 200, Content = editor };
        try
        {
            window.Show();
            IBrush?[] Colours()
            {
                Dispatcher.UIThread.RunJobs();
                editor.Measure(new Size(500, 200));
                editor.Arrange(new Rect(0, 0, 500, 200));
                editor.UpdateLayout();
                var view = editor.TextArea.TextView;
                view.EnsureVisualLines();
                return view.VisualLines.Single().Elements
                    .SelectMany(e => Enumerable.Repeat(e.TextRunProperties.ForegroundBrush, e.DocumentLength)).ToArray();
            }
            Colours().Should().Equal(Brushes.Blue, Brushes.Green, Brushes.Purple, Brushes.Blue,
                Brushes.Blue, Brushes.Purple, Brushes.Green, Brushes.Blue, Brushes.Black, Brushes.Red);
            editor.Text = "(";
            Colours().Should().Equal(Brushes.Red);
            editor.Text = "()";
            Colours().Should().Equal(Brushes.Blue, Brushes.Blue);
            editor.Resources["BracketDepth1Brush"] = Brushes.Orange;
            Colours().Should().Equal(Brushes.Orange, Brushes.Orange);
            settings.ColorBracketsByDepth.Returns(false);
            settings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(settings,
                new PropertyChangedEventArgs(nameof(ISettingsService.ColorBracketsByDepth)));
            Colours().Should().Equal(Brushes.Black, Brushes.Black);
        }
        finally { window.Close(); }
    }
}

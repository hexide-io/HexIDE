using HexIDE.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using HexIDE.Runtime.Debugging;
using Avalonia.Headless.XUnit;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Automation;

public class NativePickerFeedbackTests
{
    [AvaloniaTheory]
    [InlineData("menu")]
    [InlineData("interact")]
    [InlineData("clear")]
    [InlineData("tree")]
    public async Task Tools_report_an_open_native_picker_and_refuse_actions_under_it(string tool)
    {
        ScriptedFileDialogs.Clear();
        var manager = new WindowManager(Substitute.For<ILocalizationService>());
        var completion = new TaskCompletionSource<int>();
        var pending = manager.TrackNativeDialog(() => completion.Task, "Save As", "Project1.vbp");
        var tools = new HexIdeTools(null!);
        try
        {
            ScriptedFileDialogs.AnswerNextWith("still-armed.frm");
            switch (tool)
            {
                case "menu":
                    var menu = await tools.InvokeMenuItemAsync("File/Save Project As", TestContext.Current.CancellationToken);
                    menu.Success.Should().BeFalse();
                    menu.Error.Should().Contain("native file picker");
                    ScriptedFileDialogs.Pending.Should().Be(1);
                    break;
                case "interact":
                    var action = await tools.InteractAsync("Window/Button", "invoke");
                    action.Success.Should().BeFalse();
                    action.Error.Should().Contain("native file picker");
                    ScriptedFileDialogs.Pending.Should().Be(1);
                    break;
                case "clear":
                    var clear = await tools.ClearFileDialogAnswersAsync();
                    clear.Note.Should().Contain("native file picker").And.Contain("does not close");
                    ScriptedFileDialogs.Pending.Should().Be(0);
                    break;
                case "tree":
                    var tree = await tools.DumpVisualTreeAsync();
                    tree.Note.Should().Contain("native file picker").And.Contain("outside the control tree");
                    break;
            }
        }
        finally
        {
            completion.SetResult(1);
            await pending;
            ScriptedFileDialogs.Clear();
        }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Picker_feedback_ends_on_completion_or_failure(bool fails)
    {
        var manager = new WindowManager(Substitute.For<ILocalizationService>());
        var completion = new TaskCompletionSource<int>();
        var pending = manager.TrackNativeDialog(() => completion.Task, "Save As", "Project1.vbp");
        var tools = new HexIdeTools(null!);
        try
        {
            (await tools.ClearFileDialogAnswersAsync()).Note.Should().Contain("native file picker");
        }
        finally
        {
            if (fails)
            {
                completion.SetException(new System.InvalidOperationException("Picker failed"));
                await ((System.Func<Task>)(async () => await pending)).Should().ThrowAsync<System.InvalidOperationException>();
            }
            else
            {
                completion.SetResult(42);
                (await pending).Should().Be(42);
            }
        }
        (await tools.ClearFileDialogAnswersAsync()).Note.Should().NotContain("native file picker");
    }

    [AvaloniaTheory]
    [InlineData("menu")]
    [InlineData("interact")]
    public async Task The_action_that_opens_a_picker_reports_it_in_its_reply(string route)
    {
        var manager = new WindowManager(Substitute.For<ILocalizationService>());
        var completion = new TaskCompletionSource<int>();
        Task<int>? pending = null;
        var command = Substitute.For<ICommand>();
        command.CanExecute(Arg.Any<object?>()).Returns(true);
        command.When(c => c.Execute(Arg.Any<object?>())).Do(_ => pending = manager.TrackNativeDialog(() => completion.Task, "Save As", "Project1.vbp"));
        var button = new Button { Name = "Save", Content = "Save", Command = command };
        var window = new Window { Content = new StackPanel { Children =
        {
            new Menu { Items = { new MenuItem { Header = "File", Items =
            {
                new MenuItem { Header = "Save Project As", Command = command }
            } } } }, button
        } } };
        var lifetime = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        lifetime.MainWindow.Returns(window);
        // As in ProgramPrintedStartTests, lend the initialized headless app a desktop lifetime.
        var lifetimeField = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(IApplicationLifetime));
        var previous = lifetimeField.GetValue(Application.Current!);
        lifetimeField.SetValue(Application.Current!, lifetime);
        using var context = new IdeContext(null!, null!, Substitute.For<HexIDE.Lsp.ILspClient>(),
            null!, null!, null!, null!, null!, null!, Substitute.For<IDebugController>(), null!, null!, null!, null!,
            null!, null!, null!, null!, null!, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        var tools = new HexIdeTools(context);
        window.Show();
        try
        {
            if (route == "menu")
            {
                var reply = await tools.InvokeMenuItemAsync("File/Save Project As", TestContext.Current.CancellationToken);
                reply.Success.Should().BeTrue();
                reply.Note.Should().Contain("native file picker").And.Contain("Save As").And.Contain("Project1.vbp");
            }
            else
            {
                var reply = await tools.InteractAsync("Window/Button[Save]", "invoke", window: "ide");
                reply.Success.Should().BeTrue();
                reply.Detail.Should().Contain("native file picker").And.Contain("Save As").And.Contain("Project1.vbp");
            }
        }
        finally
        {
            completion.SetResult(1);
            if (pending is not null) await pending;
            window.Close();
            lifetimeField.SetValue(Application.Current!, previous);
        }
    }

}

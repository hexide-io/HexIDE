using Avalonia.Controls;
using Avalonia.Threading;

namespace HexIDE.Integration.Tests.Views;

public class ForegroundWindowLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(nameof(ForegroundWindowTests.Ide_scope_wins_over_a_dialog_too))]
    [InlineData(nameof(ForegroundWindowTests.A_dialog_over_a_running_form_is_the_foreground_window))]
    [InlineData(nameof(ForegroundWindowTests.A_dialog_over_the_main_window_is_still_found))]
    [InlineData(nameof(ForegroundWindowTests.A_dialog_outranks_a_form_opened_after_it))]
    [InlineData(nameof(ForegroundWindowTests.Closing_the_dialog_hands_the_foreground_back_to_the_form))]
    [InlineData(nameof(ForegroundWindowTests.Two_windows_of_one_name_are_refused_as_ambiguous_and_a_hidden_one_as_unreachable))]
    [InlineData(nameof(ForegroundWindowTests.A_path_that_resolves_in_the_window_auto_picked_reaches_its_control))]
    public void Disposing_a_foreground_test_closes_every_window_it_opened(string method)
    {
        var opened = new List<Window>();
        var open = new HashSet<Window>();
        var closed = new List<Window>();
        var owners = new Dictionary<Window, Window>();
        using var opening = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) =>
        {
            opened.Add(w);
            open.Add(w);
            if (w.Owner is Window owner)
                owners.Add(w, owner);
        });
        using var closing = Window.WindowClosedEvent.AddClassHandler<Window>((w, _) =>
        {
            closed.Add(w);
            open.Remove(w);
        });
        try
        {
            var tests = new ForegroundWindowTests();
            try
            {
                typeof(ForegroundWindowTests).GetMethod(method)!.Invoke(tests, null);
            }
            finally
            {
                (tests as IDisposable)?.Dispose();
            }
            Dispatcher.UIThread.RunJobs();

            opened.Should().NotBeEmpty();
            open.Should().BeEmpty("no window should outlive its test, even when hidden");
            foreach (var (dialog, owner) in owners)
                closed.IndexOf(dialog).Should().BeLessThan(closed.IndexOf(owner));
        }
        finally
        {
            foreach (var window in opened.AsEnumerable().Reverse())
                window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}

using System;
using Avalonia.Controls;

namespace HexIDE.Integration.Tests;

public class WindowLeakGuardTests
{
    [AvaloniaTheory]
    [InlineData("MsgBox", false)]
    [InlineData("Options", false)]
    [InlineData("Hidden form", true)]
    public void The_suite_guard_names_an_unclosed_window_and_the_test_that_opened_it(string title, bool hidden)
    {
        var window = new Window { Title = title };
        window.Show();
        if (hidden)
            window.Hide();
        try
        {
            Action check = WindowLeakGuard.Check;
            check.Should().Throw<Exception>().WithMessage("*" + title + "*")
                .Which.Message.Should().Contain(TestContext.Current.Test!.TestDisplayName);
        }
        finally { window.Close(); }
        WindowLeakGuard.Open.Should().NotContainKey(window, "closing a window removes it from the guard");
    }
}

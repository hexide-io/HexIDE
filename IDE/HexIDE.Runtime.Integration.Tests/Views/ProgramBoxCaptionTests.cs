using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Runtime;

namespace HexIDE.Runtime.Integration.Tests.Views;

// Host fallback policy from #5646, not a measurement of VB6 captions.
public class ProgramBoxCaptionTests
{
    [AvaloniaTheory]
    [InlineData("MsgBox \"x\"")]
    [InlineData("x = InputBox(\"x\")")]
    public async Task A_form_without_a_project_uses_an_empty_box_caption(string statement)
    {
        var window = new VBFormRuntime { Width = 300, Height = 250 };
        try
        {
            window.Show();
            window.Context.SetCode($"Private Sub Form_Load()\n    {statement}\nEnd Sub\n", "Form1");
            var run = window.Context.StartAsync();
            for (var attempt = 0; attempt < 50 && window.OwnedWindows.Count == 0; attempt++)
            {
                var context = System.Threading.ExecutionContext.Capture();
                Dispatcher.UIThread.RunJobs();
                System.Threading.ExecutionContext.Restore(context!);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            var box = window.OwnedWindows.Should().ContainSingle().Subject;
            try { box.Title.Should().Be(""); }
            finally { box.Close(); }
            await run;
        }
        finally
        {
            foreach (var box in window.OwnedWindows) box.Close();
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "")]
    [InlineData(true, "")]
    public async Task Omitted_title_uses_the_hosts_brand_and_explicit_empty_stays_empty(bool input, string? title)
    {
        // #2042 tests construct identities independently rather than changing process-wide Distribution.
        var brand = new DistributionIdentity("acme", "Acme Studio");
        var owner = new Window();
        owner.Show();
        var library = new StandaloneStandardLib(owner, brand.Name);
        Task answer = input ? library.InputBox("probe", title, "")
            : library.MsgBox("probe", title, MessageBoxButtons.Ok, MessageBoxIcon.None);
        var dialog = owner.OwnedWindows.Single();
        try { dialog.Title.Should().Be(title ?? brand.Name); }
        finally
        {
            dialog.Close();
            owner.Close();
        }
        await answer;
    }
}

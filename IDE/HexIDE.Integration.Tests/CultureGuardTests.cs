using System;
using System.Globalization;
using System.Threading;
using Avalonia.Threading;
using Xunit.v3;

namespace HexIDE.Integration.Tests;

// Sets the process-wide culture defaults to check the guard names a test that leaves one (#2500).
[Collection(typeof(ProcessCultureCollection))]
public class CultureGuardTests
{
    private static readonly CultureInfo LeakCulture = CultureInfo.GetCultureInfo(
        CultureInfo.CurrentUICulture.Name == "fr-FR" ? "de-DE" : "fr-FR");

    [AvaloniaFact]
    public void After_names_the_test_that_left_an_explicit_thread_UI_culture()
    {
        var context = ExecutionContext.Capture()!;
        try
        {
            CultureInfo.CurrentUICulture = LeakCulture;
            AssertLeak("CurrentUICulture", "explicit culture leaker");
        }
        finally
        {
            ExecutionContext.Restore(context);
        }
    }

    // Avalonia 12.0.4 ran each dispatcher job by restoring the context it was posted from, and left it on the thread,
    // culture and all. So a job posted under another test's culture changed this one's, which is how the guard came to
    // name tests that never touched a culture (#2029, #2473). From 12.0.5 a job runs under the UI thread's own culture and
    // the thread keeps it (AvaloniaUI/Avalonia#21627). The guard still names a test that leaves a culture behind; this
    // pins that a job is no longer a way to do it.
    [AvaloniaFact]
    public void A_dispatcher_job_neither_runs_under_nor_leaves_the_culture_it_was_posted_under()
    {
        var context = ExecutionContext.Capture()!;
        var own = CultureInfo.CurrentUICulture.Name;
        string? seen = null;
        try
        {
            ExecutionContext.Run(context, _ =>
            {
                CultureInfo.CurrentUICulture = LeakCulture;
                Dispatcher.UIThread.Post(() => seen = CultureInfo.CurrentUICulture.Name, DispatcherPriority.Send);
            }, null);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Send);

            seen.Should().Be(own, "a job runs under the UI thread's culture, not the one it was posted under");
            CultureInfo.CurrentUICulture.Name.Should().Be(own, "and the thread keeps its own culture after it");
        }
        finally
        {
            ExecutionContext.Restore(context);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void After_names_the_test_that_left_a_default_thread_culture(bool ui)
    {
        var culture = CultureInfo.DefaultThreadCurrentCulture;
        var uiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            if (ui)
                CultureInfo.DefaultThreadCurrentUICulture = LeakCulture;
            else
                CultureInfo.DefaultThreadCurrentCulture = LeakCulture;
            AssertLeak(ui ? "DefaultThreadCurrentUICulture" : "DefaultThreadCurrentCulture", "default culture leaker");
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        }
    }

    private static void AssertLeak(string member, string name)
    {
        // Invoke the exit hook before xUnit's execution-context isolation can erase the synthetic leak.
        var test = Substitute.For<IXunitTest>();
        test.TestDisplayName.Returns(name);
        Action after = () => new CultureGuardAttribute().After(
            typeof(CultureGuardTests).GetMethod(nameof(After_names_the_test_that_left_an_explicit_thread_UI_culture))!, test);
        after.Should().Throw<Exception>().Which.Message.Should()
            .Contain(name).And.Contain($"must leave {member} at its host value").And.Contain(LeakCulture.Name);
    }
}

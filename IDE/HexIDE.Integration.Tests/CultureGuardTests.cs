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

    [AvaloniaFact]
    public void After_names_the_test_whose_dispatcher_job_left_its_execution_context()
    {
        var context = ExecutionContext.Capture()!;
        try
        {
            ExecutionContext.Run(context, _ =>
            {
                CultureInfo.CurrentUICulture = LeakCulture;
                Dispatcher.UIThread.Post(() => { }, DispatcherPriority.Send);
            }, null);
            CultureInfo.CurrentUICulture.Name.Should().NotBe(LeakCulture.Name);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Send);
            CultureInfo.CurrentUICulture.Name.Should().Be(LeakCulture.Name);
            AssertLeak("CurrentUICulture", "dispatcher context leaker");
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

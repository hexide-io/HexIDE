using Avalonia.Headless.XUnit;
using HexIDE.Addins;
using HexIDE.Conversations;
using HexIDE.Debugging;

namespace HexIDE.Tests.DebugAdapter;

/// <summary>
/// The launches add-ins contribute for the External Debug Session: kept under the add-in's name, offered for the projects
/// they say they run, taken out by their handle or with a failed initialisation, and started only when asked.
/// </summary>
public class DebugLaunchServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static AddinDebugLaunch Launch(string title, Func<AddinProjectInfo, bool>? claims = null,
        Func<AddinProjectInfo, CancellationToken, Task<AddinLaunchConfiguration>>? create = null) => new(title)
    {
        Claims = claims ?? (_ => true),
        CreateConfigurationAsync = create ?? ((_, _) => throw new InvalidOperationException("not asked for in this test")),
    };

    [Fact]
    public async Task A_launch_is_kept_under_its_add_ins_name_until_its_handle_takes_it_out()
    {
        await using var log = new ConversationLog();
        var service = new DebugLaunchService(new ExternalDebugService(log));
        var changes = 0;
        service.LaunchesChanged += () => ++changes;

        var handle = service.Register(Launch("RDCore"), "RDCore for HexIDE");
        service.Register(Launch("RDCore"), "Another add-in");

        service.Launches.Select(launch => launch.Key).Should().Equal("RDCore for HexIDE/RDCore", "Another add-in/RDCore");
        service.Find("RDCore for HexIDE/RDCore")!.Origin.Should().Be("RDCore for HexIDE");
        handle.Dispose();
        handle.Dispose();
        service.Launches.Should().ContainSingle().Which.Origin.Should().Be("Another add-in");
        service.Find("RDCore for HexIDE/RDCore").Should().BeNull();
        changes.Should().Be(3, "two added and one taken out; a second dispose is nothing");
    }

    [Fact]
    public async Task One_add_in_cannot_contribute_two_launches_of_one_title_and_a_launch_needs_its_parts()
    {
        await using var log = new ConversationLog();
        var service = new DebugLaunchService(new ExternalDebugService(log));
        service.Register(Launch("RDCore"), "RDCore for HexIDE");

        var twice = () => service.Register(Launch("RDCore"), "RDCore for HexIDE");
        twice.Should().Throw<InvalidOperationException>();
        var untitled = () => service.Register(Launch(" "), "RDCore for HexIDE");
        untitled.Should().Throw<ArgumentException>();
        var unmade = () => service.Register(new AddinDebugLaunch("Other") { CreateConfigurationAsync = null! }, "RDCore for HexIDE");
        unmade.Should().Throw<ArgumentException>();
        service.Launches.Should().ContainSingle();
    }

    [Fact]
    public async Task A_launch_contributed_by_an_initialisation_that_fails_is_taken_out_with_it()
    {
        await using var log = new ConversationLog();
        var service = new DebugLaunchService(new ExternalDebugService(log));

        using (new AddinInitializationScope())
            service.Register(Launch("RDCore"), "RDCore for HexIDE");

        service.Launches.Should().BeEmpty("the scope was disposed without being completed, as a failed Initialize leaves it");
    }

    [Fact]
    public async Task A_project_is_offered_the_launches_that_say_they_run_it_and_not_one_whose_answer_throws()
    {
        await using var log = new ConversationLog();
        var service = new DebugLaunchService(new ExternalDebugService(log));
        var project = TestHelpers.CreateProject("Invoices");
        service.Register(Launch("Runs it", claims: info => info.ProjectName == "Invoices"), "A");
        service.Register(Launch("Not this one", claims: _ => false), "B");
        service.Register(Launch("Throws", claims: _ => throw new InvalidOperationException("broken")), "C");

        service.For(project).Select(launch => launch.Title).Should().Equal("Runs it");
    }

    [AvaloniaFact]
    public async Task Starting_makes_the_configuration_for_the_project_then_and_a_failure_to_make_it_ends_the_start()
    {
        await using var log = new ConversationLog();
        var debugger = new ExternalDebugService(log);
        var service = new DebugLaunchService(debugger);
        var project = TestHelpers.CreateProject("Invoices");
        AddinProjectInfo? asked = null;
        service.Register(Launch("RDCore", create: (info, _) =>
        {
            asked = info;
            throw new InvalidOperationException("rdcore.json was not found");
        }), "RDCore for HexIDE");
        var launch = service.Find("RDCore for HexIDE/RDCore")!;
        asked.Should().BeNull("nothing is asked until a run starts");
        var failures = new List<string>();
        debugger.Failed += failures.Add;

        await service.StartAsync(launch, project).WaitAsync(TimeSpan.FromSeconds(20), Token);

        asked!.ProjectName.Should().Be("Invoices");
        debugger.IsActive.Should().BeFalse();
        failures.Should().ContainSingle().Which.Should().Contain("rdcore.json was not found");
    }
}

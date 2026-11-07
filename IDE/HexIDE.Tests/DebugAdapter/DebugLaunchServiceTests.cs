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

        var handle = service.Register(Launch("RDCore"), "com.rdcore.hexide", "RDCore for HexIDE");
        service.Register(Launch("RDCore"), "com.example.another", "Another add-in");

        service.Launches.Select(launch => launch.Key).Should().Equal("com.rdcore.hexide/RDCore", "com.example.another/RDCore");
        service.Find("com.rdcore.hexide/RDCore")!.Origin.Should().Be("RDCore for HexIDE");
        handle.Dispose();
        handle.Dispose();
        service.Launches.Should().ContainSingle().Which.Origin.Should().Be("Another add-in");
        service.Find("com.rdcore.hexide/RDCore").Should().BeNull();
        changes.Should().Be(3, "two added and one taken out; a second dispose is nothing");
    }

    [Fact]
    public async Task One_add_in_cannot_contribute_two_launches_of_one_title_and_a_launch_needs_its_parts()
    {
        await using var log = new ConversationLog();
        var service = new DebugLaunchService(new ExternalDebugService(log));
        service.Register(Launch("RDCore"), "com.rdcore.hexide", "RDCore for HexIDE");

        var twice = () => service.Register(Launch("RDCore"), "com.rdcore.hexide", "RDCore for HexIDE");
        twice.Should().Throw<InvalidOperationException>();
        var untitled = () => service.Register(Launch(" "), "com.rdcore.hexide", "RDCore for HexIDE");
        untitled.Should().Throw<ArgumentException>();
        var unmade = () => service.Register(new AddinDebugLaunch("Other") { CreateConfigurationAsync = null! }, "com.rdcore.hexide", "RDCore for HexIDE");
        unmade.Should().Throw<ArgumentException>();
        service.Launches.Should().ContainSingle();
    }

    [Fact]
    public async Task A_launch_contributed_by_an_initialisation_that_fails_is_taken_out_with_it()
    {
        await using var log = new ConversationLog();
        var service = new DebugLaunchService(new ExternalDebugService(log));

        using (new AddinInitializationScope())
            service.Register(Launch("RDCore"), "com.rdcore.hexide", "RDCore for HexIDE");

        service.Launches.Should().BeEmpty("the scope was disposed without being completed, as a failed Initialize leaves it");
    }

    [Fact]
    public async Task A_project_is_offered_the_launches_that_say_they_run_it_and_not_one_whose_answer_throws()
    {
        await using var log = new ConversationLog();
        var service = new DebugLaunchService(new ExternalDebugService(log));
        var project = TestHelpers.CreateProject("Invoices");
        service.Register(Launch("Runs it", claims: info => info.ProjectName == "Invoices"), "a", "A");
        service.Register(Launch("Not this one", claims: _ => false), "b", "B");
        service.Register(Launch("Throws", claims: _ => throw new InvalidOperationException("broken")), "c", "C");

        service.For(project).Select(launch => launch.Title).Should().Equal("Runs it");
    }

    [AvaloniaFact]
    public async Task Starting_makes_the_configuration_for_the_project_then_and_a_failure_to_make_it_ends_the_start()
    {
        await using var log = new ConversationLog();
        var debugger = new ExternalDebugService(log);
        var service = new DebugLaunchService(debugger);
        var project = TestHelpers.CreateProject("Invoices");
        project.StartsAtSubMain = true;
        AddinProjectInfo? asked = null;
        service.Register(Launch("RDCore", create: (info, _) =>
        {
            asked = info;
            throw new InvalidOperationException("rdcore.json was not found");
        }), "com.rdcore.hexide", "RDCore for HexIDE");
        var launch = service.Find("com.rdcore.hexide/RDCore")!;
        asked.Should().BeNull("nothing is asked until a run starts");
        var failures = new List<string>();
        debugger.Failed += failures.Add;

        await service.StartAsync(launch, project).WaitAsync(TimeSpan.FromSeconds(20), Token);

        asked!.ProjectName.Should().Be("Invoices");
        asked.StartupObject.Should().Be("Sub Main", "what RDCore's launch needs to know what to run");
        debugger.IsActive.Should().BeFalse();
        failures.Should().ContainSingle().Which.Should().Contain("rdcore.json was not found");
    }
}

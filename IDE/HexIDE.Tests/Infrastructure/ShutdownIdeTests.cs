using HexIDE.Runtime.Serialization;
using System.IO;
using System.Text.Json;
using HexIDE.Events;
using HexIDE.Projects;
using Avalonia.Threading;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace HexIDE.Tests.Infrastructure;

public class ShutdownIdeTests
{
    [AvaloniaFact]
    public async Task Main_window_close_is_registered_for_response_completion()
    {
        var previousForce = Static.ForceCloseWithoutPrompt;
        var response = Substitute.For<IHttpResponseFeature>();
        var callbacks = new List<(Func<object, Task> Callback, object State)>();
        response.When(r => r.OnCompleted(Arg.Any<Func<object, Task>>(), Arg.Any<object>()))
            .Do(call => callbacks.Add((call.ArgAt<Func<object, Task>>(0), call.ArgAt<object>(1))));
        var http = new DefaultHttpContext();
        http.Features.Set(response);
        var accessor = new HttpContextAccessor { HttpContext = http };
        using var services = new ServiceCollection().AddSingleton<IHttpContextAccessor>(accessor).BuildServiceProvider();
        using var context = new IdeContext(Substitute.For<IProjectManager>(), Substitute.For<IDocumentDockService>(),
            Substitute.For<ILspClient>(), null!, null!, null!, new EventBus(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        try
        {
            var tools = ActivatorUtilities.CreateInstance<HexIdeTools>(services, context);
            var result = await tools.ShutdownIdeAsync(force: false);
            Dispatcher.UIThread.RunJobs();

            result.Requested.Should().BeTrue();
            callbacks.Should().ContainSingle("returning a tool result does not finish its HTTP response");
            await callbacks[0].Callback(callbacks[0].State);
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            accessor.HttpContext = null;
            Static.ForceCloseWithoutPrompt = previousForce;
        }
    }

    [AvaloniaTheory]
    [InlineData(true, true, "form")]
    [InlineData(true, false, "form")]
    [InlineData(false, true, "form")]
    [InlineData(false, false, "form")]
    [InlineData(true, true, "module")]
    [InlineData(false, true, "module")]
    [InlineData(true, true, "project")]
    [InlineData(false, true, "project")]
    public async Task Reply_reports_discarded_documents_or_the_waiting_prompt(bool force, bool modified, string member)
    {
        var previousForce = Static.ForceCloseWithoutPrompt;
        var project = member == "module" ? TestHelpers.CreateProjectWithModule("Project1")
            : TestHelpers.CreateProjectWithForm("Project1");
        var name = member == "project" ? "Project1" : member == "module" ? "Project1/Module1" : "Project1/Form1";
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var projectService = Substitute.For<IProjectService>();
        var bus = new EventBus();
        var appliedOnUiThread = false;
        using var subscription = bus.Subscribe<ApplyAllUnsavedChangesEvent>(_ =>
        {
            appliedOnUiThread = Dispatcher.UIThread.CheckAccess();
            if (member == "project") projectService.HasUnsavedChanges(project).Returns(modified);
            else if (member == "module") projectService.HasUnsavedChanges(project.Modules[0]).Returns(modified);
            else projectService.HasUnsavedChanges(project.Forms[0]).Returns(modified);
        });
        using var context = new IdeContext(projects, Substitute.For<IDocumentDockService>(),
            Substitute.For<ILspClient>(), null!, Substitute.For<IProjectRunnerService>(), projectService, bus,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var http = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = http };
        try
        {
            var tools = new HexIdeTools(context, accessor);
            var result = await tools.ShutdownIdeAsync(force);
            var json = JsonSerializer.SerializeToElement(result, ReplyJson.Options);
            json.TryGetProperty("discardedDocuments", out var discarded).Should().BeTrue("the reply reports discarded work even when empty");
            discarded.EnumerateArray().Select(x => x.GetString())
                .Should().Equal(force && modified ? [name] : Array.Empty<string>());
            if (!force && modified)
            {
                result.Note.Should().Contain("save-changes prompt").And.Contain("wait")
                    .And.Contain(name).And.Contain("dump_visual_tree {window: \"auto\"}")
                    .And.Contain("interact").And.Contain("Yes, No or Cancel")
                    .And.Contain("Cancel keeps the IDE running").And.NotContain("/health");
            }
            else if (force)
                result.Note.Should().Contain("Discarded").And.Contain("/health");
            else
                result.Note.Should().Be("Shutdown requested. Poll /health until it stops answering to confirm the process exited.");

            appliedOnUiThread.Should().BeTrue();
            var schema = ToolSchemas.For(tools).Single(t => t.ProtocolTool.Name == "shutdown_ide").ProtocolTool.InputSchema;
            if (schema.TryGetProperty("required", out var required))
                required.EnumerateArray().Select(x => x.GetString()).Should().NotContain("force");
        }
        finally
        {
            accessor.HttpContext = null;
            Static.ForceCloseWithoutPrompt = previousForce;
        }
    }

    [Fact]
    public void Server_cleanup_waits_for_exit_so_a_vetoed_shutdown_keeps_serving()
    {
        var source = ToolSource.WithoutComments(File.ReadAllText(Path.Combine(RepoTree.Root(),
            "IDE", "HexIDE.Desktop", "DesktopStartup.cs"))).ReplaceLineEndings("\n");
        source.Should().MatchRegex(@"desktop\.Exit \+= [\s\S]*?ctx\.Dispose\(\);\s*cts\.Cancel\(\);");
        source.Should().NotMatchRegex(@"desktop\.ShutdownRequested \+= [^{]*\{\s*ctx\.Dispose\(\)");
        var server = ToolSource.WithoutComments(File.ReadAllText(Path.Combine(RepoTree.Root(),
            "IDE", "HexIDE.Desktop", "Server", "IdeServer.cs")));
        server.Should().Contain("builder.Services.AddHttpContextAccessor();");
    }
}

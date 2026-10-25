using System.IO;
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
            Substitute.For<ILspClient>(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
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

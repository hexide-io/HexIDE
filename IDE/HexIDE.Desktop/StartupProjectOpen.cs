using System.Threading.Tasks;
using Avalonia.Controls;
using HexIDE.Projects;

namespace HexIDE.Desktop;

internal sealed class StartupProjectOpen
{
    private readonly TaskCompletionSource<ContributedProjects> _discovered = new();
    private readonly TaskCompletionSource _consented = new();

    public async Task Open(IProjectService projects, string path)
    {
        var types = await _discovered.Task;
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".vbp", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".vbg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".twinproj", StringComparison.OrdinalIgnoreCase) && types.Find(path) is null)
            await _consented.Task;

        // The shared route looks up the type again, including anything loaded during consent, and names an unclaimed file.
        await projects.OpenProject(path);
    }

    public void Attach(Window window, ContributedProjects types, Func<Task> consent)
    {
        // First-load consent needs the shown window as its owner, in Release as well as Debug.
        window.Opened += async (_, _) =>
        {
            try { await consent(); }
            catch (Exception ex) { Serilog.Log.Error(ex, "Add-in consent pass failed"); }
            finally { _consented.TrySetResult(); }
        };
        _discovered.SetResult(types);
    }
}

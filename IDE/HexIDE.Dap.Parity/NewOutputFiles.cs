// Tracks only successful exclusive creates, so a failed run can remove its own partial output.
internal sealed class NewOutputFiles : IDisposable
{
    private readonly List<string> paths = [];
    private bool completed;

    public FileStream Create(string path)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        lock (paths)
            paths.Add(path);
        return stream;
    }

    public void Complete() => completed = true;

    public void Dispose()
    {
        if (completed)
            return;
        foreach (var path in paths)
        {
            // Cleanup must not hide the reservation or write failure.
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

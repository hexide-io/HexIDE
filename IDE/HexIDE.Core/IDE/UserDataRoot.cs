namespace HexIDE.IDE;

/// <summary>
/// The per-user directory's value and its one-way redirect, separated from <see cref="UserDataPath"/> so
/// the rule can be tested.
/// </summary>
/// <remarks>
/// <see cref="UserDataPath"/> holds one of these for the whole process, and every test in an assembly
/// shares that process. A test that redirected it would move every other test's files, so the tests
/// construct their own instance instead and the static stays a thin forward.
/// </remarks>
internal sealed class UserDataRoot(Func<string> platformDefault)
{
    private readonly Lock _gate = new();
    private string? _redirected;
    private bool _read;

    public string Directory
    {
        get
        {
            lock (_gate)
            {
                _read = true;
                return _redirected ?? platformDefault();
            }
        }
    }

    public bool IsRedirected
    {
        get { lock (_gate) return _redirected is not null; }
    }

    public void RedirectTo(string directory)
    {
        // Absolute only, and resolved by the caller. A relative path has no meaning here: startup moves the
        // working directory to the executable's folder before this can run, so the only code that knows
        // what a relative path was relative to is the code that read the command line.
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory))
            throw new ArgumentException(
                $"A user data directory must be an absolute path; got '{directory}'. Resolve it against the "
              + "directory HexIDE was started from before redirecting.",
                nameof(directory));

        var normalised = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

        lock (_gate)
        {
            if (_read)
                throw new InvalidOperationException(
                    $"Cannot move per-user files to '{normalised}': something has already read where they are, "
                  + "so part of this session would use the old directory and part the new one. Redirect "
                  + "before anything starts.");

            if (_redirected is not null)
                throw new InvalidOperationException(
                    $"Per-user files were already moved to '{_redirected}' and cannot be moved again to "
                  + $"'{normalised}'.");

            _redirected = normalised;
        }
    }
}

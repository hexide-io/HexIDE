namespace HexIDE.IDE;

/// <summary>
/// Which product this installation is: its stable id, the name people see, and what it adds to HexIDE's own
/// About box. Fixed for the life of the process, and never read from user settings or a profile (#2042).
/// </summary>
/// <remarks>
/// <b>Compatibility identities are not in here on purpose.</b> Assembly names, protocol ids, session headers,
/// environment variable names and project vocabulary stay HexIDE's in every distribution, so existing clients
/// and projects keep working. What changes is what a person reads.
/// </remarks>
public sealed class DistributionIdentity
{
    public const string StockId = "hexide";
    public const string StockName = "HexIDE";
    public const string StockRepositoryUrl = "https://github.com/hexide-io/HexIDE";

    private readonly byte[]? _logo;

    public DistributionIdentity(string id, string name, string? tagline = null, string? copyright = null,
        IReadOnlyList<string>? additionalCredits = null, string? homeUrl = null, string? supportUrl = null,
        string? repositoryUrl = null, byte[]? logo = null)
    {
        Id = id;
        Name = name;
        Tagline = tagline;
        Copyright = copyright;
        AdditionalCredits = additionalCredits?.ToArray() ?? [];
        HomeUrl = homeUrl;
        SupportUrl = supportUrl;
        RepositoryUrl = repositoryUrl;
        _logo = logo;
    }

    /// <summary>The product this tree builds, used wherever no descriptor sits beside the executable.</summary>
    public static DistributionIdentity Stock { get; } = new(StockId, StockName, repositoryUrl: StockRepositoryUrl);

    public string Id { get; }
    public string Name { get; }
    public string? Tagline { get; }
    public string? Copyright { get; }

    /// <summary>
    /// Credits shown <b>after</b> HexIDE's own. There is deliberately no member that removes or replaces those.
    /// Jen (#2746): "a whitelabelled IDE should _not_ get an API to remove HexIDE credits, only to add to them."
    /// </summary>
    public IReadOnlyList<string> AdditionalCredits { get; }

    public string? HomeUrl { get; }
    public string? SupportUrl { get; }
    public string? RepositoryUrl { get; }

    public bool IsStock => Id == StockId;

    /// <summary>
    /// The folder that holds this distribution's per-user state, under each platform's roots. It comes from the
    /// id and not the name, so renaming a product never moves anybody's settings.
    /// </summary>
    public string StateDirectoryName => IsStock ? StockName : Id;

    /// <summary>This distribution's log root. Bundled child processes are told it, so they log beside the host.</summary>
    public string LogsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StateDirectoryName, "logs");

    /// <summary>A new read-only stream over the validated logo, or null when the descriptor names none.</summary>
    public Stream? OpenLogo() => _logo is null ? null : new MemoryStream(_logo, writable: false);
}

/// <summary>
/// The value and its one-way establishment, separated from <see cref="Distribution"/> so the rule can be tested
/// without moving every other test's per-user paths, as <see cref="UserDataRoot"/> is.
/// </summary>
internal sealed class DistributionHolder
{
    private readonly Lock _gate = new();
    private DistributionIdentity _current = DistributionIdentity.Stock;
    private bool _read;

    public DistributionIdentity Current
    {
        get
        {
            lock (_gate)
            {
                _read = true;
                return _current;
            }
        }
    }

    public void Establish(DistributionIdentity identity)
    {
        lock (_gate)
        {
            // Every per-user path is derived from this. An identity that changed after the first read would split
            // one session's state between two products' folders, and nothing would say so.
            if (_read)
                throw new InvalidOperationException(
                    "The distribution was established after it had been read. Establish it first thing at startup.");
            _read = true;
            _current = identity;
        }
    }
}

/// <summary>The installation's identity, for the whole process.</summary>
public static class Distribution
{
    private static readonly DistributionHolder Process = new();

    public static DistributionIdentity Current => Process.Current;

    /// <summary>The name people see. A property of its own so that AXAML can bind it with <c>x:Static</c>.</summary>
    public static string DisplayName => Current.Name;

    /// <summary>
    /// Fixes this process's identity. Called once, from startup, before anything has asked where a per-user file
    /// lives. Refused once the identity has been read.
    /// </summary>
    public static void Establish(DistributionIdentity identity) => Process.Establish(identity);

    /// <summary>This process's log root.</summary>
    public static string LogsDirectory => Current.LogsDirectory;
}

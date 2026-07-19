using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using Serilog;

namespace HexIDE.Addins;

internal sealed class AddinRegistry : IAddinRegistry, IAddinLoader
{
    private readonly IPackageVerifier _verifier;
    private readonly ISettingsService _settings;
    private readonly IDeveloperModeService _devMode;
    private readonly IWindowManager _windowManager;
    private readonly ILocalizationService _localization;
    private readonly ConsentStore _consent;
    private readonly RevocationStore _revocations;
    private readonly List<AddinContext> _contexts = [];
    private readonly string _stateFilePath;
    private readonly HashSet<string> _disabled;
    private IHexIdeHost? _host;
    private bool _disposed;

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public AddinRegistry(IPackageVerifier verifier, ISettingsService settings, IDeveloperModeService devMode,
                         IWindowManager windowManager, ILocalizationService localization)
    {
        _verifier = verifier;
        _settings = settings;
        _devMode = devMode;
        _windowManager = windowManager;
        _localization = localization;
        _consent = new ConsentStore();
        _revocations = new RevocationStore();
        _stateFilePath = DefaultStateFilePath();
        _disabled = LoadDisabledSet();
    }

    // ── IAddinLoader ─────────────────────────────────────────────

    public void LoadAll(IHexIdeHost host)
    {
        _host = host;   // retained for the post-window consent pass (late-loading consented add-ins)

        var dir = Path.Combine(AppContext.BaseDirectory, "addins");
        if (!Directory.Exists(dir))
        {
            Log.Debug("AddinRegistry: addins/ folder not found at {Dir} — skipping", dir);
            return;
        }

        // Refresh the revocation list before loading anything — but ONLY if a URL is configured. The
        // default is the bundled floor + last-known-good cache with no network, so a normal cold start
        // does no fetch and incurs no delay. The fetch is blocking with a short timeout and fail-open.
        if (!string.IsNullOrWhiteSpace(_settings.RevocationListUrl))
            _revocations.RefreshFromUrl(_settings.RevocationListUrl, TimeSpan.FromSeconds(3));

        // Unsigned packages load only when BOTH the session is in developer mode AND the persisted
        // capability is on — neither alone is sufficient (the gate the developer-mode spec defines).
        var allowUnsigned = _devMode.IsEnabled && _settings.LoadUnsignedAddins;
        if (allowUnsigned)
            Log.Warning("AddinRegistry: developer mode + load-unsigned-add-ins is ON — unsigned add-ins will load");

        // Each immediate subdirectory is a package (addins/<id>/ with an addin.json manifest).
        foreach (var packageDir in Directory.GetDirectories(dir))
            LoadPackage(Path.GetFullPath(packageDir), allowUnsigned, host);
    }

    private void LoadPackage(string packageDir, bool allowUnsigned, IHexIdeHost host)
    {
        if (_disabled.Contains(packageDir))
        {
            _contexts.Add(new AddinContext(ShallowInfo(packageDir)) { Status = AddinStatus.Disabled, PackageDirectory = packageDir });
            Log.Debug("AddinRegistry: {Dir} is disabled — skipped", packageDir);
            return;
        }

        var result = _verifier.Verify(packageDir);

        // A directory without a manifest is simply not an add-in package — ignore it silently.
        if (result.Manifest is null && result.Error == "addin.json not found")
            return;

        var info = BuildInfo(packageDir, result);

        if (!result.IsVerified && !allowUnsigned)
        {
            _contexts.Add(new AddinContext(info) { Status = AddinStatus.Untrusted, ErrorMessage = result.Error, PackageDirectory = packageDir });
            Log.Warning("AddinRegistry: {Dir} is untrusted ({Reason}) — skipped (developer mode + load-unsigned-add-ins required)",
                packageDir, result.Error);
            return;
        }

        if (result.Manifest is not { } manifest)
        {
            _contexts.Add(new AddinContext(info) { Status = AddinStatus.Error, ErrorMessage = result.Error ?? "no manifest", PackageDirectory = packageDir });
            Log.Warning("AddinRegistry: {Dir} has no usable manifest ({Reason}) — skipped", packageDir, result.Error);
            return;
        }

        var status = result.IsVerified ? AddinStatus.Loaded : AddinStatus.DeveloperUnsigned;

        // Revocation outranks everything: a revoked package never loads and is never prompted. Build
        // (hash) revocation hits everyone including first-party; publisher/intermediate revocation exempts
        // key-pinned first-party (so a marketplace compromise can't disable the bundled add-ins).
        if (_revocations.IsRevoked(result.ManifestHash, result.PublisherId, result.IntermediateId, result.IsFirstParty) is { } revokeReason)
        {
            _contexts.Add(new AddinContext(info) { Status = AddinStatus.Revoked, ErrorMessage = revokeReason, PackageDirectory = packageDir, Manifest = manifest });
            Log.Warning("AddinRegistry: {Title} REVOKED ({Reason}) — not loaded", info.Title, revokeReason);
            return;
        }

        // Consent gate: first-party packages are pre-consented (installing HexIDE is consent for its
        // own features); everything else needs the user's recorded first-load decision, keyed by the
        // manifest hash. With no decision the add-in is AwaitingConsent and is NOT loaded here — the
        // prompt is deferred to the post-window pass, because LoadAll runs before the window (and thus
        // before any modal can have an owner) exists.
        // First-party is pinned to the first-party publisher KEY (PackageVerification.IsFirstParty),
        // not the publisherId string — a mis-issued id or a compromised intermediate cannot masquerade.
        if (!result.IsFirstParty)
        {
            var decision = _consent.GetDecision(result.ManifestHash ?? "");
            if (decision == ConsentDecisions.Block)
            {
                _contexts.Add(new AddinContext(info) { Status = AddinStatus.ConsentDenied, PackageDirectory = packageDir, Manifest = manifest });
                Log.Information("AddinRegistry: {Title} blocked by a prior consent decision — not loaded", info.Title);
                return;
            }
            if (decision is null)
            {
                _contexts.Add(new AddinContext(info)
                {
                    Status = AddinStatus.AwaitingConsent,
                    PackageDirectory = packageDir,
                    Manifest = manifest,
                    PendingResult = result,
                });
                Log.Information("AddinRegistry: {Title} awaiting first-load consent", info.Title);
                return;
            }
            // decision == Allow → fall through and load.
        }

        var ctx = new AddinContext(info) { PackageDirectory = packageDir, Manifest = manifest };
        _contexts.Add(ctx);
        LoadEntry(ctx, manifest, status, host);
    }

    /// <summary>Loads + initializes the entry assembly into <paramref name="ctx"/> (used both at startup
    /// for consented add-ins and later, after consent is granted).</summary>
    private void LoadEntry(AddinContext ctx, AddinManifest manifest, AddinStatus status, IHexIdeHost host)
    {
        var entryPath = Path.Combine(ctx.PackageDirectory, manifest.Entry);
        try
        {
            var alc = new AddinLoadContext(ctx.PackageDirectory, manifest);
            ctx.LoadContext = alc;
            var assembly = alc.LoadFromAssemblyPath(entryPath);

            var addinType = assembly.GetExportedTypes()
                .FirstOrDefault(t => typeof(IAddin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
            if (addinType is null)
            {
                ctx.Status = AddinStatus.Error;
                ctx.ErrorMessage = "no IAddin implementation found";
                Log.Warning("AddinRegistry: no IAddin implementor in {Dir} — skipped", ctx.PackageDirectory);
                return;
            }

            var addin = (IAddin)Activator.CreateInstance(addinType)!;
            ctx.Instance = addin;
            addin.Initialize(host);
            ctx.Status = status;
            Log.Information("AddinRegistry: {Title} v{Version} loaded from {Dir} [{Trust}]",
                ctx.Info.Title, ctx.Info.Version, ctx.PackageDirectory,
                status == AddinStatus.Loaded ? $"verified: {ctx.Info.Publisher}" : "developer-unsigned");
        }
        catch (Exception ex)
        {
            ctx.Status = AddinStatus.Error;
            ctx.ErrorMessage = ex.Message;
            Log.Warning(ex, "AddinRegistry: failed to load {Dir}", ctx.PackageDirectory);
        }
    }

    /// <summary>
    /// Prompts (modally) for every add-in still <see cref="AddinStatus.AwaitingConsent"/> and, on Allow,
    /// records the decision and late-loads it. Must run AFTER the main window is shown (so the modal has
    /// an owner); the MCP server runs on a background thread so the modal does not block it.
    /// </summary>
    public async Task PromptPendingConsentAsync()
    {
        if (_host is not { } host)
            return;

        // Snapshot: prompting mutates statuses, not the list.
        var pending = _contexts
            .Where(c => c.Status == AddinStatus.AwaitingConsent && c.PendingResult is not null)
            .ToList();

        foreach (var ctx in pending)
        {
            var result = ctx.PendingResult!;
            ctx.PendingResult = null;   // clear up-front so an exception can't leave it pending/re-promptable
            var vm = new AddinConsentDialogViewModel(
                ctx.Info.Title, ctx.Info.Version, ctx.Info.Author,
                result.IsVerified ? result.PublisherDisplayName : null,
                _localization,
                result.Chain, _windowManager);

            bool allowed;
            try
            {
                allowed = await _windowManager.ShowDialog(vm);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AddinRegistry: consent dialog failed for {Title}", ctx.Info.Title);
                continue;
            }

            if (_disposed)
                return;   // the IDE was shut down while the consent dialog was open — do not load anything

            _consent.Record(ctx.Info.Id, result.PublisherId ?? "", result.ManifestHash ?? "",
                allowed ? ConsentDecisions.Allow : ConsentDecisions.Block);

            if (!allowed)
            {
                ctx.Status = AddinStatus.ConsentDenied;
                Log.Information("AddinRegistry: {Title} blocked by the user at the consent prompt", ctx.Info.Title);
                continue;
            }

            // TOCTOU guard: the dialog may have been open for a long time. Re-verify and load only if
            // the package on disk is still EXACTLY what the user consented to (same verdict + same hash).
            // A swapped signed file fails re-verification; a changed manifest changes the hash.
            var fresh = _verifier.Verify(ctx.PackageDirectory);
            if (fresh.IsVerified == result.IsVerified
                && string.Equals(fresh.ManifestHash, result.ManifestHash, StringComparison.Ordinal)
                && fresh.Manifest is { } manifest)
            {
                LoadEntry(ctx, manifest, result.IsVerified ? AddinStatus.Loaded : AddinStatus.DeveloperUnsigned, host);
            }
            else
            {
                ctx.Status = AddinStatus.Error;
                ctx.ErrorMessage = "package changed on disk after the consent prompt";
                Log.Warning("AddinRegistry: {Title} changed on disk after the consent prompt — not loaded", ctx.Info.Title);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var ctx in _contexts)
        {
            if (ctx.Instance is { } instance)
            {
                try { instance.Dispose(); }
                catch (Exception ex) { Log.Warning(ex, "AddinRegistry: error disposing {Title}", ctx.Info.Title); }
            }

            try { ctx.LoadContext?.Unload(); }
            catch (Exception ex) { Log.Warning(ex, "AddinRegistry: error unloading {Title}", ctx.Info.Title); }
        }
        _contexts.Clear();
    }

    // ── IAddinRegistry ───────────────────────────────────────────

    public IReadOnlyList<AddinInfo> GetAll() => [.. _contexts.Select(c => c.Info)];

    public AddinStatus GetStatus(string assemblyPath)
    {
        var norm = Path.GetFullPath(assemblyPath);
        var ctx = _contexts.FirstOrDefault(c =>
            string.Equals(c.Info.AssemblyPath, norm, StringComparison.OrdinalIgnoreCase));
        if (ctx is not null)
            return ctx.Status;
        return _disabled.Contains(norm) ? AddinStatus.Disabled : AddinStatus.Loaded;
    }

    public void SetEnabled(string assemblyPath, bool enabled)
    {
        var norm = Path.GetFullPath(assemblyPath);
        if (enabled)
            _disabled.Remove(norm);
        else
            _disabled.Add(norm);

        SaveDisabledSet();

        var ctx = _contexts.FirstOrDefault(c =>
            string.Equals(c.Info.AssemblyPath, norm, StringComparison.OrdinalIgnoreCase));
        if (ctx is not null)
            ctx.Status = enabled ? AddinStatus.Loaded : AddinStatus.Disabled;
    }

    public void RevokeConsent(string manifestHash)
    {
        if (string.IsNullOrEmpty(manifestHash))
            return;
        _consent.Revoke(manifestHash);
        var ctx = _contexts.FirstOrDefault(c => c.Info.ManifestHash == manifestHash);
        Log.Information("AddinRegistry: consent revoked for {Title} — re-prompts next launch",
            ctx?.Info.Title ?? manifestHash);
    }

    // ── Metadata helpers ─────────────────────────────────────────

    private static AddinInfo BuildInfo(string packageDir, PackageVerificationResult result) =>
        result.Manifest is { } m
            ? new AddinInfo(packageDir, m.Title, m.Description, m.Version, m.Author,
                            result.PublisherDisplayName ?? "", m.Id, result.ManifestHash ?? "",
                            result.LogoPath, result.Chain)
            : ShallowInfo(packageDir);

    private static AddinInfo ShallowInfo(string packageDir) =>
        new(packageDir, Path.GetFileName(packageDir), string.Empty, string.Empty, string.Empty);

    // ── Persistence (enable/disable state) ───────────────────────

    private static string DefaultStateFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "HexIDE");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "addins.json");
    }

    private HashSet<string> LoadDisabledSet()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(_stateFilePath);
            var data = JsonSerializer.Deserialize<AddinStateData>(json);
            return new HashSet<string>(data?.Disabled ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AddinRegistry: Failed to read {Path}, using defaults", _stateFilePath);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveDisabledSet()
    {
        try
        {
            var data = new AddinStateData { Disabled = [.. _disabled] };
            var json = JsonSerializer.Serialize(data, s_jsonOptions);
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AddinRegistry: Failed to save {Path}", _stateFilePath);
        }
    }

    private sealed class AddinStateData
    {
        [JsonPropertyName("disabled")]
        public List<string> Disabled { get; set; } = [];
    }
}

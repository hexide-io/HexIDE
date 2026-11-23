using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using Serilog;

namespace HexIDE.Addins;

/// <summary>
/// Adds an add-in's resource dictionaries to the application's resources, refusing any that would change how the IDE or
/// another add-in looks or reads (#5396). <see cref="IHexIdeHost.RegisterResources"/> is its only way in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every key is read before anything is merged,</b> in the dictionary's merged and theme dictionaries too, and one
/// refused key refuses the whole dictionary. Half a dictionary would break its own <c>BasedOn</c> chains.
/// </para>
/// <para>
/// <b>Keys, not values.</b> A dictionary's values are deferred until they're looked up, and its themes'
/// <c>BasedOn="{StaticResource …}"</c> resolve only once it's merged. Reading its keys builds none of them.
/// </para>
/// <para>
/// <b>Later arrivals.</b> The check is made once, at registration. What the IDE adds later still wins: the language pack's
/// dictionary is merged again after every other on each change of language, and Avalonia looks merged dictionaries up
/// last first. A key the IDE adds later to its own entries wins outright, since a dictionary's own entries come before
/// its merged ones. Reasoned from Avalonia's lookup order, not measured for every later route.
/// </para>
/// </remarks>
internal sealed class AddinResourceService
{
    /// <summary>What a key held before any add-in registered is said to belong to.</summary>
    internal const string IdeOwner = "the IDE";

    // Every key an add-in's dictionary added, with that add-in, so a refusal can name the other owner.
    private readonly Dictionary<object, AddinOrigin> owners = new();

    /// <summary>
    /// Adds <paramref name="dictionary"/> for <paramref name="origin"/>, whose code in <paramref name="caller"/> asked.
    /// </summary>
    /// <exception cref="ArgumentException">Not an <c>avares://</c> address in <paramref name="caller"/>, or not a
    /// resource dictionary.</exception>
    /// <exception cref="InvalidOperationException">A key is refused; the message names each, with its owner.</exception>
    internal IDisposable Register(Uri dictionary, AddinOrigin origin, Assembly caller)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        Dispatcher.UIThread.VerifyAccess();
        var app = Application.Current ?? throw new InvalidOperationException("There is no application to add resources to.");

        var assembly = caller.GetName().Name;
        if (!dictionary.IsAbsoluteUri || dictionary.Scheme != "avares"
            || !string.Equals(dictionary.Host, assembly, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"{origin.Title} can register resources only from its own assembly, as avares://{assembly}/…; it named {dictionary}.",
                nameof(dictionary));

        var include = new ResourceInclude((Uri?)null) { Source = dictionary };
        IResourceDictionary loaded;
        try
        {
            loaded = include.Loaded;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new ArgumentException($"{dictionary} is not a resource dictionary {origin.Title} can register: {ex.Message}",
                nameof(dictionary), ex);
        }

        var keys = new HashSet<object>();
        var refusals = new List<string>();
        Collect(loaded, keys, refusals, new HashSet<IResourceProvider>(ReferenceEqualityComparer.Instance));
        foreach (var key in keys)
        {
            if (Refusal(app, key, caller) is { } reason)
                refusals.Add($"{Describe(key)}: {reason}");
        }

        if (refusals.Count > 0)
        {
            foreach (var refusal in refusals)
                Log.Warning("Resources: refused {Dictionary} from {Addin} ({AddinId}): {Refusal}",
                    dictionary, origin.Title, origin.Id, refusal);
            throw new InvalidOperationException(
                $"{origin.Title} can't register {dictionary}: " + string.Join("; ", refusals) + ".");
        }

        app.Resources.MergedDictionaries.Add(include);
        foreach (var key in keys)
            owners[key] = origin;
        Log.Information("Resources: {Addin} ({AddinId}) added {Dictionary} with {Count} key(s)",
            origin.Title, origin.Id, dictionary, keys.Count);
        return new Registration(this, app, include, keys);

        string? Refusal(Application application, object key, Assembly from)
        {
            switch (key)
            {
                case string text when text.StartsWith("Str.", StringComparison.Ordinal):
                    return "a Str.* key is the IDE's text";
                case string:
                    break;
                case Type type when type.Assembly != from:
                    return $"a type from {type.Assembly.GetName().Name}, which would restyle every control of that type";
                case Type:
                    break;
                default:
                    return $"a key of type {key.GetType().FullName}; only string and type keys can be registered";
            }

            if (owners.TryGetValue(key, out var other))
                return $"already registered by {other.Title} ({other.Id})";
            return Held(application, key) ? $"already held by {IdeOwner}" : null;
        }
    }

    /// <summary>Whether the application finds <paramref name="key"/> in either theme, its themes and styles included.</summary>
    private static bool Held(Application application, object key) =>
        application.TryGetResource(key, ThemeVariant.Light, out _) || application.TryGetResource(key, ThemeVariant.Dark, out _);

    // Reads keys only: a deferred value is built when it's looked up, which nothing here does.
    private static void Collect(IResourceDictionary dictionary, HashSet<object> keys, List<string> refusals,
        HashSet<IResourceProvider> seen)
    {
        if (!seen.Add(dictionary))
            return;
        foreach (var key in dictionary.Keys)
            keys.Add(key);
        foreach (var theme in dictionary.ThemeDictionaries.Values)
            CollectProvider(theme, keys, refusals, seen);
        foreach (var merged in dictionary.MergedDictionaries)
            CollectProvider(merged, keys, refusals, seen);
    }

    private static void CollectProvider(object provider, HashSet<object> keys, List<string> refusals,
        HashSet<IResourceProvider> seen)
    {
        switch (provider)
        {
            case ResourceInclude include:
                Collect(include.Loaded, keys, refusals, seen);
                break;
            case IResourceDictionary nested:
                Collect(nested, keys, refusals, seen);
                break;
            default:
                // Its keys can't be listed, so it can't be checked.
                refusals.Add($"a nested {provider.GetType().FullName}, whose keys can't be read before it's merged");
                break;
        }
    }

    private static string Describe(object key) => key switch
    {
        string text => $"\"{text}\"",
        Type type => $"{{x:Type {type.FullName}}}",
        _ => key.ToString() ?? key.GetType().FullName ?? "a key",
    };

    private sealed class Registration(AddinResourceService service, Application app, ResourceInclude include,
        HashSet<object> keys) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Dispatcher.UIThread.Invoke(() =>
            {
                app.Resources.MergedDictionaries.Remove(include);
                foreach (var key in keys)
                    service.owners.Remove(key);
            });
        }
    }
}

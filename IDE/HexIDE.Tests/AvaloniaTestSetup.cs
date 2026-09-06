using Avalonia;
using Avalonia.Headless;

namespace HexIDE.Tests;

/// <summary>
/// One-time Avalonia headless initialization for tests that depend on Avalonia infrastructure
/// (e.g. AssetLoader for avares:// URIs).
///
/// <para>
/// <b>This does not fix the thread-affinity flake, and cannot.</b> <c>SetupWithoutStarting</c> binds
/// Avalonia's dispatcher to the calling thread, and every later access must come from that thread — but
/// xunit gives <b>each test class its own thread</b> (measured: two classes on 10 and 13, with an async
/// test's continuation on 18). So there is no single thread to bind to that satisfies the assembly, and
/// no amount of choosing a better moment to call this will produce one. See hexide-io/HexIDE#292 for the
/// two fixes that were tried against a deterministic reproduction and failed, and for what is left.
/// </para>
///
/// <para>
/// What is fixed here is smaller and real: the latch is now set <b>before</b> the call it guards.
/// Previously a throw from <c>SetupWithoutStarting</c> — which sets Avalonia's own internal flag partway
/// through — left this latch false, so every later caller re-entered and reported
/// <c>Setup was already called</c>: a secondary symptom, repeated hundreds of times, hiding whatever
/// actually failed first.
/// </para>
///
/// <para>
/// <see cref="BoundThreadId"/> is a diagnostic, not a guarantee. Comparing it with
/// <c>Environment.CurrentManagedThreadId</c> tells you in one line whether the current test is on the
/// bound thread, which is the difference between reading a cause and reading 282 copies of a symptom.
/// </para>
/// </summary>
internal static class AvaloniaTestSetup
{
    private static bool s_initialized;
    private static readonly object s_lock = new();

    /// <summary>The managed thread Avalonia's dispatcher was bound to, or null if setup never ran.</summary>
    internal static int? BoundThreadId { get; private set; }

    internal static void EnsureInitialized()
    {
        if (s_initialized) return;
        lock (s_lock)
        {
            if (s_initialized) return;

            // Recorded BEFORE the call that can throw. Avalonia sets its own internal flag partway
            // through, so a failure here used to leave this latch false, every later caller re-enter, and
            // each report "Setup was already called" — a secondary symptom masking the real first error.
            BoundThreadId = Environment.CurrentManagedThreadId;
            s_initialized = true;

            AppBuilder.Configure<Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
        }
    }
}

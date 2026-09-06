// Run this assembly's tests SERIALLY (no cross-class parallelism).
//
// Why: these tests share ONE global headless Avalonia application (AvaloniaTestSetup.EnsureInitialized builds a
// single Application via SetupWithoutStarting). Avalonia is single-threaded — controls, the Dispatcher, and the
// static CommandManager (which the ViewModels' CanExecute/RequerySuggested paths touch) all have UI-thread affinity
// and shared static state. xunit's default parallelises test COLLECTIONS (one per class) across worker threads, so
// classes touching Avalonia concurrently raced on that shared app — producing intermittent failures in a DIFFERENT
// handful of ViewModel tests each run (all of which pass in isolation and on re-run).
//
// The tests here are not written with the Avalonia.Headless.XUnit [AvaloniaFact] attribute; the plain [Fact] +
// one-time setup pattern requires serial execution to be race-free. Disabling parallelisation is the standard fix
// for a headless-Avalonia xunit-v2 suite. (Cost: a modestly slower run — reliability over speed.)
//
// It is NOT a sufficient fix, and this comment used to say why in a way that was wrong. [AvaloniaFact] does not
// marshal each test onto a "per-test UI thread": Avalonia caches ONE HeadlessUnitTestSession per assembly and
// launches exactly one thread that becomes the UI thread for every test in it. Measured directly — two classes and
// a post-await continuation all reported thread 28. What is per-test is the Application and Dispatcher, under the
// default PerTest isolation; the THREAD is shared either way.
//
// That distinction is the whole point. Disabling parallelisation prevents concurrency but not thread drift, and
// xunit gives each test CLASS its own thread (measured: 10, 13, and 18 for a continuation) — so Avalonia binds to
// whichever class got there first and every other class is on the wrong side of VerifyAccess. A single shared UI
// thread is exactly what removes that. See hexide-io/HexIDE#292, and #297 for why it needs xunit v3 first.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

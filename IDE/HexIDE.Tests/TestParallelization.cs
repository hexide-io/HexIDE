// Run this assembly's tests SERIALLY (no cross-class parallelism).
//
// Why: these tests share ONE global headless Avalonia application (AvaloniaTestSetup.EnsureInitialized builds a
// single Application via SetupWithoutStarting). Avalonia is single-threaded — controls, the Dispatcher, and the
// static CommandManager (which the ViewModels' CanExecute/RequerySuggested paths touch) all have UI-thread affinity
// and shared static state. xunit's default parallelises test COLLECTIONS (one per class) across worker threads, so
// classes touching Avalonia concurrently raced on that shared app — producing intermittent failures in a DIFFERENT
// handful of ViewModel tests each run (all of which pass in isolation and on re-run).
//
// The tests here are not written with the Avalonia.Headless.XUnit [AvaloniaFact] attribute (which would marshal each
// test onto a per-test UI thread); the plain [Fact] + one-time setup pattern requires serial execution to be
// race-free. Disabling parallelisation is the standard, robust fix for a headless-Avalonia xunit-v2 suite. (Cost: a
// modestly slower run — reliability over speed for the test suite.)
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

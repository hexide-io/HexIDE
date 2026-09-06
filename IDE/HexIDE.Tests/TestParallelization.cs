// Run this assembly's tests SERIALLY (no cross-class parallelism).
//
// Why, now that [AvaloniaFact] handles the UI thread: the Avalonia-dependent tests share one Application
// and one Dispatcher per assembly, and much of what they touch is process-global static state — the
// CommandManager the ViewModels' CanExecute paths hit, the localization service's active pack, the
// settings singleton. Running two classes concurrently races that state regardless of which thread
// Avalonia is on, which is what produced intermittent failures in a DIFFERENT handful of ViewModel tests
// each run before this was set.
//
// It is NOT what fixes the 282-failure cascade, and this comment used to imply otherwise. Serial
// execution prevents concurrency; it never prevented thread DRIFT, because xunit gives each test class
// its own thread. That is fixed by [AvaloniaFact] (see TestApp and AvaloniaThreadAffinityTests), not by
// this line. Both are needed, for different reasons.
//
// (Cost: a modestly slower run — reliability over speed for the test suite.)
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

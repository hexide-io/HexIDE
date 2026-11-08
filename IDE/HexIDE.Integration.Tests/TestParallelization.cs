// Run this assembly's test classes one at a time (#2530).
//
// Avalonia.Headless resets the UI dispatcher at the start of every [AvaloniaFact]'s setup, and the first thread to
// create a dispatcher after that becomes the UI thread. AvaloniaObject takes Dispatcher.CurrentDispatcher, so a
// plain [Fact] that builds any control, brush or other Avalonia object on one of xunit's own threads while another
// class's Avalonia test is being set up takes the UI thread. The session's setup then throws, its loop ends, and
// every later Avalonia test waits forever (AvaloniaUI/Avalonia#22021). Linux CI caught CommonDialogFileResultTests
// doing exactly that.
//
// With one class at a time, no other test runs during a setup. Background work a test leaves behind still can,
// which is why LanguageServerLeakGuard stays (#3256).
//
// Cost: none measurable. Full runs on Windows and WSL took as long with this as without it (#3304 has the figures).
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

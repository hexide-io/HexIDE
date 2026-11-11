// Every test here shares one headless Avalonia application and its dispatcher, so they run one at a time, as in
// HexIDE.Integration.Tests.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

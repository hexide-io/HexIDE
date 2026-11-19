using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;

using var source = new EventPipeEventSource(args[0]);
var starts = new Dictionary<int, double>();
source.Clr.GCStart += data => starts[data.Count] = data.TimeStampRelativeMSec;
source.Clr.GCStop += data =>
{
    if (starts.Remove(data.Count, out var start))
        Console.WriteLine(FormattableString.Invariant(
            $"GC,{data.Count},{data.Depth},{start:F3},{data.TimeStampRelativeMSec - start:F3}"));
};
source.Process();

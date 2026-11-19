using Microsoft.Diagnostics.Tracing.Etlx;

var converted = TraceLog.CreateFromEventPipeDataFile(args[0]);
using var log = new TraceLog(converted);
var sites = new Dictionary<string, (int Count, long Bytes)>();
var active = false;
var total = 0;
using var source = log.Events.GetSource();
source.Dynamic.All += data =>
{
    if (data.ProviderName == "HexIDE-RecursionCost")
        active = data.ID == (Microsoft.Diagnostics.Tracing.TraceEventID)1 && Convert.ToInt32(data.PayloadValue(0)) >= 2;
};
source.Clr.GCAllocationTick += data =>
{
    if (!active) return;
    total++;
    var stack = data.CallStack();
    while (stack is not null)
    {
        var method = stack.CodeAddress.FullMethodName;
        if (method.StartsWith("HexIDE.Runtime.Interpreter.", StringComparison.Ordinal))
        {
            var key = method + " | " + data.TypeName;
            var previous = sites.GetValueOrDefault(key);
            sites[key] = (previous.Count + 1, previous.Bytes + data.AllocationAmount64);
            break;
        }
        stack = stack.Caller;
    }
};
source.Process();
Console.WriteLine($"warm allocation samples: {total}; attributed: {sites.Values.Sum(s => s.Count)}");
foreach (var pair in sites.OrderByDescending(p => p.Value.Bytes).Take(30))
    Console.WriteLine($"{pair.Value.Count}\t{pair.Value.Bytes}\t{pair.Key}");

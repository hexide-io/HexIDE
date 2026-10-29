using System.Runtime.CompilerServices;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Tests;

static class Program
{
    public static void Main()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { new Probe().Measure(); }
            catch (Exception ex) { failure = ex; }
        }, 1024 * 1024);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw failure;
    }
}

sealed class Probe : BaseVBTestFixture
{
    public void Measure()
    {
        Run("""
            Dim depth As Long
            On Error Resume Next
            Call Down(0)
            Debug.Print depth
            Debug.Print Err.Number
            Function Down(n)
              depth = n
              Down = Down(n + 1) + 1
            End Function
            """).GetAwaiter().GetResult();
        Console.WriteLine($"Value bytes: {Unsafe.SizeOf<Vb6Value>()}; depth reached: {debug[0].Value}; error: {debug[1].Value}");
    }
}

using HexIDE.Runtime.Tests;

static class Program
{
    public static void Main()
    {
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var low = 0;
            var high = 1024;
            if (!Completes(low) || Completes(high))
                throw new InvalidOperationException("Search endpoints do not bracket the limit.");
            while (high - low > 1)
            {
                var middle = (low + high) / 2;
                if (Completes(middle)) low = middle;
                else high = middle;
            }
            Console.WriteLine($"DEPTH {low} first-failure {high}");
        }
    }

    private static bool Completes(int n)
    {
        bool completed = false;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { completed = new Probe().Completes(n); }
            catch (Exception ex) { failure = ex; }
        }, 4 * 1024 * 1024);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("Probe thread did not finish.");
        if (failure is not null) throw failure;
        return completed;
    }
}

sealed class Probe : BaseVBTestFixture
{
    public bool Completes(int n)
    {
        Run($"""
            On Error Resume Next
            Debug.Print Down({n})
            Debug.Print Err.Number
            Function Down(n)
              If n <= 0 Then
                Down = 0
              Else
                Down = Down(n - 1) + 1
              End If
            End Function
            """).GetAwaiter().GetResult();
        var error = Convert.ToInt32(debug[^1].Value);
        if (error == 28) return false;
        if (error != 0 || debug.Count != 2 || Convert.ToInt32(debug[0].Value) != n)
            throw new InvalidOperationException($"Unexpected result at {n}: error {error}.");
        return true;
    }
}

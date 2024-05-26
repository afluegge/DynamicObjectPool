using System.Diagnostics;

namespace Haisl.Utils;

public class StopwatchWrapper : IStopwatch
{
    private readonly Stopwatch _stopwatch;


    public StopwatchWrapper() : this(new Stopwatch())
    {
    }

    public StopwatchWrapper(Stopwatch stopwatch)
    {
        _stopwatch = stopwatch;
    }


    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public long ElapsedTicks => _stopwatch.ElapsedTicks;

    public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;

    public bool IsRunning => _stopwatch.IsRunning;


    public void Start() => _stopwatch.Start();

    public void Stop() => _stopwatch.Stop();

    public void Reset() => _stopwatch.Reset();

    public void Restart() => _stopwatch.Restart();
}

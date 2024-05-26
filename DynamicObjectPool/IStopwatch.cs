using JetBrains.Annotations;

namespace Haisl.Utils;

[PublicAPI]
public interface IStopwatch
{
    TimeSpan Elapsed { get; }

    long ElapsedTicks { get; }

    long ElapsedMilliseconds { get; }

    bool IsRunning { get; }


    void Start();

    void Stop();

    void Reset();

    void Restart();
}

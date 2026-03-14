namespace DefaultMonitorSwitcher.Core;

public interface IActivityTracker : IDisposable
{
    /// <summary>Raised on the background poll thread each time a tick completes.</summary>
    event EventHandler<ActivitySample>? SampleProduced;

    /// <summary>
    /// Temporarily reduces the poll interval to ElevatedPollIntervalSeconds for
    /// ElevatedPollDurationSeconds. Calling again while already elevated resets the expiry.
    /// </summary>
    void ActivateElevatedPolling();

    /// <summary>True while the elevated poll interval is in effect.</summary>
    bool IsElevatedPollingActive { get; }

    void Start();
    void Stop();
}

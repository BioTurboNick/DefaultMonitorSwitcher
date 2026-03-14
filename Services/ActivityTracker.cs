using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.Services;

public sealed class ActivityTracker : IActivityTracker
{
    public event EventHandler<ActivitySample>? SampleProduced;
    public void ActivateElevatedPolling() { }
    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
}

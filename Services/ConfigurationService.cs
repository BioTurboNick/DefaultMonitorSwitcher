using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.Services;

public sealed class ConfigurationService : IConfigurationService
{
    public AppConfiguration Current { get; private set; } = new();
    public event EventHandler<AppConfiguration>? ConfigurationChanged;
    public ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public void Save(AppConfiguration configuration) { }
}

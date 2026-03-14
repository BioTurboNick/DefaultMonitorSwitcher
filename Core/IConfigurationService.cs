namespace DefaultMonitorSwitcher.Core;

public interface IConfigurationService
{
    AppConfiguration Current { get; }
    event EventHandler<AppConfiguration>? ConfigurationChanged;
    ValueTask InitializeAsync(CancellationToken ct = default);
    void Save(AppConfiguration configuration);
}

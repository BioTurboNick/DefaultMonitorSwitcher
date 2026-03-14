using System.IO;
using System.Text.Json;
using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.Services;

public sealed class ConfigurationService : IConfigurationService
{
    private static readonly string ConfigFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DefaultMonitorSwitcher",
        "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AppConfiguration Current { get; private set; } = new();

    public event EventHandler<AppConfiguration>? ConfigurationChanged;

    public async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ConfigFilePath))
        {
            Current = new AppConfiguration();
            return;
        }

        try
        {
            await using var stream = File.OpenRead(ConfigFilePath);
            var loaded = await JsonSerializer.DeserializeAsync<AppConfiguration>(stream, JsonOptions, ct);
            Current = loaded ?? new AppConfiguration();
        }
        catch (Exception)
        {
            // Corrupt or unreadable config — fall back to defaults
            Current = new AppConfiguration();
        }
    }

    public void Save(AppConfiguration configuration)
    {
        Current = configuration;

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);

        ConfigurationChanged?.Invoke(this, configuration);
    }
}

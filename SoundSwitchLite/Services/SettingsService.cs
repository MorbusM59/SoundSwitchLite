using System.IO;
using System.Text.Json;
using SoundSwitchLite.Models;

namespace SoundSwitchLite.Services;

public class AppSettings
{
    /// <summary>Output (playback) device slots.</summary>
    public List<DeviceMapping> DeviceMappings { get; set; } = new();
    /// <summary>Input (capture) device slots.</summary>
    public List<DeviceMapping> InputDeviceMappings { get; set; } = new();
    /// <summary>Device IDs that have been sent to the "unused" pool for output devices.</summary>
    public List<string> UnusedOutputDeviceIds { get; set; } = new();
    /// <summary>Device IDs that have been sent to the "unused" pool for input devices.</summary>
    public List<string> UnusedInputDeviceIds { get; set; } = new();
    /// <summary>Master volume percentage (0–100). Defaults to 100.</summary>
    public int MasterVolume { get; set; } = 100;
    /// <summary>Input master volume percentage (0–100). Defaults to 100.</summary>
    public int InputMasterVolume { get; set; } = 100;
}

public class SettingsService
{
    private readonly string _settingsPath;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundSwitchLite", "settings.json"))
    {
    }

    // Constructor used for testing to specify an alternate settings file path
    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // If settings are corrupt, start fresh
        }
        return new AppSettings();
    }

    public bool Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(dir);

            // Create a timestamped backup of existing settings to allow recovery if overwritten
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var bakName = Path.Combine(dir, "settings.json." + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + ".bak");
                    File.Copy(_settingsPath, bakName, overwrite: false);
                }
            }
            catch { /* best-effort backup; ignore failures */ }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsPath, json);

            // Append a concise save log entry for diagnostics
            try
            {
                var log = Path.Combine(dir, "save.log");
                var entry = DateTime.UtcNow.ToString("o") + " Saved settings: DeviceMappings=" + (settings.DeviceMappings?.Count ?? 0) + ", InputDeviceMappings=" + (settings.InputDeviceMappings?.Count ?? 0) + "\n";
                File.AppendAllText(log, entry);
            }
            catch { }

            return true;
        }
        catch (Exception ex)
        {
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundSwitchLite");
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "error.log"), DateTime.UtcNow.ToString("o") + " " + ex + "\n");
            }
            catch { }
            return false;
        }
    }
}

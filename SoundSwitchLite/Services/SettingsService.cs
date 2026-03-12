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
}

public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SoundSwitchLite",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // If settings are corrupt, start fresh
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignore save failures silently
        }
    }
}

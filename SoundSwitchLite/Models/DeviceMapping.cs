namespace SoundSwitchLite.Models;

public class DeviceMapping
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    /// <summary>Win32 modifier key flags: Alt=1, Ctrl=2, Shift=4, Win=8</summary>
    public int ModifierKeys { get; set; }
    /// <summary>Virtual-key code (VK_*), e.g. 49 = '1', 50 = '2'</summary>
    public int Key { get; set; }
    /// <summary>
    /// Base volume percentage (0–100). Effective volume = MasterVolume * BaseVolume / 100.
    /// Defaults to 100.
    /// </summary>
    public int BaseVolume { get; set; } = 100;
    // Legacy fields kept for JSON round-trip compatibility; migrated to BaseVolume on load.
    public int? DefaultVolume { get; set; }
    public bool EnforceVolume { get; set; }
}

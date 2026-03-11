namespace SoundSwitchLite.Models;

public class DeviceMapping
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    /// <summary>
    /// Win32 modifier key flags: Alt=1, Ctrl=2, Shift=4, Win=8
    /// </summary>
    public int ModifierKeys { get; set; }
    /// <summary>
    /// Virtual-key code (VK_*), e.g. 49 = '1', 50 = '2'
    /// </summary>
    public int Key { get; set; }
    /// <summary>
    /// Default volume percentage (0–100). Null means no saved value.
    /// </summary>
    public int? DefaultVolume { get; set; }
    /// <summary>
    /// When true, the volume is set to DefaultVolume whenever this device is activated.
    /// </summary>
    public bool EnforceVolume { get; set; }
}

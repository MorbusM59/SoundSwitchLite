using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;

namespace SoundSwitchLite.Services;

public class AudioDevice
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

public class AudioDeviceService : IDisposable
{
    private readonly CoreAudioController _controller;

    public AudioDeviceService()
    {
        _controller = new CoreAudioController();
    }

    public async Task<IEnumerable<AudioDevice>> GetPlaybackDevicesAsync()
    {
        var devices = await _controller.GetPlaybackDevicesAsync(DeviceState.Active);
        return devices.Select(d => new AudioDevice { Id = d.Id.ToString(), Name = d.FullName });
    }

    public async Task<string?> GetDefaultDeviceIdAsync()
    {
        var device = await _controller.GetDefaultDeviceAsync(DeviceType.Playback, Role.Multimedia);
        return device?.Id.ToString();
    }

    public async Task<bool> SetDefaultDeviceAsync(string deviceId)
    {
        try
        {
            var devices = await _controller.GetPlaybackDevicesAsync(DeviceState.Active);
            var target = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (target == null) return false;
            return await target.SetAsDefaultAsync();
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetDeviceNameAsync(string deviceId)
    {
        try
        {
            var devices = await _controller.GetPlaybackDevicesAsync(DeviceState.Active);
            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            return device?.FullName;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        (_controller as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }
}

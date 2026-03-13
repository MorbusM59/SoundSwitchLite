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
    private readonly CoreAudioController? _controller;
    private IEnumerable<dynamic>? _playbackCache;
    private DateTime _playbackCacheAt = DateTime.MinValue;
    private IEnumerable<dynamic>? _captureCache;
    private DateTime _captureCacheAt = DateTime.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(1);

    public AudioDeviceService()
    {
        try
        {
            _controller = new CoreAudioController();
        }
        catch
        {
            _controller = null;
        }
    }

    public async Task<IEnumerable<AudioDevice>> GetPlaybackDevicesAsync()
    {
        var devices = await GetPlaybackDeviceObjectsAsync();
        return devices.Select(d => new AudioDevice { Id = d.Id.ToString(), Name = d.FullName });
    }

    public async Task<string?> GetDefaultDeviceIdAsync()
    {
        if (_controller == null) return null;
        var device = await _controller.GetDefaultDeviceAsync(DeviceType.Playback, Role.Multimedia);
        return device?.Id.ToString();
    }

    public async Task<bool> SetDefaultDeviceAsync(string deviceId)
    {
        try
        {
            var devices = await GetPlaybackDeviceObjectsAsync();
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
            var devices = await GetPlaybackDeviceObjectsAsync();
            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            return device?.FullName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the current volume (0–100) for the given device, or null on failure.</summary>
    public async Task<int?> GetVolumeAsync(string deviceId)
    {
        try
        {
            var devices = await GetPlaybackDeviceObjectsAsync();
            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (device == null) return null;
            return (int)Math.Round(device.Volume);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sets the volume (0–100) for the given device. Returns true on success.</summary>
    public async Task<bool> SetVolumeAsync(string deviceId, int volume)
    {
        try
        {
            var devices = await GetPlaybackDeviceObjectsAsync();
            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (device == null) return false;
            await device.SetVolumeAsync(Math.Clamp(volume, 0, 100));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns all active capture (microphone/input) devices.</summary>
    public async Task<IEnumerable<AudioDevice>> GetCaptureDevicesAsync()
    {
        var devices = await GetCaptureDeviceObjectsAsync();
        return devices.Select(d => new AudioDevice { Id = d.Id.ToString(), Name = d.FullName });
    }

    /// <summary>Returns the ID of the current default capture device, or null.</summary>
    public async Task<string?> GetDefaultCaptureDeviceIdAsync()
    {
        if (_controller == null) return null;
        var device = await _controller.GetDefaultDeviceAsync(DeviceType.Capture, Role.Multimedia);
        return device?.Id.ToString();
    }

    /// <summary>Sets the specified capture device as the default. Returns true on success.</summary>
    public async Task<bool> SetDefaultCaptureDeviceAsync(string deviceId)
    {
        try
        {
            var devices = await GetCaptureDeviceObjectsAsync();
            var target = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (target == null) return false;
            return await target.SetAsDefaultAsync();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns the current volume (0–100) for the given capture device, or null on failure.</summary>
    public async Task<int?> GetCaptureVolumeAsync(string deviceId)
    {
        try
        {
            var devices = await GetCaptureDeviceObjectsAsync();
            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (device == null) return null;
            return (int)Math.Round(device.Volume);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sets the volume (0–100) for the given capture device. Returns true on success.</summary>
    public async Task<bool> SetCaptureVolumeAsync(string deviceId, int volume)
    {
        try
        {
            if (_controller == null) return false;
            var devices = await _controller.GetCaptureDevicesAsync(DeviceState.Active);
            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (device == null) return false;
            await device.SetVolumeAsync(Math.Clamp(volume, 0, 100));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns all active capture (microphone/input) devices.</summary>
    public async Task<IEnumerable<AudioDevice>> GetCaptureDevicesAsync()
    {
        var devices = await _controller.GetCaptureDevicesAsync(DeviceState.Active);
        return devices.Select(d => new AudioDevice { Id = d.Id.ToString(), Name = d.FullName });
    }

    /// <summary>Returns the ID of the current default capture device, or null.</summary>
    public async Task<string?> GetDefaultCaptureDeviceIdAsync()
    {
        var device = await _controller.GetDefaultDeviceAsync(DeviceType.Capture, Role.Multimedia);
        return device?.Id.ToString();
    }

    /// <summary>Sets the specified capture device as the default. Returns true on success.</summary>
    public async Task<bool> SetDefaultCaptureDeviceAsync(string deviceId)
    {
        try
        {
            var devices = await _controller.GetCaptureDevicesAsync(DeviceState.Active);
            var target = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (target == null) return false;
            return await target.SetAsDefaultAsync();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns the current volume (0–100) for the given capture device, or null on failure.</summary>
    public async Task<int?> GetCaptureVolumeAsync(string deviceId)
    {
        try
        {
            var devices = await _controller.GetCaptureDevicesAsync(DeviceState.Active);
            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (device == null) return null;
            return (int)Math.Round(device.Volume);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sets the volume (0–100) for the given capture device. Returns true on success.</summary>
    public async Task<bool> SetCaptureVolumeAsync(string deviceId, int volume)
    {
        try
        {
            var devices = await _controller.GetCaptureDevicesAsync(DeviceState.Active);
            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (device == null) return false;
            await device.SetVolumeAsync(Math.Clamp(volume, 0, 100));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        (_controller as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IEnumerable<dynamic>> GetPlaybackDeviceObjectsAsync()
    {
        if (_playbackCache != null && DateTime.UtcNow - _playbackCacheAt < _cacheDuration)
            return _playbackCache;
        if (_controller == null) return Enumerable.Empty<dynamic>();
        var devices = await _controller.GetPlaybackDevicesAsync(DeviceState.Active);
        _playbackCache = devices.ToList();
        _playbackCacheAt = DateTime.UtcNow;
        return _playbackCache;
    }

    private async Task<IEnumerable<dynamic>> GetCaptureDeviceObjectsAsync()
    {
        if (_captureCache != null && DateTime.UtcNow - _captureCacheAt < _cacheDuration)
            return _captureCache;
        if (_controller == null) return Enumerable.Empty<dynamic>();
        var devices = await _controller.GetCaptureDevicesAsync(DeviceState.Active);
        _captureCache = devices.ToList();
        _captureCacheAt = DateTime.UtcNow;
        return _captureCache;
    }
}

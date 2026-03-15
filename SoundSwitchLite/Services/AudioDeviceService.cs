using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Security.Principal;
using System.Threading;

namespace SoundSwitchLite.Services;

public class AudioDevice
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

public class AudioDeviceService : IDisposable
{
    private readonly CoreAudioController? _controller;
    private static readonly SemaphoreSlim _pnpEnableLock = new(1, 1);
    private IEnumerable<dynamic>? _playbackCache;
    private DateTime _playbackCacheAt = DateTime.MinValue;
    private IEnumerable<dynamic>? _captureCache;
    private DateTime _captureCacheAt = DateTime.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(1);

    public AudioDeviceService()
    {
        try { _controller = new CoreAudioController(); }
        catch { _controller = null; }
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
        catch { return false; }
    }

    public async Task<string?> GetDeviceNameAsync(string deviceId)
    {
        try
        {
            var devices = await GetPlaybackDeviceObjectsAsync();
            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            return device?.FullName;
        }
        catch { return null; }
    }

    public async Task<int?> GetVolumeAsync(string deviceId)
    {
        try
        {
            var devices = await GetPlaybackDeviceObjectsAsync();
            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (device == null) return null;
            return (int)Math.Round(device.Volume);
        }
        catch { return null; }
    }

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
        catch { return false; }
    }

    // Capture (input) devices
    public async Task<IEnumerable<AudioDevice>> GetCaptureDevicesAsync()
    {
        var devices = await GetCaptureDeviceObjectsAsync();
        return devices.Select(d => new AudioDevice { Id = d.Id.ToString(), Name = d.FullName });
    }

    public async Task<IEnumerable<AudioDevice>> GetDisabledPlaybackDevicesAsync()
    {
        var devices = await GetPlaybackDeviceObjectsByStateAsync(DeviceState.Disabled);
        return devices.Select(d => new AudioDevice { Id = d.Id.ToString(), Name = d.FullName });
    }

    public async Task<IEnumerable<AudioDevice>> GetDisabledCaptureDevicesAsync()
    {
        var devices = await GetCaptureDeviceObjectsByStateAsync(DeviceState.Disabled);
        return devices.Select(d => new AudioDevice { Id = d.Id.ToString(), Name = d.FullName });
    }

    public async Task<string?> GetDefaultCaptureDeviceIdAsync()
    {
        if (_controller == null) return null;
        var device = await _controller.GetDefaultDeviceAsync(DeviceType.Capture, Role.Multimedia);
        return device?.Id.ToString();
    }

    public async Task<bool> SetDefaultCaptureDeviceAsync(string deviceId)
    {
        try
        {
            var devices = await GetCaptureDeviceObjectsAsync();
            var target = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (target == null) return false;
            return await target.SetAsDefaultAsync();
        }
        catch { return false; }
    }

    public async Task<int?> GetCaptureVolumeAsync(string deviceId)
    {
        try
        {
            var devices = await GetCaptureDeviceObjectsAsync();
            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (device == null) return null;
            return (int)Math.Round(device.Volume);
        }
        catch { return null; }
    }

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
        catch { return false; }
    }

    public async Task<bool> DisableDeviceAsync(string deviceId, bool isCapture)
    {
        try
        {
            if (_controller == null) return false;
            AppendDiag($"DisableDeviceAsync start: id={deviceId}, isCapture={isCapture}");

            IEnumerable<dynamic> devices = isCapture
                ? await _controller.GetCaptureDevicesAsync(DeviceState.Active)
                : await _controller.GetPlaybackDevicesAsync(DeviceState.Active);

            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (device == null)
            {
                AppendDiag($"DisableDeviceAsync device-not-found: id={deviceId}");
                return false;
            }

            var disabled = await TryDisableViaReflectionAsync(device);
            if (disabled)
            {
                if (isCapture)
                    _captureCache = null;
                else
                    _playbackCache = null;
            }

            AppendDiag($"DisableDeviceAsync result: id={deviceId}, success={disabled}");

            return disabled;
        }
        catch (Exception ex)
        {
            AppendDiag($"DisableDeviceAsync exception: id={deviceId}, msg={ex}");
            return false;
        }
    }

    public async Task<bool> EnableDeviceAsync(string deviceId, bool isCapture)
    {
        try
        {
            if (_controller == null) return false;
            AppendDiag($"EnableDeviceAsync start: id={deviceId}, isCapture={isCapture}");

            IEnumerable<dynamic> devices = isCapture
                ? await _controller.GetCaptureDevicesAsync(DeviceState.Disabled)
                : await _controller.GetPlaybackDevicesAsync(DeviceState.Disabled);

            var device = devices.FirstOrDefault(d => d.Id.ToString() == deviceId);
            if (device == null)
            {
                AppendDiag($"EnableDeviceAsync device-not-found-in-disabled: id={deviceId}");
                return false;
            }

            var enabled = await TryEnableViaReflectionAsync(device);
            if (!enabled)
            {
                // Fallback for runtime variants that expose no enable/state APIs.
                string fullName = string.Empty;
                try { fullName = device.FullName?.ToString() ?? string.Empty; } catch { }
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    enabled = await TryEnableViaPnpDeviceAsync(fullName);
                    AppendDiag($"EnableDeviceAsync PnP fallback result: id={deviceId}, name={fullName}, success={enabled}");
                }
            }

            if (enabled)
            {
                if (isCapture)
                    _captureCache = null;
                else
                    _playbackCache = null;
            }

            AppendDiag($"EnableDeviceAsync result: id={deviceId}, success={enabled}");

            return enabled;
        }
        catch (Exception ex)
        {
            AppendDiag($"EnableDeviceAsync exception: id={deviceId}, msg={ex}");
            return false;
        }
    }

    /// <summary>Reads the current volume of a playback device without using the cache.</summary>
    public async Task<int?> GetVolumeFreshAsync(string deviceId)
    {
        try
        {
            _playbackCache = null; // bust cache
            return await GetVolumeAsync(deviceId);
        }
        catch { return null; }
    }

    /// <summary>Reads the current volume of a capture device without using the cache.</summary>
    public async Task<int?> GetCaptureVolumeFreshAsync(string deviceId)
    {
        try
        {
            _captureCache = null; // bust cache
            return await GetCaptureVolumeAsync(deviceId);
        }
        catch { return null; }
    }

    /// <summary>
    /// Subscribe to system-level volume changes for any device.
    /// Callback receives (deviceId, newVolume 0-100). Returns a disposable that cancels the subscription.
    /// </summary>
    public IDisposable? SubscribeVolumeChanged(Action<string, double> onVolumeChanged)
    {
        if (_controller == null) return null;
        try
        {
            var observable = ((AudioSwitcher.AudioApi.IAudioController)_controller).AudioDeviceChanged;
            return observable.Subscribe(new DelegateObserver<AudioSwitcher.AudioApi.DeviceChangedArgs>(e =>
            {
                if (e is AudioSwitcher.AudioApi.DeviceVolumeChangedArgs va)
                    onVolumeChanged(va.Device.Id.ToString(), va.Volume);
            }));
        }
        catch { return null; }
    }

    /// <summary>
    /// Subscribe to device change notifications (add/remove/default change/state change).
    /// Callback receives the raw DeviceChangedArgs from AudioSwitcher.
    /// </summary>
    public IDisposable? SubscribeDeviceChanged(Action<AudioSwitcher.AudioApi.DeviceChangedArgs> onDeviceChanged)
    {
        if (_controller == null) return null;
        try
        {
            var observable = ((AudioSwitcher.AudioApi.IAudioController)_controller).AudioDeviceChanged;
            return observable.Subscribe(new DelegateObserver<AudioSwitcher.AudioApi.DeviceChangedArgs>(e =>
            {
                try { onDeviceChanged(e); } catch { }
            }));
        }
        catch { return null; }
    }

    private sealed class DelegateObserver<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;
        public DelegateObserver(Action<T> onNext) => _onNext = onNext;
        public void OnNext(T value) { try { _onNext(value); } catch { } }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
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

    private async Task<IEnumerable<dynamic>> GetPlaybackDeviceObjectsByStateAsync(DeviceState state)
    {
        if (_controller == null) return Enumerable.Empty<dynamic>();
        var devices = await _controller.GetPlaybackDevicesAsync(state);
        return devices.ToList();
    }

    private async Task<IEnumerable<dynamic>> GetCaptureDeviceObjectsByStateAsync(DeviceState state)
    {
        if (_controller == null) return Enumerable.Empty<dynamic>();
        var devices = await _controller.GetCaptureDevicesAsync(state);
        return devices.ToList();
    }

    private static async Task<bool> TryDisableViaReflectionAsync(dynamic device)
    {
        var type = (object)device;
        var runtimeType = type.GetType();

        // Cover different AudioSwitcher versions by probing known API names.
        if (await TryInvokeOptionalMethodAsync(type, runtimeType, "DisableAsync"))
            return true;

        if (await TryInvokeOptionalMethodAsync(type, runtimeType, "Disable"))
            return true;

        if (await TryInvokeOptionalMethodAsync(type, runtimeType, "ChangeStateAsync", "Disabled"))
            return true;

        if (await TryInvokeOptionalMethodAsync(type, runtimeType, "ChangeState", "Disabled"))
            return true;

        return await TrySetDeviceStateViaReflectionAsync(type, runtimeType, "Disabled");
    }

    private static async Task<bool> TryEnableViaReflectionAsync(dynamic device)
    {
        var type = (object)device;
        var runtimeType = type.GetType();

        if (await TryInvokeOptionalMethodAsync(type, runtimeType, "EnableAsync"))
            return true;

        if (await TryInvokeOptionalMethodAsync(type, runtimeType, "Enable"))
            return true;

        if (await TryInvokeOptionalMethodAsync(type, runtimeType, "ChangeStateAsync", "Active"))
            return true;

        if (await TryInvokeOptionalMethodAsync(type, runtimeType, "ChangeState", "Active"))
            return true;

        return await TrySetDeviceStateViaReflectionAsync(type, runtimeType, "Active");
    }

    private static async Task<bool> TryInvokeOptionalMethodAsync(object instance, Type runtimeType, string methodName, string? desiredStateName = null)
    {
        var methods = runtimeType.GetMethods().Where(m => m.Name == methodName).ToList();
        AppendDiag($"TryInvokeOptionalMethodAsync probing: type={runtimeType.FullName}, method={methodName}, overloads={methods.Count}");
        foreach (var method in methods)
        {
            var args = TryBuildBestEffortArguments(method, desiredStateName);
            if (args == null)
                continue;

            try
            {
                var result = method.Invoke(instance, args);
                if (result is Task task)
                    await task;

                AppendDiag($"TryInvokeOptionalMethodAsync success: type={runtimeType.FullName}, method={methodName}, sig={method}");
                return true;
            }
            catch (Exception ex)
            {
                AppendDiag($"TryInvokeOptionalMethodAsync failed-overload: type={runtimeType.FullName}, method={methodName}, sig={method}, msg={ex.Message}");
            }
        }

        return false;
    }

    private static async Task<bool> TrySetDeviceStateViaReflectionAsync(object instance, Type runtimeType, string desiredStateName)
    {
        bool anyStateMethod = false;
        foreach (var methodName in new[] { "SetStateAsync", "SetState" })
        {
            var candidates = runtimeType
                .GetMethods()
                .Where(m => m.Name == methodName && m.GetParameters().Length >= 1)
                .ToList();

            if (candidates.Count > 0)
                anyStateMethod = true;

            AppendDiag($"TrySetDeviceStateViaReflectionAsync probing: type={runtimeType.FullName}, method={methodName}, overloads={candidates.Count}, state={desiredStateName}");

            foreach (var method in candidates)
            {
                var parameters = method.GetParameters();
                var parameterType = parameters[0].ParameterType;
                object? stateArgument = null;

                if (parameterType.IsEnum)
                {
                    var names = Enum.GetNames(parameterType);
                    var exact = names.FirstOrDefault(n => string.Equals(n, desiredStateName, StringComparison.OrdinalIgnoreCase));
                    if (exact != null)
                        stateArgument = Enum.Parse(parameterType, exact, ignoreCase: true);
                }
                else if (parameterType == typeof(int))
                {
                    stateArgument = string.Equals(desiredStateName, "Active", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                }

                if (stateArgument == null)
                    continue;

                var args = new object?[parameters.Length];
                args[0] = stateArgument;

                bool canInvoke = true;
                for (int i = 1; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    if (p.IsOptional)
                    {
                        args[i] = p.DefaultValue == DBNull.Value
                            ? GetTypeDefault(p.ParameterType)
                            : p.DefaultValue;
                    }
                    else
                    {
                        canInvoke = false;
                        break;
                    }
                }

                if (!canInvoke)
                    continue;

                try
                {
                    var result = method.Invoke(instance, args);
                    if (result is Task task)
                        await task;

                    AppendDiag($"TrySetDeviceStateViaReflectionAsync success: type={runtimeType.FullName}, method={methodName}, state={desiredStateName}, sig={method}");
                    return true;
                }
                catch (Exception ex)
                {
                    AppendDiag($"TrySetDeviceStateViaReflectionAsync failed-overload: type={runtimeType.FullName}, method={methodName}, state={desiredStateName}, sig={method}, msg={ex.Message}");
                }
            }
        }

        // Some API variants may expose writable State instead of helper methods.
        var stateProp = runtimeType.GetProperty("State");
        if (stateProp != null && stateProp.CanWrite)
        {
            try
            {
                var stateValue = ConvertStateValue(stateProp.PropertyType, desiredStateName);
                if (stateValue != null)
                {
                    stateProp.SetValue(instance, stateValue);
                    AppendDiag($"TrySetDeviceStateViaReflectionAsync success via State property: type={runtimeType.FullName}, state={desiredStateName}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppendDiag($"TrySetDeviceStateViaReflectionAsync failed State property: type={runtimeType.FullName}, state={desiredStateName}, msg={ex.Message}");
            }
        }

        if (!anyStateMethod)
            AppendDiag($"TrySetDeviceStateViaReflectionAsync no state methods found: type={runtimeType.FullName}, state={desiredStateName}");

        return false;
    }

    private static object?[]? TryBuildBestEffortArguments(System.Reflection.MethodInfo method, string? desiredStateName)
    {
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (i == 0 && desiredStateName != null)
            {
                var stateValue = ConvertStateValue(p.ParameterType, desiredStateName);
                if (stateValue != null)
                {
                    args[i] = stateValue;
                    continue;
                }
            }

            if (p.IsOptional)
            {
                args[i] = p.DefaultValue == DBNull.Value
                    ? GetTypeDefault(p.ParameterType)
                    : p.DefaultValue;
            }
            else
            {
                // Best-effort defaults for required parameters (e.g., CancellationToken, bool flags).
                args[i] = GetTypeDefault(p.ParameterType);
            }
        }

        return args;
    }

    private static object? ConvertStateValue(Type parameterType, string desiredStateName)
    {
        if (parameterType.IsEnum)
        {
            var exact = Enum.GetNames(parameterType)
                .FirstOrDefault(n => string.Equals(n, desiredStateName, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return Enum.Parse(parameterType, exact, ignoreCase: true);
            return null;
        }

        if (parameterType == typeof(int))
            return string.Equals(desiredStateName, "Active", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

        return null;
    }

    private static object? GetTypeDefault(Type type)
    {
        if (!type.IsValueType) return null;
        return Activator.CreateInstance(type);
    }

    private static async Task<bool> TryEnableViaPnpDeviceAsync(string friendlyName)
    {
        await _pnpEnableLock.WaitAsync();
        try
        {
            var escaped = friendlyName.Replace("'", "''");
            var script = "$name = '" + escaped + "'; " +
                         "$d = Get-PnpDevice -Class AudioEndpoint | Where-Object { $_.FriendlyName -eq $name -and $_.Status -ne 'OK' } | Select-Object -First 1; " +
                         "if ($d) { Enable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false -ErrorAction Stop; exit 0 } else { exit 1 }";

            var isElevated = IsProcessElevated();
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"" + script + "\"",
                UseShellExecute = !isElevated,
                CreateNoWindow = isElevated
            };

            if (isElevated)
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
            }
            else
            {
                psi.Verb = "runas";
            }

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                AppendDiag($"TryEnableViaPnpDeviceAsync timeout: name={friendlyName}");
                return false;
            }

            string stdout = string.Empty;
            string stderr = string.Empty;
            if (isElevated)
            {
                stdout = await process.StandardOutput.ReadToEndAsync();
                stderr = await process.StandardError.ReadToEndAsync();
            }

            AppendDiag($"TryEnableViaPnpDeviceAsync exit={process.ExitCode}, name={friendlyName}, elevated={isElevated}, out={stdout.Trim()}, err={stderr.Trim()}");
            return process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            AppendDiag($"TryEnableViaPnpDeviceAsync elevation-cancelled: name={friendlyName}");
            return false;
        }
        catch (Exception ex)
        {
            AppendDiag($"TryEnableViaPnpDeviceAsync exception: name={friendlyName}, msg={ex}");
            return false;
        }
        finally
        {
            _pnpEnableLock.Release();
        }
    }

    private static void AppendDiag(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundSwitchLite");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "device-debug.log");
            File.AppendAllText(file, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}

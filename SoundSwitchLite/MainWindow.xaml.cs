using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SoundSwitchLite.Models;
using SoundSwitchLite.Services;

namespace SoundSwitchLite;

// ViewModel for a single device slot
public class DeviceSlotViewModel : INotifyPropertyChanged
{
    private bool _isActive;
    private AudioDevice? _selectedDevice;
    private bool _isListening;
    private string _hotkeyDisplay = "Click to assign hotkey";
    private int _modifiers;
    private int _key;
    private int _defaultVolume = 50;
    private bool _enforceVolume;
    public int HotkeyId { get; set; } = -1;

    public ObservableCollection<AudioDevice> AvailableDevices { get; } = new();

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public AudioDevice? SelectedDevice
    {
        get => _selectedDevice;
        set { _selectedDevice = value; OnPropertyChanged(); }
    }

    public bool IsListening
    {
        get => _isListening;
        set { _isListening = value; OnPropertyChanged(); }
    }

    public string HotkeyDisplay
    {
        get => _hotkeyDisplay;
        set { _hotkeyDisplay = value; OnPropertyChanged(); }
    }

    public int ModifierKeys
    {
        get => _modifiers;
        set { _modifiers = value; OnPropertyChanged(); }
    }

    public int Key
    {
        get => _key;
        set { _key = value; OnPropertyChanged(); }
    }

    /// <summary>Volume percentage (0–100) to apply when this device is activated.</summary>
    public int DefaultVolume
    {
        get => _defaultVolume;
        set { _defaultVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
    }

    /// <summary>When true, DefaultVolume is applied whenever this device is activated.</summary>
    public bool EnforceVolume
    {
        get => _enforceVolume;
        set { _enforceVolume = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Main window view model
public class MainWindowViewModel : INotifyPropertyChanged
{
    private bool _canAddDevice = true;

    public ObservableCollection<DeviceSlotViewModel> DeviceSlots { get; } = new();

    public bool CanAddDevice
    {
        get => _canAddDevice;
        set { _canAddDevice = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private readonly AudioDeviceService _audioService;
    private readonly HotkeyService _hotkeyService;
    private readonly SettingsService _settingsService;
    private List<AudioDevice> _allDevices = new();
    private DeviceSlotViewModel? _listeningSlot;

    // Modifier key VK codes for building the display string
    private static readonly Dictionary<int, string> ModifierNames = new()
    {
        { 1, "Alt" },
        { 2, "Ctrl" },
        { 4, "Shift" },
        { 8, "Win" }
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _audioService = App.AudioDeviceService;
        _hotkeyService = App.HotkeyService;
        _settingsService = App.SettingsService;

        Loaded += OnLoaded;
        Closing += OnWindowClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize hotkey service with this window's handle
        _hotkeyService.Initialize(this);

        // Load all available audio devices
        _allDevices = (await _audioService.GetPlaybackDevicesAsync()).ToList();

        // Restore saved settings
        var settings = _settingsService.Load();
        if (settings.DeviceMappings.Count > 0)
        {
            foreach (var mapping in settings.DeviceMappings)
            {
                var slot = CreateSlot();
                PopulateSlotDevices(slot);

                var device = _allDevices.FirstOrDefault(d => d.Id == mapping.DeviceId)
                          ?? _allDevices.FirstOrDefault(d => d.Name == mapping.DeviceName);

                if (device != null)
                    slot.SelectedDevice = device;

                if (mapping.ModifierKeys != 0 && mapping.Key != 0)
                {
                    slot.ModifierKeys = mapping.ModifierKeys;
                    slot.Key = mapping.Key;
                    slot.HotkeyDisplay = BuildHotkeyDisplayString(mapping.ModifierKeys, mapping.Key);
                    RegisterSlotHotkey(slot);
                }

                // Restore volume settings; seed from current device volume if not yet saved
                if (mapping.DefaultVolume.HasValue)
                {
                    slot.DefaultVolume = mapping.DefaultVolume.Value;
                }
                else if (device != null)
                {
                    var currentVol = await _audioService.GetVolumeAsync(device.Id);
                    slot.DefaultVolume = currentVol ?? 50;
                }
                slot.EnforceVolume = mapping.EnforceVolume;

                _viewModel.DeviceSlots.Add(slot);
            }
        }
        else
        {
            // Add a default empty slot
            var slot = CreateSlot();
            PopulateSlotDevices(slot);
            _viewModel.DeviceSlots.Add(slot);
        }

        await RefreshActiveDevice();
        UpdateAddButtonVisibility();
    }

    private DeviceSlotViewModel CreateSlot() => new DeviceSlotViewModel();

    private void PopulateSlotDevices(DeviceSlotViewModel slot)
    {
        slot.AvailableDevices.Clear();
        foreach (var d in _allDevices)
            slot.AvailableDevices.Add(d);
    }

    private async Task RefreshActiveDevice()
    {
        var defaultId = await _audioService.GetDefaultDeviceIdAsync();
        foreach (var slot in _viewModel.DeviceSlots)
            slot.IsActive = slot.SelectedDevice?.Id == defaultId;
    }

    private void UpdateAddButtonVisibility()
    {
        var usedDeviceIds = _viewModel.DeviceSlots
            .Where(s => s.SelectedDevice != null)
            .Select(s => s.SelectedDevice!.Id)
            .ToHashSet();
        bool hasUnassignedDevices = _allDevices.Any(d => !usedDeviceIds.Contains(d.Id));
        bool slotsUnderDeviceCount = usedDeviceIds.Count < _allDevices.Count;
        _viewModel.CanAddDevice = hasUnassignedDevices && slotsUnderDeviceCount;
        AddDeviceButton.Visibility = _viewModel.CanAddDevice ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot && slot.SelectedDevice != null)
        {
            await ActivateSlotDeviceAsync(slot);
        }
    }

    private async Task ActivateSlotDeviceAsync(DeviceSlotViewModel slot)
    {
        if (slot.SelectedDevice == null) return;
        await _audioService.SetDefaultDeviceAsync(slot.SelectedDevice.Id);
        if (slot.EnforceVolume)
            await _audioService.SetVolumeAsync(slot.SelectedDevice.Id, slot.DefaultVolume);
        await RefreshActiveDevice();
        ShowBalloon($"Switched to: {slot.SelectedDevice.Name}");
    }

    private async void DeviceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Seed the default volume from the device's current volume when a new device is chosen
        if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot
            && slot.SelectedDevice != null && e.AddedItems.Count > 0)
        {
            var currentVol = await _audioService.GetVolumeAsync(slot.SelectedDevice.Id);
            if (currentVol.HasValue)
                slot.DefaultVolume = currentVol.Value;
        }
        SaveSettings();
        UpdateAddButtonVisibility();
    }

    private void VolumeEnforce_Changed(object sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private void VolumeSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        SaveSettings();
    }

    private void HotkeyField_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot)
        {
            // Disarm any previously listening slot
            if (_listeningSlot != null && _listeningSlot != slot)
            {
                _listeningSlot.IsListening = false;
                _listeningSlot.HotkeyDisplay = _listeningSlot.Key != 0
                    ? BuildHotkeyDisplayString(_listeningSlot.ModifierKeys, _listeningSlot.Key)
                    : "Click to assign hotkey";
            }

            slot.IsListening = !slot.IsListening;
            _listeningSlot = slot.IsListening ? slot : null;

            if (slot.IsListening)
                slot.HotkeyDisplay = "Press keys...";
            else
                slot.HotkeyDisplay = slot.Key != 0
                    ? BuildHotkeyDisplayString(slot.ModifierKeys, slot.Key)
                    : "Click to assign hotkey";

            e.Handled = true;
        }
    }

    private void HotkeyField_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot)
        {
            ClearHotkey(slot);
            e.Handled = true;
        }
    }

    private void ClearHotkey(DeviceSlotViewModel slot)
    {
        if (slot.HotkeyId >= 0)
        {
            _hotkeyService.UnregisterHotkey(slot.HotkeyId);
            slot.HotkeyId = -1;
        }
        slot.ModifierKeys = 0;
        slot.Key = 0;
        slot.IsListening = false;
        slot.HotkeyDisplay = "Click to assign hotkey";
        if (_listeningSlot == slot) _listeningSlot = null;
        SaveSettings();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_listeningSlot == null) return;

        // Let modifier-only presses pass through without capturing
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            // Show partial combo
            int currentMods = GetCurrentModifiers();
            if (currentMods != 0)
                _listeningSlot.HotkeyDisplay = BuildModifierString(currentMods) + "+...";
            return;
        }

        e.Handled = true;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_listeningSlot == null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore standalone modifier releases
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            // If all modifiers were released without a non-modifier key, reset display
            if (GetCurrentModifiers() == 0 && _listeningSlot.HotkeyDisplay == "Press keys...")
                _listeningSlot.HotkeyDisplay = "Press keys...";
            return;
        }

        // Capture the hotkey combination
        int mods = GetCurrentModifiers();
        int vk = KeyInterop.VirtualKeyFromKey(key);

        var slot = _listeningSlot;
        slot.IsListening = false;
        _listeningSlot = null;

        // Unregister old hotkey if any
        if (slot.HotkeyId >= 0)
        {
            _hotkeyService.UnregisterHotkey(slot.HotkeyId);
            slot.HotkeyId = -1;
        }

        slot.ModifierKeys = mods;
        slot.Key = vk;
        slot.HotkeyDisplay = BuildHotkeyDisplayString(mods, vk);

        RegisterSlotHotkey(slot);
        SaveSettings();

        e.Handled = true;
    }

    private void RegisterSlotHotkey(DeviceSlotViewModel slot)
    {
        if (slot.Key == 0) return;

        var capturedSlot = slot;
        int id = _hotkeyService.RegisterHotkey(slot.ModifierKeys, slot.Key, async () =>
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                await ActivateSlotDeviceAsync(capturedSlot);
            });
        });
        slot.HotkeyId = id;
    }

    private void RemoveSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot)
        {
            if (slot.HotkeyId >= 0)
                _hotkeyService.UnregisterHotkey(slot.HotkeyId);
            if (_listeningSlot == slot)
                _listeningSlot = null;
            _viewModel.DeviceSlots.Remove(slot);
            SaveSettings();
            UpdateAddButtonVisibility();
        }
    }

    private void AddDevice_Click(object sender, RoutedEventArgs e)
    {
        var slot = CreateSlot();
        PopulateSlotDevices(slot);

        // Pre-select first unassigned device
        var usedIds = _viewModel.DeviceSlots
            .Where(s => s.SelectedDevice != null)
            .Select(s => s.SelectedDevice!.Id)
            .ToHashSet();
        var firstFree = _allDevices.FirstOrDefault(d => !usedIds.Contains(d.Id));
        if (firstFree != null)
            slot.SelectedDevice = firstFree;

        _viewModel.DeviceSlots.Add(slot);
        SaveSettings();
        UpdateAddButtonVisibility();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        // Intercept close and hide to tray instead
        e.Cancel = true;
        Hide();
    }

    private void SaveSettings()
    {
        var settings = new Services.AppSettings
        {
            DeviceMappings = _viewModel.DeviceSlots
                .Select(s => new DeviceMapping
                {
                    DeviceId = s.SelectedDevice?.Id ?? string.Empty,
                    DeviceName = s.SelectedDevice?.Name ?? string.Empty,
                    ModifierKeys = s.ModifierKeys,
                    Key = s.Key,
                    DefaultVolume = s.DefaultVolume,
                    EnforceVolume = s.EnforceVolume
                })
                .ToList()
        };
        _settingsService.Save(settings);
    }

    private void ShowBalloon(string message)
    {
        try
        {
            var trayIcon = (Hardcodet.Wpf.TaskbarNotification.TaskbarIcon)Application.Current.FindResource("TrayIcon");
            trayIcon.ShowBalloonTip("SoundSwitch Lite", message, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }
        catch { /* ignore */ }
    }

    private static int GetCurrentModifiers()
    {
        int mods = 0;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) mods |= 2;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) mods |= 1;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods |= 4;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= 8;
        return mods;
    }

    private static string BuildModifierString(int mods)
    {
        var parts = new List<string>();
        if ((mods & 2) != 0) parts.Add("Ctrl");
        if ((mods & 4) != 0) parts.Add("Shift");
        if ((mods & 1) != 0) parts.Add("Alt");
        if ((mods & 8) != 0) parts.Add("Win");
        return string.Join("+", parts);
    }

    private static string BuildHotkeyDisplayString(int mods, int vk)
    {
        var parts = new List<string>();
        if ((mods & 2) != 0) parts.Add("Ctrl");
        if ((mods & 4) != 0) parts.Add("Shift");
        if ((mods & 1) != 0) parts.Add("Alt");
        if ((mods & 8) != 0) parts.Add("Win");

        var key = KeyInterop.KeyFromVirtualKey(vk);
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}

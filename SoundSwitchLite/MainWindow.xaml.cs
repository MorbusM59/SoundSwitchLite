using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SoundSwitchLite.Models;
using SoundSwitchLite.Services;

namespace SoundSwitchLite;

// ViewModel for a single device slot (output or input)
public class DeviceSlotViewModel : INotifyPropertyChanged
{
    private bool _isActive;
    private AudioDevice? _selectedDevice;
    private bool _isListening;
    private string _hotkeyDisplay = "Click to assign hotkey";
    private int _modifiers;
    private int _key;
    private int _baseVolume = 100;
    private List<AudioDevice> _availableDevices = new();

    public int HotkeyId { get; set; } = -1;
    /// <summary>True when this slot represents a capture (input) device.</summary>
    public bool IsInput { get; init; }

    public List<AudioDevice> AvailableDevices
    {
        get => _availableDevices;
        set { _availableDevices = value; OnPropertyChanged(); }
    }

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

    /// <summary>
    /// Base volume (0–100). Effective volume when switching = MasterVolume * BaseVolume / 100.
    /// Defaults to 100.
    /// </summary>
    public int BaseVolume
    {
        get => _baseVolume;
        set { _baseVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
    }

    public string ActivateButtonTooltip =>
        IsInput ? "Set as default input device" : "Set as default output device";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Main window view model
public class MainWindowViewModel : INotifyPropertyChanged
{
    private int _masterVolume = 100;
    private string _selectedTab = "Output";
    private bool _canAddOutputDevice;
    private bool _canAddInputDevice;

    public ObservableCollection<DeviceSlotViewModel> OutputSlots { get; } = new();
    public ObservableCollection<DeviceSlotViewModel> InputSlots { get; } = new();
    public ObservableCollection<AudioDevice> UnusedOutputDevices { get; } = new();
    public ObservableCollection<AudioDevice> UnusedInputDevices { get; } = new();

    public int MasterVolume
    {
        get => _masterVolume;
        set { _masterVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
    }

    public string SelectedTab
    {
        get => _selectedTab;
        set
        {
            _selectedTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOutputTabSelected));
            OnPropertyChanged(nameof(IsInputTabSelected));
            OnPropertyChanged(nameof(IsUnusedTabSelected));
        }
    }

    public bool IsOutputTabSelected => _selectedTab == "Output";
    public bool IsInputTabSelected => _selectedTab == "Input";
    public bool IsUnusedTabSelected => _selectedTab == "Unused";

    public bool CanAddOutputDevice
    {
        get => _canAddOutputDevice;
        set { _canAddOutputDevice = value; OnPropertyChanged(); }
    }

    public bool CanAddInputDevice
    {
        get => _canAddInputDevice;
        set { _canAddInputDevice = value; OnPropertyChanged(); }
    }

    public int UnusedOutputCount => UnusedOutputDevices.Count;
    public int UnusedInputCount => UnusedInputDevices.Count;
    public bool HasNoUnusedDevices => UnusedOutputDevices.Count == 0 && UnusedInputDevices.Count == 0;

    public void NotifyUnusedChanged()
    {
        OnPropertyChanged(nameof(UnusedOutputCount));
        OnPropertyChanged(nameof(UnusedInputCount));
        OnPropertyChanged(nameof(HasNoUnusedDevices));
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
    private List<AudioDevice> _allOutputDevices = new();
    private List<AudioDevice> _allInputDevices = new();
    private DeviceSlotViewModel? _listeningSlot;
    private bool _isRefreshingDevices;

    private static readonly Dictionary<int, string> ModifierNames = new()
    {
        { 1, "Alt" }, { 2, "Ctrl" }, { 4, "Shift" }, { 8, "Win" }
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
        _hotkeyService.Initialize(this);

        _allOutputDevices = (await _audioService.GetPlaybackDevicesAsync()).ToList();
        _allInputDevices = (await _audioService.GetCaptureDevicesAsync()).ToList();

        var settings = _settingsService.Load();
        _viewModel.MasterVolume = settings.MasterVolume;

        // Restore unused device pools
        foreach (var id in settings.UnusedOutputDeviceIds)
        {
            var d = _allOutputDevices.FirstOrDefault(x => x.Id == id);
            if (d != null) _viewModel.UnusedOutputDevices.Add(d);
        }
        foreach (var id in settings.UnusedInputDeviceIds)
        {
            var d = _allInputDevices.FirstOrDefault(x => x.Id == id);
            if (d != null) _viewModel.UnusedInputDevices.Add(d);
        }
        _viewModel.NotifyUnusedChanged();

        // Restore output slots
        if (settings.DeviceMappings.Count > 0)
        {
            foreach (var mapping in settings.DeviceMappings)
            {
                var slot = CreateSlot(isInput: false);
                _viewModel.OutputSlots.Add(slot);
                RestoreMappingToSlot(slot, mapping, _allOutputDevices);
            }
        }
        else
        {
            _viewModel.OutputSlots.Add(CreateSlot(isInput: false));
        }

        // Restore input slots
        foreach (var mapping in settings.InputDeviceMappings)
        {
            var slot = CreateSlot(isInput: true);
            _viewModel.InputSlots.Add(slot);
            RestoreMappingToSlot(slot, mapping, _allInputDevices);
        }

        // Refresh all dropdowns now that all slots are loaded
        RefreshSlotDevices(_viewModel.OutputSlots, _allOutputDevices, _viewModel.UnusedOutputDevices);
        RefreshSlotDevices(_viewModel.InputSlots, _allInputDevices, _viewModel.UnusedInputDevices);

        await RefreshActiveDevice();
        UpdateOutputAddButtonVisibility();
        UpdateInputAddButtonVisibility();
    }

    private void RestoreMappingToSlot(DeviceSlotViewModel slot, DeviceMapping mapping, List<AudioDevice> allDevices)
    {
        var device = allDevices.FirstOrDefault(d => d.Id == mapping.DeviceId)
                  ?? allDevices.FirstOrDefault(d => d.Name == mapping.DeviceName);
        if (device != null)
            slot.SelectedDevice = device;

        if (mapping.ModifierKeys != 0 && mapping.Key != 0)
        {
            slot.ModifierKeys = mapping.ModifierKeys;
            slot.Key = mapping.Key;
            slot.HotkeyDisplay = BuildHotkeyDisplayString(mapping.ModifierKeys, mapping.Key);
            RegisterSlotHotkey(slot);
        }

        // Migrate legacy DefaultVolume → BaseVolume
        if (mapping.BaseVolume != 100)
            slot.BaseVolume = mapping.BaseVolume;
        else if (mapping.DefaultVolume.HasValue)
            slot.BaseVolume = mapping.DefaultVolume.Value;
        else
            slot.BaseVolume = 100;
    }

    private static DeviceSlotViewModel CreateSlot(bool isInput) => new() { IsInput = isInput };

    private void RefreshSlotDevices(
        ObservableCollection<DeviceSlotViewModel> slots,
        List<AudioDevice> allDevices,
        ObservableCollection<AudioDevice> unusedDevices)
    {
        _isRefreshingDevices = true;
        try
        {
            var unusedIds = unusedDevices.Select(d => d.Id).ToHashSet();
            foreach (var slot in slots)
            {
                var usedByOthers = slots
                    .Where(s => s != slot && s.SelectedDevice != null)
                    .Select(s => s.SelectedDevice!.Id)
                    .ToHashSet();

                slot.AvailableDevices = allDevices
                    .Where(d => !unusedIds.Contains(d.Id) &&
                                 (!usedByOthers.Contains(d.Id) || slot.SelectedDevice?.Id == d.Id))
                    .ToList();

                // Clear selection if the selected device is no longer available
                if (slot.SelectedDevice != null && !slot.AvailableDevices.Contains(slot.SelectedDevice))
                    slot.SelectedDevice = null;
            }
        }
        finally
        {
            _isRefreshingDevices = false;
        }
    }

    private async Task RefreshActiveDevice()
    {
        var defaultOutputId = await _audioService.GetDefaultDeviceIdAsync();
        foreach (var slot in _viewModel.OutputSlots)
            slot.IsActive = slot.SelectedDevice?.Id == defaultOutputId;

        var defaultInputId = await _audioService.GetDefaultCaptureDeviceIdAsync();
        foreach (var slot in _viewModel.InputSlots)
            slot.IsActive = slot.SelectedDevice?.Id == defaultInputId;
    }

    private void UpdateOutputAddButtonVisibility()
    {
        var usedIds = _viewModel.OutputSlots
            .Where(s => s.SelectedDevice != null).Select(s => s.SelectedDevice!.Id).ToHashSet();
        var unusedIds = _viewModel.UnusedOutputDevices.Select(d => d.Id).ToHashSet();
        _viewModel.CanAddOutputDevice =
            _allOutputDevices.Any(d => !usedIds.Contains(d.Id) && !unusedIds.Contains(d.Id));
    }

    private void UpdateInputAddButtonVisibility()
    {
        var usedIds = _viewModel.InputSlots
            .Where(s => s.SelectedDevice != null).Select(s => s.SelectedDevice!.Id).ToHashSet();
        var unusedIds = _viewModel.UnusedInputDevices.Select(d => d.Id).ToHashSet();
        _viewModel.CanAddInputDevice =
            _allInputDevices.Any(d => !usedIds.Contains(d.Id) && !unusedIds.Contains(d.Id));
    }

    // ── Play button ──────────────────────────────────────────────────────────

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot && slot.SelectedDevice != null)
            await ActivateSlotDeviceAsync(slot);
    }

    private async Task ActivateSlotDeviceAsync(DeviceSlotViewModel slot)
    {
        if (slot.SelectedDevice == null) return;

        int effectiveVolume = (int)Math.Round(_viewModel.MasterVolume / 100.0 * slot.BaseVolume);

        if (slot.IsInput)
        {
            await _audioService.SetDefaultCaptureDeviceAsync(slot.SelectedDevice.Id);
            await _audioService.SetCaptureVolumeAsync(slot.SelectedDevice.Id, effectiveVolume);
        }
        else
        {
            await _audioService.SetDefaultDeviceAsync(slot.SelectedDevice.Id);
            await _audioService.SetVolumeAsync(slot.SelectedDevice.Id, effectiveVolume);
        }

        await RefreshActiveDevice();
        ShowBalloon($"Switched to: {slot.SelectedDevice.Name}");
    }

    // ── Device combo box ─────────────────────────────────────────────────────

    private void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingDevices) return;
        if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot)
        {
            if (slot.IsInput)
            {
                RefreshSlotDevices(_viewModel.InputSlots, _allInputDevices, _viewModel.UnusedInputDevices);
                UpdateInputAddButtonVisibility();
            }
            else
            {
                RefreshSlotDevices(_viewModel.OutputSlots, _allOutputDevices, _viewModel.UnusedOutputDevices);
                UpdateOutputAddButtonVisibility();
            }
        }
        SaveSettings();
    }

    // ── Base volume controls ─────────────────────────────────────────────────

    private void VolumeDecrement_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot)
        {
            slot.BaseVolume--;
            SaveSettings();
        }
    }

    private void VolumeIncrement_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot)
        {
            slot.BaseVolume++;
            SaveSettings();
        }
    }

    private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void NumberOnly_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string));
            if (!text.All(char.IsDigit)) e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    private void BaseVolumeTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is DeviceSlotViewModel slot)
        {
            if (int.TryParse(tb.Text, out int val))
                slot.BaseVolume = val; // clamped by setter
            else
                tb.Text = slot.BaseVolume.ToString();
            SaveSettings();
        }
    }

    // ── Master volume ─────────────────────────────────────────────────────────

    private void MasterVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SaveSettings();
    }

    // ── Hotkey handling ───────────────────────────────────────────────────────

    private void HotkeyField_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot)
        {
            if (_listeningSlot != null && _listeningSlot != slot)
            {
                _listeningSlot.IsListening = false;
                _listeningSlot.HotkeyDisplay = _listeningSlot.Key != 0
                    ? BuildHotkeyDisplayString(_listeningSlot.ModifierKeys, _listeningSlot.Key)
                    : "Click to assign hotkey";
            }

            slot.IsListening = !slot.IsListening;
            _listeningSlot = slot.IsListening ? slot : null;

            slot.HotkeyDisplay = slot.IsListening
                ? "Press keys..."
                : (slot.Key != 0 ? BuildHotkeyDisplayString(slot.ModifierKeys, slot.Key) : "Click to assign hotkey");

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
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
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
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            if (GetCurrentModifiers() == 0 && _listeningSlot.HotkeyDisplay == "Press keys...")
                _listeningSlot.HotkeyDisplay = "Press keys...";
            return;
        }

        int mods = GetCurrentModifiers();
        int vk = KeyInterop.VirtualKeyFromKey(key);

        var slot = _listeningSlot;
        slot.IsListening = false;
        _listeningSlot = null;

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
            await Dispatcher.InvokeAsync(async () => await ActivateSlotDeviceAsync(capturedSlot));
        });
        slot.HotkeyId = id;
    }

    // ── Add / Remove slots ───────────────────────────────────────────────────

    private void RemoveSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot)
        {
            if (slot.HotkeyId >= 0) _hotkeyService.UnregisterHotkey(slot.HotkeyId);
            if (_listeningSlot == slot) _listeningSlot = null;

            if (slot.IsInput)
            {
                _viewModel.InputSlots.Remove(slot);
                RefreshSlotDevices(_viewModel.InputSlots, _allInputDevices, _viewModel.UnusedInputDevices);
                UpdateInputAddButtonVisibility();
            }
            else
            {
                _viewModel.OutputSlots.Remove(slot);
                RefreshSlotDevices(_viewModel.OutputSlots, _allOutputDevices, _viewModel.UnusedOutputDevices);
                UpdateOutputAddButtonVisibility();
            }
            SaveSettings();
        }
    }

    private void AddOutputDevice_Click(object sender, RoutedEventArgs e)
    {
        var slot = CreateSlot(isInput: false);
        _viewModel.OutputSlots.Add(slot);
        RefreshSlotDevices(_viewModel.OutputSlots, _allOutputDevices, _viewModel.UnusedOutputDevices);

        // Pre-select the first available device
        var firstFree = slot.AvailableDevices.FirstOrDefault();
        if (firstFree != null) slot.SelectedDevice = firstFree;

        UpdateOutputAddButtonVisibility();
        SaveSettings();
    }

    private void AddInputDevice_Click(object sender, RoutedEventArgs e)
    {
        var slot = CreateSlot(isInput: true);
        _viewModel.InputSlots.Add(slot);
        RefreshSlotDevices(_viewModel.InputSlots, _allInputDevices, _viewModel.UnusedInputDevices);

        var firstFree = slot.AvailableDevices.FirstOrDefault();
        if (firstFree != null) slot.SelectedDevice = firstFree;

        UpdateInputAddButtonVisibility();
        SaveSettings();
    }

    // ── Send to unused / Restore ─────────────────────────────────────────────

    private void SendOutputToUnused_Click(object sender, RoutedEventArgs e)
    {
        var usedIds = _viewModel.OutputSlots
            .Where(s => s.SelectedDevice != null).Select(s => s.SelectedDevice!.Id).ToHashSet();
        var unusedIds = _viewModel.UnusedOutputDevices.Select(d => d.Id).ToHashSet();

        foreach (var d in _allOutputDevices)
            if (!usedIds.Contains(d.Id) && !unusedIds.Contains(d.Id))
                _viewModel.UnusedOutputDevices.Add(d);

        RefreshSlotDevices(_viewModel.OutputSlots, _allOutputDevices, _viewModel.UnusedOutputDevices);
        UpdateOutputAddButtonVisibility();
        _viewModel.NotifyUnusedChanged();
        SaveSettings();
    }

    private void SendInputToUnused_Click(object sender, RoutedEventArgs e)
    {
        var usedIds = _viewModel.InputSlots
            .Where(s => s.SelectedDevice != null).Select(s => s.SelectedDevice!.Id).ToHashSet();
        var unusedIds = _viewModel.UnusedInputDevices.Select(d => d.Id).ToHashSet();

        foreach (var d in _allInputDevices)
            if (!usedIds.Contains(d.Id) && !unusedIds.Contains(d.Id))
                _viewModel.UnusedInputDevices.Add(d);

        RefreshSlotDevices(_viewModel.InputSlots, _allInputDevices, _viewModel.UnusedInputDevices);
        UpdateInputAddButtonVisibility();
        _viewModel.NotifyUnusedChanged();
        SaveSettings();
    }

    private void RestoreOutputDevice_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is AudioDevice device)
        {
            _viewModel.UnusedOutputDevices.Remove(device);
            RefreshSlotDevices(_viewModel.OutputSlots, _allOutputDevices, _viewModel.UnusedOutputDevices);
            UpdateOutputAddButtonVisibility();
            _viewModel.NotifyUnusedChanged();
            SaveSettings();
        }
    }

    private void RestoreInputDevice_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is AudioDevice device)
        {
            _viewModel.UnusedInputDevices.Remove(device);
            RefreshSlotDevices(_viewModel.InputSlots, _allInputDevices, _viewModel.UnusedInputDevices);
            UpdateInputAddButtonVisibility();
            _viewModel.NotifyUnusedChanged();
            SaveSettings();
        }
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void TabOutput_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedTab = "Output";
    private void TabInput_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedTab = "Input";
    private void TabUnused_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedTab = "Unused";

    // ── Title bar / window ────────────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    // ── Settings persistence ──────────────────────────────────────────────────

    private void SaveSettings()
    {
        var settings = new AppSettings
        {
            MasterVolume = _viewModel.MasterVolume,
            DeviceMappings = _viewModel.OutputSlots.Select(MappingFromSlot).ToList(),
            InputDeviceMappings = _viewModel.InputSlots.Select(MappingFromSlot).ToList(),
            UnusedOutputDeviceIds = _viewModel.UnusedOutputDevices.Select(d => d.Id).ToList(),
            UnusedInputDeviceIds = _viewModel.UnusedInputDevices.Select(d => d.Id).ToList()
        };
        _settingsService.Save(settings);
    }

    private static DeviceMapping MappingFromSlot(DeviceSlotViewModel s) => new()
    {
        DeviceId = s.SelectedDevice?.Id ?? string.Empty,
        DeviceName = s.SelectedDevice?.Name ?? string.Empty,
        ModifierKeys = s.ModifierKeys,
        Key = s.Key,
        BaseVolume = s.BaseVolume
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

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
        parts.Add(KeyInterop.KeyFromVirtualKey(vk).ToString());
        return string.Join("+", parts);
    }
}

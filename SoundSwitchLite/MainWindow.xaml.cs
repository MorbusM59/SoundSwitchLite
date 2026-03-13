using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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

    public int BaseVolume
    {
        get => _baseVolume;
        set { _baseVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
    }

    public string ActivateButtonTooltip => IsInput ? "Set as default input device" : "Set as default output device";

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

    // Info panel state
    private bool _infoPanelVisible;
    private string _infoText = string.Empty;

    public bool InfoPanelVisible
    {
        get => _infoPanelVisible;
        set { _infoPanelVisible = value; OnPropertyChanged(); }
    }

    public string InfoText
    {
        get => _infoText;
        set { _infoText = value; OnPropertyChanged(); }
    }
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

    // Volume button repeat support
    private DispatcherTimer? _volumeRepeatTimer;
    private DeviceSlotViewModel? _repeatSlot;
    private bool _repeatIsIncrement;
    private int _repeatCount;

    // Info auto-hide timer
    private DispatcherTimer? _infoAutoHideTimer;
    private FrameworkElement? _infoSourceElement;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _audioService = App.AudioDeviceService;
        _hotkeyService = App.HotkeyService;
        _settingsService = App.SettingsService;

        Loaded += OnLoaded;
        Closing += OnWindowClosing;
        MouseDown += (s, e) => { _viewModel.InfoPanelVisible = false; };
    }

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe)
            {
                var text = fe.Tag as string ?? "";
                _viewModel.InfoText = text;
                _viewModel.InfoPanelVisible = true;
                _infoSourceElement = fe;
                if (_infoAutoHideTimer == null)
                {
                    _infoAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                    _infoAutoHideTimer.Tick += InfoAutoHideTimer_Tick;
                }
                _infoAutoHideTimer.Start();
            }
        }
        catch { }
    }

    private void InfoAutoHideTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_infoSourceElement == null)
            {
                _infoAutoHideTimer?.Stop();
                return;
            }

            bool overSource = _infoSourceElement.IsMouseOver;
            bool overPanel = InfoPanelBorder != null && InfoPanelBorder.IsMouseOver;

            if (!overSource && !overPanel)
            {
                _viewModel.InfoPanelVisible = false;
                _infoAutoHideTimer?.Stop();
                _infoSourceElement = null;
            }
        }
        catch { }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundSwitchLite", "error.log"), DateTime.UtcNow.ToString("o") + " " + ex + "\n"); } catch { }
        }
    }

    private DeviceSlotViewModel CreateSlot(bool isInput)
    {
        var slot = new DeviceSlotViewModel { IsInput = isInput };
        slot.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(DeviceSlotViewModel.SelectedDevice)) SaveSettings(); };
        return slot;
    }

    private void RestoreMappingToSlot(DeviceSlotViewModel slot, DeviceMapping mapping, List<AudioDevice> allDevices)
    {
        slot.BaseVolume = mapping.BaseVolume != 0 ? mapping.BaseVolume : (mapping.DefaultVolume ?? 100);
        slot.ModifierKeys = mapping.ModifierKeys;
        slot.Key = mapping.Key;
        var device = allDevices.FirstOrDefault(d => d.Id == mapping.DeviceId);
        if (device != null) slot.SelectedDevice = device;
        if (slot.Key != 0) RegisterSlotHotkey(slot);
    }

    private void RefreshSlotDevices(IEnumerable<DeviceSlotViewModel> slots, List<AudioDevice> allDevices, ObservableCollection<AudioDevice> unused)
    {
        _isRefreshingDevices = true;
        try
        {
            var usedIds = _viewModel.OutputSlots.Concat(_viewModel.InputSlots)
                .Where(s => s.SelectedDevice != null).Select(s => s.SelectedDevice!.Id).ToHashSet();

            foreach (var slot in slots)
            {
                var avail = allDevices.Where(d => !unused.Any(u => u.Id == d.Id) || (slot.SelectedDevice?.Id == d.Id))
                                      .Where(d => !usedIds.Contains(d.Id) || (slot.SelectedDevice?.Id == d.Id))
                                      .ToList();
                // Ensure selected device remains in available list
                if (slot.SelectedDevice != null && !avail.Any(a => a.Id == slot.SelectedDevice!.Id))
                    avail.Insert(0, slot.SelectedDevice);
                slot.AvailableDevices = avail;
            }
        }
        finally { _isRefreshingDevices = false; }
    }

    private async Task RefreshActiveDevice()
    {
        var defaultPlayback = await _audioService.GetDefaultDeviceIdAsync();
        var defaultCapture = await _audioService.GetDefaultCaptureDeviceIdAsync();

        foreach (var s in _viewModel.OutputSlots) s.IsActive = s.SelectedDevice?.Id == defaultPlayback;
        foreach (var s in _viewModel.InputSlots) s.IsActive = s.SelectedDevice?.Id == defaultCapture;
    }

    private void UpdateOutputAddButtonVisibility()
    {
        _viewModel.CanAddOutputDevice = _viewModel.OutputSlots.Count < _allOutputDevices.Count;
    }

    private void UpdateInputAddButtonVisibility()
    {
        _viewModel.CanAddInputDevice = _viewModel.InputSlots.Count < _allInputDevices.Count;
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

    // Volume controls and helpers omitted for brevity; implement the merged behavior
    private async void VolumeDecrement_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot)
            {
                slot.BaseVolume--;
                await ApplyVolumeToActiveSlotAsync(slot);
                SaveSettings();
            }
        }
        catch { }
    }

    private async void VolumeIncrement_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot)
            {
                slot.BaseVolume++;
                await ApplyVolumeToActiveSlotAsync(slot);
                SaveSettings();
            }
        }
        catch { }
    }

    private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(char.IsDigit);

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

    private async void BaseVolumeTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is TextBox tb && tb.DataContext is DeviceSlotViewModel slot)
            {
                if (int.TryParse(tb.Text, out int val))
                    slot.BaseVolume = val;
                else
                    tb.Text = slot.BaseVolume.ToString();
                await ApplyVolumeToActiveSlotAsync(slot, force: true);
                SaveSettings();
            }
        }
        catch { }
    }

    private async void MasterVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        try { await ApplyMasterVolumeAsync(); SaveSettings(); } catch { }
    }

    private async Task ApplyMasterVolumeAsync()
    {
        foreach (var slot in _viewModel.OutputSlots.Concat(_viewModel.InputSlots))
            await ApplyVolumeToActiveSlotAsync(slot);
    }

    private async Task ApplyVolumeToActiveSlotAsync(DeviceSlotViewModel slot, bool force = false)
    {
        if (!force && !slot.IsActive) return;
        if (slot.SelectedDevice == null) return;
        int effectiveVolume = (int)Math.Round(_viewModel.MasterVolume / 100.0 * slot.BaseVolume);
        if (slot.IsInput)
            await _audioService.SetCaptureVolumeAsync(slot.SelectedDevice.Id, effectiveVolume);
        else
            await _audioService.SetVolumeAsync(slot.SelectedDevice.Id, effectiveVolume);
    }

    // Press-and-hold acceleration support
    private void VolumeButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DeviceSlotViewModel slot)
        {
            _repeatSlot = slot;
            _repeatIsIncrement = (btn.Content?.ToString() == "+");
            _repeatCount = 0;
            _volumeRepeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _volumeRepeatTimer.Tick += VolumeRepeatTimer_Tick;
            _volumeRepeatTimer.Start();
        }
    }

    private void VolumeButton_PreviewMouseUp(object sender, MouseButtonEventArgs e) => StopVolumeRepeat();
    private void VolumeButton_MouseLeave(object sender, MouseEventArgs e) => StopVolumeRepeat();

    private async void VolumeRepeatTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_repeatSlot == null || _volumeRepeatTimer == null) return;
            if (_repeatIsIncrement) _repeatSlot.BaseVolume++; else _repeatSlot.BaseVolume--;
            await ApplyVolumeToActiveSlotAsync(_repeatSlot, force: true);
            SaveSettings();
            _repeatCount++;
            double nextInterval = Math.Max(1.0 / 20.0, 1.0 / (4 + _repeatCount));
            _volumeRepeatTimer.Interval = TimeSpan.FromSeconds(nextInterval);
        }
        catch { }
    }

    private void StopVolumeRepeat()
    {
        if (_volumeRepeatTimer != null)
        {
            _volumeRepeatTimer.Stop();
            _volumeRepeatTimer.Tick -= VolumeRepeatTimer_Tick;
            _volumeRepeatTimer = null;
        }
        _repeatSlot = null;
        _repeatCount = 0;
    }

    // Hotkey handling
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

    // Add/Remove slots
    private void RemoveSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot)
        {
            if (slot.IsActive) return; // avoid removing active slot
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
        var firstFree = slot.AvailableDevices.FirstOrDefault();
        if (firstFree != null) slot.SelectedDevice = firstFree;
        UpdateOutputAddButtonVisibility();
        SaveSettings();
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.Tag is DeviceSlotViewModel slot)
            {
                await ActivateSlotDeviceAsync(slot);
            }
        }
        catch { }
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

    private void SendOutputToUnused_Click(object sender, RoutedEventArgs e)
    {
        var usedIds = _viewModel.OutputSlots.Where(s => s.SelectedDevice != null).Select(s => s.SelectedDevice!.Id).ToHashSet();
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
        var usedIds = _viewModel.InputSlots.Where(s => s.SelectedDevice != null).Select(s => s.SelectedDevice!.Id).ToHashSet();
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

    private void RestoreOutputDevice_ButtonClick(object sender, RoutedEventArgs e)
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

    private void RestoreInputDevice_ButtonClick(object sender, RoutedEventArgs e)
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

    private void TabOutput_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedTab = "Output";
    private void TabInput_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedTab = "Input";
    private void TabUnused_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedTab = "Unused";

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

    private void ShowBalloon(string message)
    {
        try
        {
            var trayIcon = (Hardcodet.Wpf.TaskbarNotification.TaskbarIcon)Application.Current.FindResource("TrayIcon");
            trayIcon.ShowBalloonTip("SoundSwitch Lite", message, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }
        catch { }
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

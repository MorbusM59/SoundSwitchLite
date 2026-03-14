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
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private bool _isArmedForDeletion;

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

    public bool IsArmedForDeletion
    {
        get => _isArmedForDeletion;
        set { _isArmedForDeletion = value; OnPropertyChanged(); }
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
    private int _inputMasterVolume = 100;
    private string _selectedTab = "Output";
    private bool _canAddOutputDevice;
    private bool _canAddInputDevice;
    private bool _canSendOutputDevices;
    private bool _canSendInputDevices;

    public ObservableCollection<DeviceSlotViewModel> OutputSlots { get; } = new();
    public ObservableCollection<DeviceSlotViewModel> InputSlots { get; } = new();
    public ObservableCollection<AudioDevice> UnusedOutputDevices { get; } = new();
    public ObservableCollection<AudioDevice> UnusedInputDevices { get; } = new();

    public int MasterVolume
    {
        get => _masterVolume;
        set { _masterVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
    }

    public int InputMasterVolume
    {
        get => _inputMasterVolume;
        set { _inputMasterVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
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
            OnPropertyChanged(nameof(IsInfoTabSelected));
        }
    }

    public bool IsOutputTabSelected => _selectedTab == "Output";
    public bool IsInputTabSelected => _selectedTab == "Input";
    public bool IsUnusedTabSelected => _selectedTab == "Unused";
    public bool IsInfoTabSelected => _selectedTab == "Info";

    public bool CanAddOutputDevice
    {
        get => _canAddOutputDevice;
        set { _canAddOutputDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(OutputFooterVisible)); }
    }

    public bool CanAddInputDevice
    {
        get => _canAddInputDevice;
        set { _canAddInputDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(InputFooterVisible)); }
    }

    public bool CanSendOutputDevices
    {
        get => _canSendOutputDevices;
        set { _canSendOutputDevices = value; OnPropertyChanged(); OnPropertyChanged(nameof(OutputFooterVisible)); }
    }

    public bool CanSendInputDevices
    {
        get => _canSendInputDevices;
        set { _canSendInputDevices = value; OnPropertyChanged(); OnPropertyChanged(nameof(InputFooterVisible)); }
    }

    public bool OutputFooterVisible => CanAddOutputDevice || CanSendOutputDevices;
    public bool InputFooterVisible => CanAddInputDevice || CanSendInputDevices;

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
    // Block persistence while initial bindings fire before slots are restored.
    private bool _suppressSaves = true;
    private bool _loaded;
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

    // System volume polling
    private DispatcherTimer? _volumePollTimer;
    private bool _suppressMasterVolumeApply;

    // Info auto-hide timer (removed — info moved to separate tab)

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

    // Info panel moved to a dedicated tab — per-element info button removed.

    // Hover is intentionally a no-op — show only a tooltip on hover.
    private void Element_MouseEnter(object sender, MouseEventArgs e)
    {
        // no-op: tooltips provide the short hover guidance
    }

    private void Element_MouseLeave(object sender, MouseEventArgs e)
    {
        // no-op
    }

    private void Element_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // No-op: right-click no longer opens an info panel (info moved to separate tab).
        // Allow elements to handle right-clicks themselves (e.g., hotkey clear).
    }

    // Info auto-hide timer removed with panel -> Info tab.

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = OnLoadedAsync();
    }

    private async Task OnLoadedAsync()
    {
        try
        {
            _suppressSaves = true;
            _hotkeyService.Initialize(this);

            _allOutputDevices = (await _audioService.GetPlaybackDevicesAsync()).ToList();
            _allInputDevices = (await _audioService.GetCaptureDevicesAsync()).ToList();

            var settings = _settingsService.Load();
            _viewModel.MasterVolume = settings.MasterVolume;
            _viewModel.InputMasterVolume = settings.InputMasterVolume;

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

            // Restore output slots from all saved mappings so card count persists across restarts.
            var outputMappings = settings.DeviceMappings;
            if (outputMappings.Count > 0)
            {
                foreach (var mapping in outputMappings)
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

            // Restore input slots from all saved mappings so card count persists across restarts.
            var inputMappings = settings.InputDeviceMappings;
            foreach (var mapping in inputMappings)
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
        finally
        {
            _suppressSaves = false;
        }
        _loaded = true;
        _volumePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _volumePollTimer.Tick += async (_, _) => await PollSystemVolumeAsync();
        _volumePollTimer.Start();
        await SyncStateFromSystemAsync();
    }

    private async Task SyncStateFromSystemAsync()
    {
        // For each default device, infer the global master from (systemVol / baseVol).
        // If inferred > 100: cap master at 100 and enforce effective volume on the system.
        // If inferred <= 100: display inferred master without touching system volume.

        var defaultOutputId = await _audioService.GetDefaultDeviceIdAsync();
        if (defaultOutputId != null)
        {
            var activeSlot = _viewModel.OutputSlots.FirstOrDefault(s => s.SelectedDevice?.Id == defaultOutputId);
            if (activeSlot != null && activeSlot.BaseVolume > 0)
            {
                var systemVol = await _audioService.GetVolumeAsync(defaultOutputId);
                if (systemVol.HasValue)
                {
                    double inferred = systemVol.Value / (activeSlot.BaseVolume / 100.0);
                    if (inferred > 100)
                    {
                        _viewModel.MasterVolume = 100;
                        await _audioService.SetVolumeAsync(defaultOutputId, activeSlot.BaseVolume);
                    }
                    else
                    {
                        _viewModel.MasterVolume = (int)Math.Round(inferred);
                    }
                }
            }
        }

        var defaultInputId = await _audioService.GetDefaultCaptureDeviceIdAsync();
        if (defaultInputId != null)
        {
            var activeSlot = _viewModel.InputSlots.FirstOrDefault(s => s.SelectedDevice?.Id == defaultInputId);
            if (activeSlot != null && activeSlot.BaseVolume > 0)
            {
                var systemVol = await _audioService.GetCaptureVolumeAsync(defaultInputId);
                if (systemVol.HasValue)
                {
                    double inferred = systemVol.Value / (activeSlot.BaseVolume / 100.0);
                    if (inferred > 100)
                    {
                        _viewModel.InputMasterVolume = 100;
                        await _audioService.SetCaptureVolumeAsync(defaultInputId, activeSlot.BaseVolume);
                    }
                    else
                    {
                        _viewModel.InputMasterVolume = (int)Math.Round(inferred);
                    }
                }
            }
        }

        await RefreshActiveDevice();
        SaveSettings();
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_loaded && (bool)e.NewValue)
            _ = SyncStateFromSystemAsync();
    }

    private async Task PollSystemVolumeAsync()
    {
        // Read fresh system volumes and update the global masters if they've changed.
        var defaultOutputId = await _audioService.GetDefaultDeviceIdAsync();
        if (defaultOutputId != null)
        {
            var activeSlot = _viewModel.OutputSlots.FirstOrDefault(s => s.SelectedDevice?.Id == defaultOutputId);
            if (activeSlot != null && activeSlot.BaseVolume > 0)
            {
                var systemVol = await _audioService.GetVolumeFreshAsync(defaultOutputId);
                if (systemVol.HasValue)
                {
                    double inferred = systemVol.Value / (activeSlot.BaseVolume / 100.0);
                    int newMaster = inferred > 100 ? 100 : (int)Math.Round(inferred);
                    if (Math.Abs(newMaster - _viewModel.MasterVolume) >= 1)
                    {
                        if (inferred > 100)
                            await _audioService.SetVolumeAsync(defaultOutputId, activeSlot.BaseVolume);
                        AnimateMasterSlider(MasterVolumeSlider, _viewModel.MasterVolume, newMaster, isInput: false);
                        SaveSettings();
                    }
                }
            }
        }

        var defaultInputId = await _audioService.GetDefaultCaptureDeviceIdAsync();
        if (defaultInputId != null)
        {
            var activeSlot = _viewModel.InputSlots.FirstOrDefault(s => s.SelectedDevice?.Id == defaultInputId);
            if (activeSlot != null && activeSlot.BaseVolume > 0)
            {
                var systemVol = await _audioService.GetCaptureVolumeFreshAsync(defaultInputId);
                if (systemVol.HasValue)
                {
                    double inferred = systemVol.Value / (activeSlot.BaseVolume / 100.0);
                    int newMaster = inferred > 100 ? 100 : (int)Math.Round(inferred);
                    if (Math.Abs(newMaster - _viewModel.InputMasterVolume) >= 1)
                    {
                        if (inferred > 100)
                            await _audioService.SetCaptureVolumeAsync(defaultInputId, activeSlot.BaseVolume);
                        AnimateMasterSlider(InputMasterVolumeSlider, _viewModel.InputMasterVolume, newMaster, isInput: true);
                        SaveSettings();
                    }
                }
            }
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
        if (slot.Key != 0)
        {
            slot.HotkeyDisplay = BuildHotkeyDisplayString(slot.ModifierKeys, slot.Key);
            RegisterSlotHotkey(slot);
        }
        var device = allDevices.FirstOrDefault(d => d.Id == mapping.DeviceId);
        if (device != null) slot.SelectedDevice = device;
    }

    private void RefreshSlotDevices(IEnumerable<DeviceSlotViewModel> slots, List<AudioDevice> allDevices, ObservableCollection<AudioDevice> unused)
    {
        _isRefreshingDevices = true;
        try
        {
            // IDs already assigned to some slot — used to prevent duplicates across dropdowns.
            var usedIds = slots.Where(s => s.SelectedDevice != null).Select(s => s.SelectedDevice!.Id).ToHashSet();

            foreach (var slot in slots)
            {
                // A slot's own selected device MUST remain in its ItemsSource, otherwise WPF's
                // ComboBox silently resets SelectedItem to null when ItemsSource is reassigned.
                // Exclude only devices selected by OTHER slots, not this one's own selection.
                var avail = allDevices
                    .Where(d => !unused.Any(u => u.Id == d.Id)
                             && (d.Id == slot.SelectedDevice?.Id || !usedIds.Contains(d.Id)))
                    .ToList();

                if (slot.SelectedDevice != null && !avail.Any(d => d.Id == slot.SelectedDevice.Id))
                {
                    avail.Insert(0, slot.SelectedDevice);
                }

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
        var usedIds = _viewModel.OutputSlots.Where(s => s.SelectedDevice != null).Select(s => s.SelectedDevice!.Id).ToHashSet();
        var unusedIds = _viewModel.UnusedOutputDevices.Select(d => d.Id).ToHashSet();
        bool hasAvailable = _allOutputDevices.Any(d => !unusedIds.Contains(d.Id) && !usedIds.Contains(d.Id));
        _viewModel.CanAddOutputDevice = hasAvailable;
        _viewModel.CanSendOutputDevices = hasAvailable;
    }

    private void UpdateInputAddButtonVisibility()
    {
        var usedIds = _viewModel.InputSlots.Where(s => s.SelectedDevice != null).Select(s => s.SelectedDevice!.Id).ToHashSet();
        var unusedIds = _viewModel.UnusedInputDevices.Select(d => d.Id).ToHashSet();
        bool hasAvailable = _allInputDevices.Any(d => !unusedIds.Contains(d.Id) && !usedIds.Contains(d.Id));
        _viewModel.CanAddInputDevice = hasAvailable;
        _viewModel.CanSendInputDevices = hasAvailable;
    }

    private async Task ActivateSlotDeviceAsync(DeviceSlotViewModel slot)
    {
        if (slot.SelectedDevice == null) return;
        int master = slot.IsInput ? _viewModel.InputMasterVolume : _viewModel.MasterVolume;
        int effectiveVolume = (int)Math.Round(master / 100.0 * slot.BaseVolume);

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
    private void VolumeDecrement_Click(object sender, RoutedEventArgs e)
    {
        _ = VolumeDecrement_ClickAsync(sender, e);
    }

    private async Task VolumeDecrement_ClickAsync(object sender, RoutedEventArgs e)
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
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundSwitchLite", "error.log"), DateTime.UtcNow.ToString("o") + " " + ex + "\n"); } catch { }
        }
    }

    private void VolumeIncrement_Click(object sender, RoutedEventArgs e)
    {
        _ = VolumeIncrement_ClickAsync(sender, e);
    }

    private async Task VolumeIncrement_ClickAsync(object sender, RoutedEventArgs e)
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
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundSwitchLite", "error.log"), DateTime.UtcNow.ToString("o") + " " + ex + "\n"); } catch { }
        }
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

    private void BaseVolumeTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _ = BaseVolumeTextBox_LostFocusAsync(sender, e);
    }

    private async Task BaseVolumeTextBox_LostFocusAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is TextBox tb && tb.DataContext is DeviceSlotViewModel slot)
            {
                // Parse and clamp; reset display if the field is empty or non-numeric.
                if (int.TryParse(tb.Text, out int val))
                    slot.BaseVolume = val; // property setter clamps to 0-100
                else
                    tb.Text = slot.BaseVolume.ToString(); // restore last valid value
                await ApplyVolumeToActiveSlotAsync(slot, force: true);
                SaveSettings();
            }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundSwitchLite", "error.log"), DateTime.UtcNow.ToString("o") + " " + ex + "\n"); } catch { }
        }
    }

    private void MasterVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressMasterVolumeApply) return;
        _ = OutputMasterVolumeSlider_ValueChangedAsync();
    }

    private async Task OutputMasterVolumeSlider_ValueChangedAsync()
    {
        try { await ApplyOutputMasterVolumeAsync(); SaveSettings(); } catch (Exception ex) { try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundSwitchLite", "error.log"), DateTime.UtcNow.ToString("o") + " " + ex + "\n"); } catch { } }
    }

    private void InputMasterVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressMasterVolumeApply) return;
        _ = InputMasterVolumeSlider_ValueChangedAsync();
    }

    private async Task InputMasterVolumeSlider_ValueChangedAsync()
    {
        try { await ApplyInputMasterVolumeAsync(); SaveSettings(); } catch (Exception ex) { try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundSwitchLite", "error.log"), DateTime.UtcNow.ToString("o") + " " + ex + "\n"); } catch { } }
    }

    private void AnimateMasterSlider(Slider slider, int fromValue, int toValue, bool isInput)
    {
        // Update ViewModel immediately so the text label reflects the new value at once.
        if (isInput)
            _viewModel.InputMasterVolume = toValue;
        else
            _viewModel.MasterVolume = toValue;

        _suppressMasterVolumeApply = true;
        var anim = new DoubleAnimation
        {
            From = fromValue,
            To = toValue,
            Duration = new Duration(TimeSpan.FromMilliseconds(250)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (_, _) =>
        {
            _suppressMasterVolumeApply = false;
            // After animation, binding (already at correct value) resumes cleanly.
        };
        slider.BeginAnimation(Slider.ValueProperty, anim);
    }

    private async Task ApplyOutputMasterVolumeAsync()
    {
        foreach (var slot in _viewModel.OutputSlots)
            await ApplyVolumeToActiveSlotAsync(slot);
    }

    private async Task ApplyInputMasterVolumeAsync()
    {
        foreach (var slot in _viewModel.InputSlots)
            await ApplyVolumeToActiveSlotAsync(slot);
    }

    private async Task ApplyVolumeToActiveSlotAsync(DeviceSlotViewModel slot, bool force = false)
    {
        if (!force && !slot.IsActive) return;
        if (slot.SelectedDevice == null) return;
        int master = slot.IsInput ? _viewModel.InputMasterVolume : _viewModel.MasterVolume;
        int effectiveVolume = (int)Math.Round(master / 100.0 * slot.BaseVolume);
        if (slot.IsInput)
            await _audioService.SetCaptureVolumeAsync(slot.SelectedDevice.Id, effectiveVolume);
        else
            await _audioService.SetVolumeAsync(slot.SelectedDevice.Id, effectiveVolume);
    }

    private void VolumeSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is not Slider slider) return;

            // Keep normal thumb dragging behavior intact.
            var source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is System.Windows.Controls.Primitives.Thumb) return;
                source = VisualTreeHelper.GetParent(source);
            }

            var p = e.GetPosition(slider);
            if (slider.ActualWidth <= 0) return;

            var ratio = Math.Clamp(p.X / slider.ActualWidth, 0.0, 1.0);
            if (slider.IsDirectionReversed) ratio = 1.0 - ratio;
            slider.Value = slider.Minimum + (slider.Maximum - slider.Minimum) * ratio;
            e.Handled = true;
        }
        catch { }
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

    private void VolumeRepeatTimer_Tick(object? sender, EventArgs e)
    {
        _ = VolumeRepeatTimer_TickAsync();
    }

    private async Task VolumeRepeatTimer_TickAsync()
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
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundSwitchLite", "error.log"), DateTime.UtcNow.ToString("o") + " " + ex + "\n"); } catch { }
        }
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

    // Play button removed — devices activated by clicking the card container.

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var src = e.OriginalSource as DependencyObject;
            if (IsInteractiveChild(src)) return;

            if (sender is FrameworkElement fe && fe.DataContext is DeviceSlotViewModel slot)
            {
                _ = ActivateSlotDeviceAsync(slot);
            }
        }
        catch { }
    }

    private void Card_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is DeviceSlotViewModel slot)
            {
                // Right-click arms deletion for inactive slots; second right-click deletes.
                if (slot.IsActive) return; // do not allow arming active slot

                if (!slot.IsArmedForDeletion)
                {
                    slot.IsArmedForDeletion = true;
                }
                else
                {
                    // perform deletion similar to RemoveSlot_Click
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
                e.Handled = true;
            }
        }
        catch { }
    }

    private static bool IsInteractiveChild(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
            if (d is System.Windows.Controls.ComboBox) return true;
            if (d is System.Windows.Controls.TextBox) return true;
            if (d is System.Windows.Controls.Slider) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
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
            // If there's an empty slot, assign this device to it for convenience
            var emptySlot = _viewModel.OutputSlots.FirstOrDefault(s => s.SelectedDevice == null);
            if (emptySlot != null)
            {
                emptySlot.SelectedDevice = device;
            }
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
            var emptySlot = _viewModel.OutputSlots.FirstOrDefault(s => s.SelectedDevice == null);
            if (emptySlot != null)
            {
                emptySlot.SelectedDevice = device;
            }
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
            var emptySlot = _viewModel.InputSlots.FirstOrDefault(s => s.SelectedDevice == null);
            if (emptySlot != null)
            {
                emptySlot.SelectedDevice = device;
            }
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
            var emptySlot = _viewModel.InputSlots.FirstOrDefault(s => s.SelectedDevice == null);
            if (emptySlot != null)
            {
                emptySlot.SelectedDevice = device;
            }
            RefreshSlotDevices(_viewModel.InputSlots, _allInputDevices, _viewModel.UnusedInputDevices);
            UpdateInputAddButtonVisibility();
            _viewModel.NotifyUnusedChanged();
            SaveSettings();
        }
    }

    private void TabOutput_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedTab = "Output";
    private void TabInput_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedTab = "Input";
    private void TabUnused_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedTab = "Unused";
    private void TabInfo_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedTab = "Info";

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

    protected override void OnClosed(EventArgs e)
    {
        _volumePollTimer?.Stop();
        base.OnClosed(e);
    }

    private void SaveSettings()
    {
        if (_suppressSaves) return;
        var settings = new AppSettings
        {
            MasterVolume = _viewModel.MasterVolume,
            InputMasterVolume = _viewModel.InputMasterVolume,
            // Persist all slots so user-created cards survive restarts.
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

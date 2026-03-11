# SoundSwitch Lite

A super lightweight Windows 11 desktop app that lets you quickly switch audio output devices using global hotkeys or a minimal UI.

## Features

- **System Tray** — Runs silently in the background; left-click the tray icon to show/hide the window
- **Dark-themed UI** — Minimal, modern dark UI with accent colors and rounded containers
- **Global Hotkeys** — Assign system-wide keyboard shortcuts to each audio device; works even when the app is hidden
- **Persistent Settings** — All device assignments and hotkeys are saved to `%AppData%/SoundSwitchLite/settings.json`
- **Multiple Devices** — Add as many device slots as you have audio output devices
- **One-click Switching** — Click the play button in any device slot to immediately switch to that output
- **Single Instance** — Prevents duplicate launches

## Prerequisites

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build & Run

```bash
git clone https://github.com/MorbusM59/SoundSwitchLite.git
cd SoundSwitchLite

dotnet build
dotnet run --project SoundSwitchLite/SoundSwitchLite.csproj
```

## Usage

1. **Add a device slot** — Click "+ Add Device" to create a new device slot
2. **Select a device** — Use the dropdown in the slot to pick an audio output device
3. **Assign a hotkey** — Click the hotkey field (shows "Click to assign hotkey"), then press your desired key combination (e.g. `Ctrl+Alt+1`)
4. **Clear a hotkey** — Right-click the hotkey field to remove the assigned shortcut
5. **Switch device** — Click the ▶ button in any slot, or press the assigned global hotkey from anywhere
6. **Tray behavior** — Closing the window hides it to the system tray; right-click the tray icon and select "Exit" to fully quit

## Settings

Settings are automatically saved to `%AppData%\SoundSwitchLite\settings.json`.

Example:

```json
{
  "DeviceMappings": [
    {
      "DeviceId": "some-guid-here",
      "DeviceName": "Speakers (Realtek Audio)",
      "ModifierKeys": 3,
      "Key": 49
    }
  ]
}
```

## License

MIT

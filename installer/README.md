# Building the SoundSwitch Lite Installer

Follow these two steps on a Windows machine to produce a single `SoundSwitchLite-Setup-1.0.0.exe`
that your spouse (or anyone) can run to install the app.

---

## Step 1 — Publish the application

Open a terminal in the repository root and run:

```powershell
dotnet publish SoundSwitchLite/SoundSwitchLite.csproj /p:PublishProfile=win-x64
```

This creates a **self-contained, single-file** Windows executable at:

```
SoundSwitchLite/publish/win-x64/SoundSwitchLite.exe
```

No .NET runtime needs to be installed on the target PC — everything is bundled.

---

## Step 2 — Build the installer

1. Download and install **Inno Setup 6** from <https://jrsoftware.org/isinfo.php> (free).
2. Open `installer/SoundSwitchLite.iss` in the Inno Setup IDE, then click **Build → Compile**
   (or run from the command line):

   ```powershell
   iscc installer\SoundSwitchLite.iss
   ```

3. The finished installer is placed at:

   ```
   installer/Output/SoundSwitchLite-Setup-1.0.0.exe
   ```

---

## What the installer does

| Feature | Details |
|---------|---------|
| **Installation wizard** | Standard Windows wizard with a custom directory picker |
| **Self-contained** | Bundles the .NET runtime — no prerequisites needed |
| **Start Menu shortcut** | Created automatically |
| **Desktop shortcut** | Optional (unchecked by default) |
| **Auto-start at login** | Optional (unchecked by default) |
| **Clean uninstall** | Listed in *Add/Remove Programs*; removes all installed files and registry entries |

---

## Updating the version

Before building a new release, bump `Version` in
`SoundSwitchLite/SoundSwitchLite.csproj` **and** the `#define AppVersion` line at the
top of `installer/SoundSwitchLite.iss` to match.

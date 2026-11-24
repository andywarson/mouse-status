# MaicongMouseStatus

[简体中文](README.md)

MaicongMouseStatus is a Windows 11 x64 utility for monitoring a wireless mouse from a desktop widget, a compact taskbar strip, or the notification area. Supported devices can report battery level, charging state, DPI, and the selected polling-rate setting. The app uses local Windows interfaces and requires no API key or vendor account.

## Features

- Full and compact desktop widgets, an independently enabled taskbar strip, and a notification-area menu.
- Optional battery notifications, on-screen DPI and polling-rate changes, click-through locking, custom images and accents, and eight themes.
- On-device battery history, observed-time distributions for DPI and polling rate, and a runtime estimate when enough discharge data is available.
- Adjustable device refresh intervals from 5 to 300 seconds and optional launch at Windows sign-in.

## Install and use

1. Download `MaicongMouseStatus-<version>-windows-x64.zip` and its `.sha256` file from **Releases**.
2. Compare the PowerShell hash against the checksum file. Extract the ZIP and launch `MaicongMouseStatus.exe`.

   ```powershell
   Get-FileHash -Algorithm SHA256 .\MaicongMouseStatus-1.0.0-windows-x64.zip
   Get-Content .\MaicongMouseStatus-1.0.0-windows-x64.zip.sha256
   ```

3. Open Settings from the notification-area icon. Select automatic 2.4 GHz receiver detection or an already paired Windows Bluetooth device.
4. For example, connect a MCHOSE receiver and enable only the taskbar strip to see battery and polling rate. Open Statistics for the past hour, day, or week.

## Configuration and compatibility

| Settings area | Options |
| --- | --- |
| General | Data source, refresh interval, display modes, notifications, launch at sign-in |
| Appearance | Theme, accent, opacity, avatar, tray and taskbar readout |
| Statistics | Time range and DPI or polling-rate distribution |

The 2.4 GHz reader includes adapters for MCHOSE receiver IDs `3837:1018`, `3837:1014`, and `3837:1016`. Hardware testing used `3837:1018`. Treat the other two IDs as experimental. Bluetooth LE supports connection status and battery for paired devices that expose the standard GATT Battery Service. The user interface is currently in Simplified Chinese.

Preferences are stored in the current Windows user's registry. Historical samples are stored under `%LocalAppData%\MaicongMouseStatus\history`. The app removes daily files older than 14 days by UTC date. Statistics use adjacent valid readings and skip gaps during shutdown, disconnection, or extended sampling delays.

## FAQ

**Where is the taskbar strip?** It appears to the left of the hidden-icons arrow on the primary taskbar. Open the standard tray icon when space or full-screen conditions hide the strip.

**Why is a reading or chart empty?** Confirm the data source and wait for a valid sample. The distribution chart needs two consecutive valid readings. Move the mouse to wake a sleeping device.

**What if I move the executable?** Open it from the new location and save the sign-in setting again.

**What should I do with an unknown-publisher prompt?** The executable is currently unsigned. Use the repository release and verify its SHA-256.

## Build from source

Use Windows x64, PowerShell, and the Windows .NET Framework toolchain. There are no NuGet packages to restore.

```powershell
.\Build.ps1 -Test
.\Build.ps1
.\Package.ps1
```

The executable builds to the project root by default. Release files are written to `dist`. See the [contributing guide](CONTRIBUTING.md) for interactive Windows checks.

Open-source licensing is pending maintainer approval. See the [security policy](SECURITY.md) for private reporting.

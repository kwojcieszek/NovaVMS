# NovaVMS

NovaVMS is a cross-platform desktop VMS (Video Management System) for live viewing, recording playback
and control of IP cameras and network video recorders (NVR/DVR). The user interface is built with
[Avalonia UI](https://avaloniaui.net/) and video is rendered with [LibVLC](https://www.videolan.org/vlc/libvlc.html).

## Features

- **Live view** – grid layouts from 1×1 to 5×5, drag and drop cameras from the resource tree onto tiles,
  stream selection (main / sub / automatic).
- **Playback** – recording segment search with a timeline.
- **PTZ** – camera movement control and presets.
- **Device management** – add, edit, synchronize channels, check reachability.
- **Device discovery** on the local network (ONVIF WS-Discovery).
- **Camera groups** – custom organization of cameras in the resource tree.
- **Favorite cameras** – star cameras for quick access in the Favorites section at the top of the tree.
- **Device import**:
  - from CSV files (automatic delimiter detection, English and Polish column headers),
  - from iVMS-4200 backups (no vendor libraries required).
- **Localization** – English and Polish.
- **Themes** – light, dark or follow the system.

## Supported protocols

| Protocol      | Description                                                              |
|---------------|--------------------------------------------------------------------------|
| ISAPI         | HTTP/HTTPS API of Hikvision devices                                      |
| ONVIF         | Standard IP camera protocol (SOAP), including device discovery           |
| Hikvision SDK | Native Hikvision protocol (port 8000) through the vendor's HCNetSDK      |

## Solution structure

```
src/
├── NovaVMS.sln
├── NovaVMS.App/             # Avalonia application (MVVM): views, view models, services
├── NovaVMS.Core/            # Domain models, abstractions, exceptions, translations (Resources/Strings/*.json)
├── NovaVMS.Infrastructure/  # Configuration storage (JSON), CSV and iVMS-4200 import
└── NovaVMS.Protocols/       # Device clients: ISAPI, ONVIF, HCNetSDK, RTSP
```

Project dependencies:

```
NovaVMS.App ──► NovaVMS.Protocols ──► NovaVMS.Core
            ──► NovaVMS.Infrastructure ──► NovaVMS.Core
```

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows or macOS (LibVLC binaries are included through NuGet packages)
- Linux – LibVLC from the system packages, e.g.:
  ```bash
  sudo apt install vlc libvlc-dev
  ```
- *(optional)* **Hikvision HCNetSDK** – required only for the "Hikvision SDK" protocol.
  The library is proprietary and is not distributed with the application. Download it from
  hikvision.com (Support → Download → SDK → Device Network SDK) and place it in the `hcnetsdk`
  folder next to the application, or select its location in the settings.

## Building and running

```bash
cd src
dotnet build NovaVMS.sln
dotnet run --project NovaVMS.App
```

### Publishing

```bash
# Windows
dotnet publish NovaVMS.App -c Release -r win-x64

# macOS (Apple Silicon / Intel) – can also be built on a Windows machine
dotnet publish NovaVMS.App -c Release -r osx-arm64
dotnet publish NovaVMS.App -c Release -r osx-x64
```

## Application data

The configuration (`config.json`) and logs (`logs/`) are stored in the user's profile:

| OS      | Location                                     |
|---------|----------------------------------------------|
| Windows | `%APPDATA%\NovaVMS`                          |
| macOS   | `~/Library/Application Support/NovaVMS`      |
| Linux   | `~/.config/NovaVMS`                          |

The `NOVAVMS_DATA_DIR` environment variable overrides this location (e.g. portable use, testing).

Settings in `config.json` are encrypted for the current system account:

- Windows: DPAPI (`CurrentUser`), compatible with existing encrypted settings.
- macOS: AES-256-GCM with a random key in the user's login Keychain.
- Linux: AES-256-GCM with a random key in Secret Service via `libsecret-1.so.0`.
  Install `libsecret-1-0` and a Secret Service provider, such as GNOME Keyring or a compatible KDE
  service (e.g. `sudo apt install libsecret-1-0 gnome-keyring` on Ubuntu/Debian). The desktop D-Bus
  session and an unlocked keyring must be available. No plaintext fallback is used on headless systems.

The filename is retained, but the contents are binary. Existing JSON settings are automatically
encrypted after a successful load, without creating a plaintext backup. The system may ask permission
to unlock or access its credential store. Keys are not stored alongside the configuration, and missing
or inaccessible keys are never silently regenerated for an existing encrypted file.
Keep the original system profile/keychain to read the file. To transfer settings to another account or computer,
use the application's JSON export/import. Exported JSON files are not encrypted; including passwords
in an export also includes device secrets. A configuration that fails to load cannot be overwritten
until it has been restored and successfully loaded.

Managed exceptions, including caught exceptions, UI failures, background task failures and startup errors,
are written to `logs/errors-YYYYMMDD.log` with timestamps, process/thread IDs and stack traces.
If the application data directory cannot be written, logging falls back to `NovaVMS/logs` in the system
temporary directory, then to standard error. Fatal native crashes, forced process termination and
stack overflows may bypass managed exception handlers.

## Technologies

- [Avalonia UI 11](https://avaloniaui.net/) + Fluent Theme, DataGrid
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [LibVLCSharp](https://github.com/videolan/libvlcsharp)
- [Material.Icons.Avalonia](https://github.com/SKProCH/Material.Icons.Avalonia)
- Microsoft.Extensions.DependencyInjection
- Microsoft.Data.Sqlite

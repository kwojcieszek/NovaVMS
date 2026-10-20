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

## Technologies

- [Avalonia UI 11](https://avaloniaui.net/) + Fluent Theme, DataGrid
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [LibVLCSharp](https://github.com/videolan/libvlcsharp)
- [Material.Icons.Avalonia](https://github.com/SKProCH/Material.Icons.Avalonia)
- Microsoft.Extensions.DependencyInjection
- Microsoft.Data.Sqlite

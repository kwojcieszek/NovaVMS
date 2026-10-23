# NovaVMS

NovaVMS is a cross-platform desktop VMS (Video Management System) for live viewing, recording playback
and control of IP cameras and network video recorders (NVR/DVR). The user interface is built with
[Avalonia UI](https://avaloniaui.net/) and video is rendered with [LibVLC](https://www.videolan.org/vlc/libvlc.html).

## Features

- **Live view** – standard and widescreen layouts with up to 64 cameras, drag and drop cameras from the resource tree onto tiles,
  stream selection (main / sub / automatic).
- **Saved views** – create views from the live preview; camera placement, empty tiles and layout changes
  save automatically. The first saved view opens at startup. Right-click a view to rename or delete it,
  or a camera inside it to remove that camera from the view.
- **Playback** – recording segment search with a timeline.
  Adjacent recording files appear as continuous coverage, with real recording gaps preserved.
  Shuttle forwards or backwards at 2×, 4×, 8× or 16× using successive time previews, or play normally at 1×.
  Export a date/time range to MP4 in a chosen folder, with progress and cancellation. Continuous coverage
  produces one file; real gaps split exported files. Export runs independently of the preview.
  Uses the recorder's selected protocol: native HCNetSDK search and playback, ISAPI search with RTSP,
  or ONVIF Profile G search and replay. ONVIF playback requires the device to expose Profile G services
  and identify the recordings belonging to the selected camera.
- **PTZ** – camera movement control and presets.
- **Device management** – add, edit, synchronize channels, check reachability.
- **Secondary device address** – optionally configure another host with separate control and RTSP ports,
  using the same credentials.
  The first camera use checks the primary control port and then the secondary if needed. The chosen host
  is shared by all recorder channels until the application closes or connection settings change.
  If neither responds, the primary host is selected. Synchronization can try both before the first camera use
  without fixing the session's choice. The device table shows the selected address.
- **Device discovery** on the local network (ONVIF WS-Discovery).
- **Camera groups** – custom organization of cameras in the resource tree.
- **Favorite cameras** – star cameras for quick access in the Favorites section at the top of the tree.
- **Device import**:
  - from CSV files (automatic delimiter detection, English and Polish column headers),
  - devices and camera groups from iVMS-4200 backups (no vendor libraries required), replacing the
    existing devices and groups after confirmation.
- **Settings in a database** – share devices, groups and settings between computers through PostgreSQL
  or MySQL/MariaDB, with automatic checking, automatic or manual sending and a version history
  (see [Settings database](#settings-database)).
- **Localization** – English and Polish.
- **Themes** – light, dark or follow the system.
- **Application updates (Windows)** – automatic checks at startup and every six hours, or manual checks
  in Settings → General. Downloaded ZIP updates are applied by a separate helper after every NovaVMS
  process from the installation directory exits; the application then restarts. Failed file replacements
  restore the previous files. User settings and files absent from the package are preserved.

## Hosting application updates

In Settings → General → Application updates, enter the URL of your release directory and optionally
enable automatic checks. Installation is started explicitly with **Download and install**.
The separate **Automatically download new versions** option checks at startup and every six hours,
then downloads and validates a new package in the background. The downloaded package is reused for
installation, including after an application restart. A confirmation dialog appears as soon as the
package is ready. Declining keeps the package for **Install downloaded version** and suppresses
repeated automatic prompts for the same package until the next application start.

Build a Windows update package (including its `updater` folder):

```powershell
./build/Build-Update.ps1 -Version 2026.10.24
```

The script produces one `novavms_2026_10_24.zip` and `latest.json` in `build/out`.
Upload both files to the same server directory. The version defaults to `ReleaseDate` from
`src/Directory.Build.props` and must be
newer than the installed version. It performs one framework-dependent Release publish with
`PlatformTarget=AnyCPU`, without a runtime identifier or separate processor builds.
Files are placed directly at the ZIP root, including the `updater` folder.
The script excludes `hcnetsdk`, `libvlc`, and `runtimes` directories at every level, along with
debug symbols, logs and user settings. These existing dependencies remain in place during updates.
The target computer needs .NET 10 and native dependencies matching its process architecture;
AnyCPU applies to managed assemblies. Native executable launchers follow the build host.
The release date sets the application version and package filename.
`-OutputDirectory` changes the output directory.

Example `latest.json`:

```json
{
  "version": "2026.10.24",
  "file": "novavms_2026_10_24.zip"
}
```

The release script additionally includes `sha256`; when supplied, the application verifies it before
closing. A server directory listing can be used instead of `latest.json` if directory browsing is enabled.
Without either a manifest or a directory listing, a static server cannot tell the application which ZIP
is newest. A direct `latest.json` URL or a versioned ZIP URL can also be configured.

The ZIP contains the application at its root (one enclosing directory
is also accepted), with no user configuration or logs. Installation does not kill a process forcibly: if an instance
does not exit within 90 seconds, application files are left untouched. Updates require write access to
the installation directory. Download, backup and installer logs are retained in `%TEMP%/NovaVMS-updates`.

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

The application version is its release date in `yyyy.MM.dd` format (for example,
`2026.10.21`). Set `ReleaseDate` in `src/Directory.Build.props` for each release,
or pass `-p:ReleaseDate=2026.10.22` to `dotnet publish`. The same date appears in
the application header, window title and product version metadata. Rebuilding a
release preserves its version; file and assembly versions add a trailing `.0`.

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
On Windows, a managed startup or main-loop failure also displays an error dialog with the log locations.
LibVLC hardware decoding is disabled on Windows after reproducing a native D3D11 decoder access
violation when stopping a recording. This increases CPU use, especially with many high-resolution streams.
Recordings use a separate LibVLC instance with frame dropping disabled and paced SDK input, so a fast
recorder transfer neither races through the footage nor leaves only the first frame visible.
Shutdown waits for active device synchronization, PTZ commands and queued connection cleanup before
releasing the native SDK; cancelling a call alone does not mean its native work has finished.

## Settings database

Settings → Synchronization stores the configuration in a shared PostgreSQL or MySQL/MariaDB database.
After "Save and connect" the application creates its tables (`novavms_settings_meta`,
`novavms_settings_versions`) if they do not exist; the database user needs `CREATE TABLE`, `SELECT`,
`INSERT` and `DELETE` rights.

- Every version is compressed and encrypted with AES-256-GCM using a key derived from the encryption
  password (PBKDF2-SHA256, 600 000 iterations, random salt stored in the database). Device passwords are
  included, so all computers using the database must enter the same encryption password; a wrong password
  is detected on connection. The database only sees ciphertext, the date, computer name, IP address,
  system user and application version of each version.
- Automatic checking (default every 30 minutes, also at startup) applies a newer version sent from another
  computer. Automatic sending uploads the configuration a few seconds after each local change; unchanged
  settings do not create a new version. Settings can also be sent and checked manually.
- The newest *N* versions are kept (default 20). Any listed version can be applied; with automatic sending
  enabled an archived version is also sent again as the newest one.
- The connection (`sync.json`, encrypted like `config.json`) and the Hikvision SDK folder remain local to
  each computer.

## Technologies

- [Avalonia UI 11](https://avaloniaui.net/) + Fluent Theme, DataGrid
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [LibVLCSharp](https://github.com/videolan/libvlcsharp)
- [Material.Icons.Avalonia](https://github.com/SKProCH/Material.Icons.Avalonia)
- Microsoft.Extensions.DependencyInjection
- Microsoft.Data.Sqlite

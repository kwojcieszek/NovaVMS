# NovaVMS Mobile

Mobile Avalonia UI application with iOS and Android entry points. All projects
live directly in `src` and are part of the single `src/NovaVMS.sln` solution.

```text
src/
  NovaVMS.Mobile/          # Shared mobile application (MVVM)
    Localization/          #   {l:Tr Key} markup extension (the localizer is in NovaVMS.Presentation)
    Models/                #   Settings and option lists
    Native/                #   Hardware decoder video player compiled by the platform heads
    Services/              #   Encrypted device store, settings store, device connections
    ViewModels/            #   One view model per page or list item
    Views/                 #   AXAML views, one per file
  NovaVMS.Mobile.Android/  # Android startup and native services
  NovaVMS.Mobile.iOS/      # iPhone/iPad startup and native services
  NovaVMS.Mobile.Tests/    # Mobile application tests
```

## Navigation

- **Cameras** – the main page is structured like the desktop resource tree: Favorites, saved
  Views and camera Groups. Opening one plays live previews of all its cameras (sub stream,
  without sound); tapping a preview opens the camera full screen in landscape.
  The back button above the page (and the Android system back button) returns
  camera → group → main page. Tapping the picture twice switches full screen: a camera opened from the previews
  returns to them, any other camera shows its page again. A single tap on a full screen picture shows the
  translucent playback menu: live/recordings, the recording day and a 24 hour bar of its recordings, where a
  tap plays from that time. Searching lists matching cameras directly.
- **Devices** – device list and form with the desktop fields (primary and optional protocol,
  ports, secondary address, credentials, stream key) and channel download.
- **Settings** – the pages of the desktop settings window except software updates:
  General, Camera groups, Devices, Users and Synchronization.

## Configuration and synchronization

The phone keeps the same files as the desktop, with the same names and formats
(`AppDataPaths` in `NovaVMS.Infrastructure`): `config.json` (devices, groups, views, users,
settings), `sync.json` (settings database connection) and `local.json` (settings of this
device only). The desktop encrypts them for the computer account, the phone with the
Android Keystore / iOS Keychain key, so they move between devices through the settings
database or an export file, never by copying. Synchronization uses the shared
PostgreSQL / MySQL stores of `NovaVMS.Infrastructure` and behaves like the desktop: automatic
checking and sending, version history and restoring an archived version. "This phone"
settings (optional protocol) stay in `local.json` and are never synchronized. Device lists
saved by earlier mobile versions (`devices.enc`) are migrated once and get device groups.

The Hikvision SDK is not available on phones; a device using it is opened through its optional
ISAPI/ONVIF connection when it has one.

## Look and translations

- `App.axaml` uses the same palette, accent colour and brush names as
  `NovaVMS.App/App.axaml`, and the dark theme by default.
- The Android launcher icons and the iOS app icon are generated from the desktop icon
  `NovaVMS.App/Assets/novavms.png`. The interface has no title bar; pages start with their title.
- Interface texts come from the shared catalog `NovaVMS.Core/Resources/Strings/*.json`
  (keys `Mobile_*` plus shared `Common_*`, `Theme_*`, `Language_*` keys). Views use
  `{l:Tr Key}`, code uses `Localizer.Text` / `Localizer.Format`, exactly as in the
  desktop application. Language and theme are synchronized settings, as on the desktop.
- `NovaVMS.Presentation` holds what both applications share: the localizer and the list of interface
  languages, the option lists of the settings, the settings database synchronization and its form.
  It has no Avalonia types, because the desktop and the phone use different Avalonia versions.
- Interface languages: English, Polish, Chinese (simplified), Hindi, Spanish, Arabic and French. A new text
  is added to every `Resources/Strings/*.json`; a test checks that all catalogs have the same keys and placeholders.

## Video

Phones play video without LibVLC: `NovaVMS.Protocols/Rtsp` receives the RTSP stream over TCP on the
thread pool and hands every H.264/H.265 picture to the hardware decoder of the platform
(iOS `AVSampleBufferDisplayLayer`, Android `MediaCodec` rendering into a `TextureView`). The interface
thread only places the native view, so connecting cameras never blocks it. LibVLC was replaced
because its RTSP library (live555) waited about 10 seconds per stream for a multicast test that
iOS does not allow. Sound is not played, and streams of the Hikvision SDK, which need the desktop decoders, are not
shown. ISAPI/ONVIF video encrypted by a Hikvision recorder is decrypted on the phone (AES-128) with the
stream key from the device settings, before it reaches the hardware decoder.
`NovaVMS.Mobile/Native` is excluded from the shared project and compiled by the Android and iOS heads.

Mobile views are independent of `NovaVMS.App/Views`. Reuse the domain and
protocol libraries through project references; do not reference the desktop
executable or reuse its windows.

Building the whole solution requires the .NET `android` and `ios` workloads.

Run on an Android emulator or device:

```bash
cd src
dotnet build NovaVMS.Mobile.Android -t:Run
```

An iPhone installation package requires building and signing on a Mac with Xcode
and the appropriate Apple provisioning profile.

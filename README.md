<p align="center">
  <img src="assets/logo-256.png" width="150" alt="SonosDesk logo" />
</p>

<h1 align="center">SonosDesk</h1>

<p align="center">
  <b>Control your Sonos speakers from the Windows desktop – right next to the clock.</b><br/>
  Open source · local control without a cloud account · no telemetry
</p>

<p align="center">
  <a href="https://github.com/adriankrypton/sonosdesk/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/adriankrypton/sonosdesk?label=download&sort=semver"></a>
  <a href="https://github.com/adriankrypton/sonosdesk/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/adriankrypton/sonosdesk/actions/workflows/ci.yml/badge.svg"></a>
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4">
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-green"></a>
</p>

<p align="center">
  <b>English</b> · <a href="README.de.md">Deutsch</a>
</p>

---

SonosDesk finds your Sonos speakers automatically and lets you play, pause, skip, change the volume,
group rooms and start radio stations – from a modern Windows 11 style window **or directly from the
notification area**, without picking up your phone.

> SonosDesk is an independent community project. It is not affiliated with, endorsed or supported by
> Sonos, Inc. "Sonos" is a trademark of Sonos, Inc.

## Contents

- [Release notes](#release-notes)
- [Highlights](#highlights)
- [Download and install](#download-and-install)
- [First start](#first-start)
- [Everyday use](#everyday-use)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Troubleshooting](#troubleshooting)
- [Privacy and network](#privacy-and-network)
- [Supported speakers and limitations](#supported-speakers-and-limitations)
- [For developers](#for-developers)
- [License](#license)

## Release notes

### 0.1.0 – 18 October 2026 · first public release

- Installer for Windows 10/11 (per user, no admin rights) plus a portable single-file `.exe`
- Automatic speaker discovery, manual IP addresses for VPN/guest Wi-Fi/VLAN
- Playback with seek bar, shuffle, repeat, crossfade – also for **Spotify Connect**
- Volume per room and group; **volume profiles fade smoothly** instead of jumping
- Groups, group composer, colored areas, party mode, Ctrl multi-selection
- Queue, sleep timer, sound per room (bass, treble, loudness, night sound, speech enhancement)
- **Speaker info** via right-click: IP address, host name, model, software/hardware version, MAC, serial number
- Internet radio (Radio Browser) with *My stations*, folders, custom logos and own stream URLs
- Tray flyout and menu, media keys, Windows media overlay, hotkeys, quick actions, schedules, 8 languages
- Update hint when a new release is published

All versions and details: [CHANGELOG.md](CHANGELOG.md) · downloads: [Releases](https://github.com/adriankrypton/sonosdesk/releases)

## Highlights

**Everyday control**
- Play/pause, next/previous, seek within the track, shuffle, repeat, crossfade
- Title, artist, album and cover – also for music started with **Spotify Connect** from the Spotify app
- Volume per room and for whole groups, mute
- **Volume profiles** such as "Evening" or "Party" – the volume fades over an adjustable time
- **Queue:** see what comes next, play from any track, remove tracks, clear the queue
- **Sleep timer** from 15 to 120 minutes with countdown
- **Sound per room:** bass, treble, loudness – and night sound / speech enhancement on soundbars
- **Speaker info:** right-click a speaker for IP address, host name, model, versions, MAC and serial number

**Rooms and groups**
- Group and ungroup rooms with one click, **party mode** (all rooms play the same)
- **Group composer:** pick speakers, a favorite and a volume → *Create group*
- **Areas:** named, colored room combinations such as "Downstairs", grouped with one click
- Click a group to play everywhere in it, a single speaker to play only there, or Ctrl+click several rooms

**Music**
- Your **Sonos favorites** ("My Sonos")
- **Internet radio** with thousands of stations from the open [Radio Browser](https://www.radio-browser.info)
  directory: search, genres, countries, *My stations* with folders, custom logos and your own stream URLs
- For radio SonosDesk shows the **song that is currently on air** instead of cryptic stream names
- Stations you start appear in the official Sonos app as "Station · via SonosDesk"

**Windows integration**
- **Tray icon:** left click opens a compact flyout, right click a full menu – SonosDesk keeps running
  when the window is closed
- **Media keys** and the Windows media overlay (next to the volume flyout) control the active room
- Global hotkeys, quick actions, schedules (e.g. "weekdays 7:00 – radio in the kitchen")
- Light/dark theme, start with Windows, start page cards can be rearranged via drag & drop
- **8 languages:** English, Deutsch, Français, Español, Italiano, Nederlands, Polski, Русский
- **Update hint** when a new version is published on GitHub

The detailed support matrix (including what has been verified on real hardware) is in
[docs/features.md](docs/features.md).

## Download and install

Open the [latest release](https://github.com/adriankrypton/sonosdesk/releases/latest) and choose:

| File | For |
| --- | --- |
| `SonosDesk-Setup-X.Y.Z-win-x64.exe` | **Recommended.** Installs SonosDesk for your user account (no admin rights), adds a Start menu entry and optionally a desktop icon and "Start with Windows". |
| `SonosDesk-X.Y.Z-win-x64-portable.exe` | Portable single file – run it from any folder, nothing is installed. |
| `SHA256SUMS.txt` | Checksums to verify the downloads (`Get-FileHash <file>` in PowerShell). |

Requirements: Windows 10 (version 2004 or newer) or Windows 11, 64-bit, and Sonos speakers in the
same home network (S1 or S2 system). No separate .NET runtime is needed.

> **Windows SmartScreen:** SonosDesk is not code-signed yet, so Windows may show
> "Windows protected your PC". Click **More info → Run anyway**.

**Updates:** SonosDesk checks for a newer release on start and shows a notice with *Download*, *Later*
and *Skip this version*. Run the new setup – it updates the existing installation and keeps your
settings. The check can be switched off under *Settings → Updates*.

**Uninstall:** *Windows Settings → Apps → Installed apps → SonosDesk → Uninstall*. The uninstaller asks
whether your settings (`%LOCALAPPDATA%\SonosDesk`) should be deleted too. For the portable version,
switch off *Start with Windows* in the settings, exit SonosDesk and delete the file.

## First start

1. Start SonosDesk. When Windows asks about the firewall, allow access for **private networks**.
2. Your rooms appear on the left after a few seconds. The last used room is selected on the next start.
3. Nothing found? Right-click the empty room list → **Add speaker by IP address …** and enter the IP of
   any speaker (Sonos app → *Settings → System → About My System*). One speaker is enough, the other
   rooms are found through it.

The UI language follows Windows; you can change it under *Settings*.

## Everyday use

| Where | What you can do |
| --- | --- |
| Room list (left) | Click a room or group to control it · click a single speaker of a group to play only there · **Ctrl+click** several rooms, then start a favorite or station – exactly those rooms are grouped · **right-click → Info** for the speaker details |
| Start page | Now playing with seek bar, crossfade and sleep timer · favorites · room volumes · queue · sound · grouping. Drag the ☰ handle of a card to reorder |
| *Radio* | Browse and search stations, ☆ saves them to *My stations*; right-click for folders and logos; *Custom station* adds your own stream URL |
| *Create group* | Choose speakers, optionally a favorite or station and a volume, save as a quick action if you like |
| *Areas* | **+** creates a named, colored room combination; one click groups those rooms |
| Tray icon | Left click: flyout · right click: rooms, favorites, stations, areas, volume profiles, party mode, sleep timer, quick actions, exit · double click: main window |
| *Settings* | Theme, language, start with Windows, media keys, discovery, volume step and **fade time of volume profiles**, profiles, quick actions, schedules, hotkeys, updates |

Closing the main window keeps SonosDesk in the notification area (can be changed in the settings).
To end it completely, use **Exit** in the tray menu.

## Keyboard shortcuts

| Where | Shortcut |
| --- | --- |
| Anywhere (configurable) | `Ctrl+Alt+P` play/pause · `Ctrl+Alt+←/→` previous/next · `Ctrl+Alt+↑/↓` volume |
| Media keys | play/pause, next, previous of the active room (can be switched off) |
| Main window | `F5` search again · `Ctrl+Space` play/pause · `Ctrl+←/→` track · `Ctrl+↑/↓` volume · `Ctrl+M` mute · `Ctrl+,` settings |

## Troubleshooting

**No speakers are found**
- PC and speakers must be in the same network – guest Wi-Fi, VPNs and separate VLANs often block discovery.
- Allow SonosDesk for private networks in the Windows firewall (UDP 1900 and TCP 1400 in the local network).
- Add a speaker by IP address (right-click the room list, or *Settings → Network & discovery*).
- More help: [docs/discovery.md](docs/discovery.md).

**The media keys control the wrong app** – Windows sends them to the most recently active media app.
Switch off *Use Windows media keys and media overlay* in the settings if you prefer other apps.

**A command fails** – SonosDesk shows the error at the top of the window. Logs for bug reports are under
*Settings → About SonosDesk → Open logs* (`%LOCALAPPDATA%\SonosDesk\logs`). They contain no passwords
or tokens.

**Found a bug or have an idea?** Open an [issue](https://github.com/adriankrypton/sonosdesk/issues).

## Privacy and network

SonosDesk has no telemetry and needs no account. It talks to your speakers in the local network
and only connects to the internet for:

| Connection | Purpose | Optional |
| --- | --- | --- |
| Speakers, TCP 1400 / UDP 1900 (local) | discovery, control, metadata, covers | – |
| Local DNS | host name in the speaker info | only when you open *Info* |
| Cover and logo servers (HTTPS) | album art and station logos | – |
| `*.api.radio-browser.info` | radio directory | used only on the radio page |
| Radio stream servers | reading the current song of a station (ICY metadata) | yes, in the settings |
| `api.github.com` | checking for a new release | yes, in the settings |
| `api.sonos.com`, `api.ws.sonos.com` | Sonos Control API, only if you enable the cloud backend | yes |

Settings are stored in `%LOCALAPPDATA%\SonosDesk`; secrets (Control API tokens) are encrypted with
Windows DPAPI and never written to the settings file. See [SECURITY.md](SECURITY.md).

## Supported speakers and limitations

Any Sonos player with local control works, including One/Era, Five, Beam/Arc/Ray, Port/Amp/Connect;
Move/Roam only while on Wi-Fi. Boost/Bridge are ignored because they cannot play.

Current limitations:
- State is refreshed by polling (every 2 s for the visible room); push events are planned.
- No music library browsing or alarms yet.
- The optional **Sonos Control API** backend is experimental, needs your own developer credentials
  and offers fewer features (no favorites, queue, sleep timer, sound settings). See
  [docs/control-api.md](docs/control-api.md).
- Many commands are covered by automated tests with simulated speakers; which ones were also verified
  on real hardware is listed in [docs/features.md](docs/features.md).

## For developers

SonosDesk is a C# / .NET 10 WPF application (MVVM with CommunityToolkit.Mvvm, Microsoft.Extensions
DI/logging, xUnit). Building needs Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet restore SonosDesk.sln
dotnet build SonosDesk.sln -c Release
dotnet test --solution SonosDesk.sln -c Release      # no speakers needed
dotnet run --project src/SonosDesk.App -c Release
```

Installer (needs [Inno Setup 7](https://jrsoftware.org/isinfo.php)); publishes the app and writes
`artifacts/installer/SonosDesk-Setup-<version>-win-x64.exe`:

```powershell
./tools/build-installer.ps1
```

| Folder | Content |
| --- | --- |
| `src/SonosDesk.Core` | domain models, interfaces, quick actions, settings, localization (no WPF) |
| `src/SonosDesk.Infrastructure` | SSDP discovery, UPnP/SOAP, Control API, radio directory, persistence |
| `src/SonosDesk.Windows` | tray icon, global hotkeys, autostart, DPAPI |
| `src/SonosDesk.App` | WPF views and view models |
| `installer/` | Inno Setup script |
| `tests/` | deterministic xUnit tests with simulated network responses |

**Releases:** CI builds, tests and packages the app and the installer on every push. To publish a
version, raise `<Version>` in `Directory.Build.props`, add a section to `CHANGELOG.md`, then either push
a tag such as `v0.2.0` or run the *Release* workflow in the Actions tab. It creates a GitHub release with
setup, portable exe, zip and checksums; the changelog section becomes the release text.

- UI texts live in `src/SonosDesk.Core/Localization/fragments/*.{lang}.json`; run
  `python tools/merge-strings.py` after editing. New translations are welcome.
- Command line options: `--minimized` (start in the tray), `--autostart` (used by the Windows autostart entry).

Documentation: [architecture](docs/architecture.md) · [discovery](docs/discovery.md) ·
[features](docs/features.md) · [quick actions & extensibility](docs/extensibility.md) ·
[Control API setup](docs/control-api.md) · [contributing](CONTRIBUTING.md) ·
[security](SECURITY.md) · [third-party notices](THIRD-PARTY-NOTICES.md)

## License

[MIT](LICENSE) © [adriankrypton](https://github.com/adriankrypton) and SonosDesk contributors.
Radio station data is provided at runtime by the community project [Radio Browser](https://www.radio-browser.info).

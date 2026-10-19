<p align="center">
  <img src="assets/logo-256.png" width="150" alt="SonosDesk logo" />
</p>

<h1 align="center">SonosDesk</h1>

<p align="center">
  <b>Control your Sonos speakers from the desktop – on Windows and Mac, right next to the clock.</b><br/>
  Open source · local control without a cloud account · no telemetry
</p>

<p align="center">
  <a href="https://github.com/adriankrypton/sonosdesk/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/adriankrypton/sonosdesk?label=download&sort=semver&include_prereleases"></a>
  <a href="https://github.com/adriankrypton/sonosdesk/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/adriankrypton/sonosdesk/actions/workflows/ci.yml/badge.svg"></a>
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4">
  <img alt="macOS 12+" src="https://img.shields.io/badge/macOS-12%2B%20%C2%B7%20Apple%20Silicon%20%26%20Intel-000000">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4">
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-green"></a>
</p>

<p align="center">
  <b>English</b> · <a href="README.de.md">Deutsch</a>
</p>

---

SonosDesk finds your Sonos speakers automatically and lets you play, pause, skip, change the volume,
group rooms and start radio stations – from the app window **or directly from the notification area
(Windows) or the menu bar (Mac)**, without picking up your phone. Pioneer AV receivers can be
controlled like a Sonos room, too.

> SonosDesk is an independent community project. It is not affiliated with, endorsed or supported by
> Sonos, Inc. "Sonos" is a trademark of Sonos, Inc.

## Contents

- [Release notes](#release-notes)
- [Windows and Mac](#windows-and-mac)
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

### 0.1.1 – 19 October 2026 · pre-release

- **SonosDesk for Mac** (Apple Silicon and Intel, macOS 12 or newer) with menu bar icon
- **Pioneer AV receivers (VSX)** work like Sonos rooms: room list, now playing, play/stop, volume 0–100,
  mute, sound (listening mode, bass/treble), *My stations*; right-click → *Info*, on/off
- Internet radio on the receiver; its display shows **"Station - via SonosDesk"**, every 10 s alternating with the song
- Group the receiver with a Sonos room: it plays the same internet radio station and starts/stops with it
- Rescan shows the number of Sonos devices, other media players (DMR) and other UPnP devices
- *My stations* can be renamed and deleted

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

## Windows and Mac

Both apps use the same discovery, playback, radio and receiver code. The Windows app is the original
WPF app; the Mac app is new in 0.1.1 and does not have every extra yet.

| | Windows | Mac |
| --- | :---: | :---: |
| Rooms, groups, now playing, seek, shuffle/repeat, crossfade, sleep timer | ✓ | ✓ |
| Volume per room and group, mute, sound (bass, treble, loudness, night sound, speech) | ✓ | ✓ |
| Favorites, queue, party mode, grouping/ungrouping rooms | ✓ | ✓ |
| Internet radio, *My stations* (rename, delete, own logo, own stream URL) | ✓ | ✓ |
| Pioneer receivers (radio, volume, sound, listening mode, coupling with a room) | ✓ | ✓ |
| Speaker info (right-click → *Info*) | ✓ | ✓ |
| Tray / menu bar menu, global hotkeys, media keys, start at login, light/dark | ✓ | ✓ |
| Update hint for new releases | ✓ | ✓ |
| Group composer, areas, station folders | ✓ | – |
| Editing volume profiles, quick actions and schedules | ✓ | – ¹ |
| Tray flyout, Windows media overlay, rearranging start page cards | ✓ | – |
| 8 languages | ✓ | ✓ |

¹ Schedules that already exist in the settings (for example created on Windows) still run on the Mac.

## Highlights

**Everyday control**
- Play/pause, next/previous, seek within the track, shuffle, repeat, crossfade
- Title, artist, album and cover – also for music started with **Spotify Connect** from the Spotify app
- Volume per room and for whole groups, mute
- **Volume profiles** such as "Evening" or "Party" – the volume fades over an adjustable time (Windows)
- **Queue:** see what comes next, play from any track, remove tracks, clear the queue
- **Sleep timer** from 15 to 120 minutes with countdown
- **Sound per room:** bass, treble, loudness – and night sound / speech enhancement on soundbars
- **Speaker info:** right-click a speaker for IP address, host name, model, versions, MAC and serial number

**Rooms and groups**
- Group and ungroup rooms with one click, **party mode** (all rooms play the same)
- **Group composer:** pick speakers, a favorite and a volume → *Create group* (Windows)
- **Areas:** named, colored room combinations such as "Downstairs", grouped with one click (Windows)
- Click a group to play everywhere in it, a single speaker to play only there, or Ctrl+click several rooms (Windows)

**Music**
- Your **Sonos favorites** ("My Sonos")
- **Internet radio** with thousands of stations from the open [Radio Browser](https://www.radio-browser.info)
  directory: search, genres, countries, *My stations* with custom logos and your own stream URLs
- For radio SonosDesk shows the **song that is currently on air** instead of cryptic stream names
- Stations you start appear in the official Sonos app as "Station · via SonosDesk"

**AV receivers (Pioneer VSX)**
- Add a receiver via right-click on the room list; it then works like a Sonos room (play/stop, volume,
  mute, listening mode, bass/treble, *My stations*, info)
- Internet radio through the receiver's network player – the display shows "Station - via SonosDesk"
- Group it with a Sonos room: the receiver plays along whenever the room plays internet radio

**Desktop integration**
- **Windows:** tray icon – left click opens a compact flyout, right click a full menu; media keys and the
  Windows media overlay control the active room
- **Mac:** menu bar icon with now playing, play/pause, next/previous, volume, rooms and *My stations*
- SonosDesk keeps running when the window is closed
- Global hotkeys, start at login, light/dark theme
- **8 languages:** English, Deutsch, Français, Español, Italiano, Nederlands, Polski, Русский
- **Update hint** when a new version is published on GitHub

The detailed support matrix (including what has been verified on real hardware) is in
[docs/features.md](docs/features.md).

## Download and install

Open the [releases](https://github.com/adriankrypton/sonosdesk/releases) and choose:

| File | For |
| --- | --- |
| `SonosDesk-Setup-X.Y.Z-win-x64.exe` | **Windows, recommended.** Installs SonosDesk for your user account (no admin rights), adds a Start menu entry and optionally a desktop icon and "Start with Windows". |
| `SonosDesk-X.Y.Z-win-x64-portable.exe` | Windows, portable single file – run it from any folder, nothing is installed. |
| `SonosDesk-X.Y.Z-mac-arm64.dmg` | **Mac with Apple Silicon** (M1, M2, M3, M4 …). |
| `SonosDesk-X.Y.Z-mac-x64.dmg` | **Mac with Intel processor.** |
| `…-mac-arm64.zip`, `…-mac-x64.zip` | The same Mac app as a zip archive. |
| `SHA256SUMS.txt`, `SHA256SUMS-macos.txt` | Checksums to verify the downloads (`Get-FileHash <file>` in PowerShell, `shasum -a 256 <file>` in the Mac Terminal). |

All versions need Sonos speakers in the same home network (S1 or S2 system). No separate .NET runtime is needed.

### Windows

Requirements: Windows 10 (version 2004 or newer) or Windows 11, 64-bit.

> **Windows SmartScreen:** SonosDesk is not code-signed yet, so Windows may show
> "Windows protected your PC". Click **More info → Run anyway**.

**Uninstall:** *Windows Settings → Apps → Installed apps → SonosDesk → Uninstall*. The uninstaller asks
whether your settings (`%LOCALAPPDATA%\SonosDesk`) should be deleted too. For the portable version,
switch off *Start with Windows* in the settings, exit SonosDesk and delete the file.

### Mac

Requirements: macOS 12 Monterey or newer. Not sure which Mac you have? Apple menu → *About This Mac*:
"Chip Apple M…" = Apple Silicon (`arm64`), "Processor Intel …" = Intel (`x64`).

1. Open the `.dmg` and drag **SonosDesk** into **Applications**.
2. Start SonosDesk. The app is not notarized by Apple yet, so macOS blocks the first start: open
   **System Settings → Privacy & Security**, scroll down and click **Open Anyway** next to SonosDesk.
   (Alternative in Terminal: `xattr -dr com.apple.quarantine /Applications/SonosDesk.app`.)
3. Allow **"SonosDesk would like to find devices on your local network"** – without it no speakers are found.
   You can change this later under *System Settings → Privacy & Security → Local Network*.

SonosDesk then lives in the menu bar; closing the window keeps it running. Quit with **⌘Q** or via the
menu bar icon.

**Uninstall:** quit SonosDesk, drag it from *Applications* to the Trash and, if you like, delete the
settings in `~/Library/Application Support/SonosDesk`.

### Updates

SonosDesk checks for a newer release on start and shows a notice. On Windows run the new setup – it
updates the existing installation and keeps your settings. On the Mac replace the app in *Applications*
with the new one. The check can be switched off under *Settings → Updates*.

## First start

1. Start SonosDesk. When Windows asks about the firewall, allow access for **private networks**;
   on the Mac allow access to the local network.
2. Your rooms appear on the left after a few seconds. The last used room is selected on the next start.
3. Nothing found? Right-click the empty room list → **Add speaker by IP address …** and enter the IP of
   any speaker (Sonos app → *Settings → System → About My System*). One speaker is enough, the other
   rooms are found through it.

The UI language follows the system; you can change it under *Settings*.

## Everyday use

| Where | What you can do |
| --- | --- |
| Room list (left) | Click a room or group to control it · **right-click → Info** for the speaker details · right-click the list to add a speaker or receiver by IP address |
| Now playing | Seek bar, shuffle/repeat, crossfade and sleep timer · favorites · room volumes · queue · sound · grouping and party mode |
| *Radio* | Browse and search stations, ☆ saves them to *My stations*; rename, delete or give them your own logo; add your own stream URL |
| Receiver | Same page as a room: *My stations* to tap and play, volume, listening mode, bass/treble, coupling with a Sonos room |
| Tray icon (Windows) | Left click: flyout · right click: rooms, favorites, stations, areas, volume profiles, party mode, sleep timer, quick actions, exit · double click: main window |
| Menu bar icon (Mac) | Now playing, play/pause, next/previous, louder/quieter, mute, rooms, *My stations*, pause all, open, settings, quit |
| *Settings* | Theme, language, start at login, discovery and manual addresses, volume step, hotkeys, updates (Windows also: media overlay, profiles, quick actions, schedules) |

Windows only: click a single speaker of a group to play only there, **Ctrl+click** several rooms and then
start a favorite or station to group exactly those rooms, *Create group* and *Areas* pages, and dragging
the ☰ handle of a start page card to reorder it.

Closing the main window keeps SonosDesk running in the notification area or menu bar (can be changed in
the settings on Windows). To end it completely, use **Exit** in the tray menu or **⌘Q** on the Mac.

## Keyboard shortcuts

| Where | Windows | Mac |
| --- | --- | --- |
| Anywhere (configurable) | `Ctrl+Alt+P` play/pause · `Ctrl+Alt+←/→` previous/next · `Ctrl+Alt+↑/↓` volume | the same with `⌃ Control` + `⌥ Option` |
| Media keys | play/pause, next, previous of the active room (can be switched off) | the same (macOS asks for *Accessibility* permission) |
| Main window | `F5` search again · `Ctrl+Space` play/pause · `Ctrl+←/→` track · `Ctrl+↑/↓` volume · `Ctrl+M` mute · `Ctrl+,` settings | `F5` · `⌘Space` · `⌘←/→` · `⌘↑/↓` · `⌘,` settings · `⌘Q` quit |

## Troubleshooting

**No speakers are found**
- Computer and speakers must be in the same network – guest Wi-Fi, VPNs and separate VLANs often block discovery.
- Windows: allow SonosDesk for private networks in the Windows firewall (UDP 1900 and TCP 1400 in the local network).
- Mac: check *System Settings → Privacy & Security → Local Network* – SonosDesk must be switched on.
- Add a speaker by IP address (right-click the room list, or *Settings → Network & discovery*).
- More help: [docs/discovery.md](docs/discovery.md).

**The receiver does not play a station** – the receiver fetches the stream from SonosDesk on the computer.
Allow SonosDesk in the firewall (Windows: private networks; Mac: *System Settings → Network → Firewall*,
allow incoming connections), and enable network control ("Network Standby") on the receiver. SonosDesk
has to keep running while the receiver plays.

**The media keys control the wrong app** – the system sends them to the most recently active media app.
Switch off the media keys option in the settings if you prefer other apps.

**The Mac says "SonosDesk is damaged" or cannot be opened** – this is the Gatekeeper warning for apps
that are not notarized. Use *Open Anyway* in *Privacy & Security* or the `xattr` command from
[Mac](#mac).

**A command fails** – SonosDesk shows the error at the top of the window. Logs for bug reports are under
*Settings → About SonosDesk → Open logs* (Windows: `%LOCALAPPDATA%\SonosDesk\logs`; Mac:
`~/Library/Logs/SonosDesk` and `~/Library/Application Support/SonosDesk/logs`). They contain no
passwords or tokens.

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
| AV receiver → computer (local, random TCP port) | the receiver fetches internet radio streams from SonosDesk | only while a receiver plays a stream |
| AV receiver, TCP 8102 / 8080 (local) | Pioneer network control and media renderer | only with receivers |
| `api.sonos.com`, `api.ws.sonos.com` | Sonos Control API, only if you enable the cloud backend | yes |

On the Mac the window talks to a small SonosDesk service on the same computer; it only listens on
`127.0.0.1` and only accepts requests from the app (a random key per start).

Settings are stored in `%LOCALAPPDATA%\SonosDesk` (Windows) or `~/Library/Application Support/SonosDesk`
(Mac). Secrets (Control API tokens) are encrypted – with Windows DPAPI, on the Mac with a key in the
Keychain – and never written to the settings file. See [SECURITY.md](SECURITY.md).

## Supported speakers and limitations

Any Sonos player with local control works, including One/Era, Five, Beam/Arc/Ray, Port/Amp/Connect;
Move/Roam only while on Wi-Fi. Boost/Bridge are ignored because they cannot play.

Current limitations:
- State is refreshed by polling (every 2 s for the visible room); push events are planned.
- No music library browsing or alarms yet.
- The Mac app is new: see [Windows and Mac](#windows-and-mac) for what it does not have yet. It is not
  notarized by Apple yet.
- The optional **Sonos Control API** backend is experimental, needs your own developer credentials
  and offers fewer features (no favorites, queue, sleep timer, sound settings). See
  [docs/control-api.md](docs/control-api.md).
- Many commands are covered by automated tests with simulated speakers; which ones were also verified
  on real hardware is listed in [docs/features.md](docs/features.md).

## For developers

SonosDesk is written in C# / .NET 10. The Windows app is WPF (MVVM with CommunityToolkit.Mvvm,
Microsoft.Extensions DI/logging, xUnit). The Mac app is an [Electron](electron/README.md) interface that
starts `SonosDesk.Server`, a local .NET service built from the same Core and Infrastructure code
([API](docs/desktop-api.md), [architecture](docs/architecture.md)). Building needs the
[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0); the WPF app needs Windows.

**Windows app**

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

**Mac app** (on a Mac with .NET 10 SDK and Node.js 22; `npm start` also works on Windows for development)

```bash
dotnet build src/SonosDesk.Server
cd electron
npm ci
npm start                                                    # development, starts the server too
node scripts/publish-server.mjs --platform mac --arch x64,arm64
npx electron-builder --mac --x64 --arm64 --publish never     # dist/*.dmg and *.zip
```

| Folder | Content |
| --- | --- |
| `src/SonosDesk.Core` | domain models, interfaces, quick actions, settings, localization (no UI) |
| `src/SonosDesk.Infrastructure` | SSDP discovery, UPnP/SOAP, Control API, radio directory, receivers, persistence |
| `src/SonosDesk.Windows` | tray icon, global hotkeys, autostart, DPAPI |
| `src/SonosDesk.App` | WPF views and view models (Windows app) |
| `src/SonosDesk.Server` | local REST/WebSocket service for the Mac app (cross-platform) |
| `electron/` | Electron app for the Mac (menu bar, now playing, radio, receivers, settings) |
| `installer/` | Inno Setup script |
| `tests/` | deterministic xUnit tests with simulated network responses |

**Releases:** CI builds and tests both apps on every push and uploads the Windows installer and the
Mac `.dmg`/`.zip` files as artifacts. To publish a version, raise `<Version>` in `Directory.Build.props`,
add a section to `CHANGELOG.md`, then either push a tag such as `v0.2.0` or run the *Release* workflow in
the Actions tab. It creates a GitHub release with the Windows setup, portable exe, zip, the Mac apps and
checksums; the changelog section becomes the release text.

- UI texts live in `src/SonosDesk.Core/Localization/fragments/*.{lang}.json`; run
  `python tools/merge-strings.py` after editing. New translations are welcome.
- Windows command line options: `--minimized` (start in the tray), `--autostart` (used by the Windows autostart entry).

Documentation: [architecture](docs/architecture.md) · [discovery](docs/discovery.md) ·
[features](docs/features.md) · [quick actions & extensibility](docs/extensibility.md) ·
[Control API setup](docs/control-api.md) · [desktop API](docs/desktop-api.md) ·
[contributing](CONTRIBUTING.md) · [security](SECURITY.md) · [third-party notices](THIRD-PARTY-NOTICES.md)

## License

[MIT](LICENSE) © [adriankrypton](https://github.com/adriankrypton) and SonosDesk contributors.
Radio station data is provided at runtime by the community project [Radio Browser](https://www.radio-browser.info).

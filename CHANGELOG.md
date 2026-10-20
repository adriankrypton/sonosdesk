# Changelog

All notable changes to SonosDesk are listed here. Versions follow [Semantic Versioning](https://semver.org/).
The section of a version is used as the text of its GitHub release.

## [0.1.1] – 2026-10-20

### Added
- **SonosDesk for macOS** (Apple Silicon and Intel, macOS 12 or newer): an Electron app with rooms, now playing,
  favorites, queue, sound, grouping, radio and *My stations*, receivers, speaker info and settings, plus a menu
  bar icon. It runs `SonosDesk.Server`, a local .NET service built from the same code as the Windows app.
  The Mac app is not notarized yet (first start: *System Settings → Privacy & Security → Open Anyway*).
- **Pioneer AV receivers (VSX)** appear in the room list next to the Sonos rooms and work like them:
  now playing, play/stop, volume 0–100, mute, sound (listening mode, bass/treble), "My stations" to tap and play.
  Add a receiver via right-click on the room list → *Add receiver (Pioneer) by IP address* (with network search).
- **Internet radio on the receiver** through its network player (UPnP/DLNA). The front display shows
  "Station - via SonosDesk", alternating every 10 seconds with the current song.
- **Coupling with Sonos:** group the receiver with a Sonos room like rooms are grouped. When the room plays
  internet radio, the receiver plays the same station and follows start/stop (a delay of a few seconds is normal).
- **Speaker info** (right-click → *Info*) for receivers: IP address, host name, model, software version, control port.
- Volume profiles and quick actions can include receivers; volume changes fade like on Sonos.
- The rescan status shows how many Sonos devices, other media players (DMR) and other UPnP devices were found.
- "My stations" can be renamed and deleted.

### Notes
- SonosDesk has to keep running while the receiver plays a stream (it relays the stream). Allow SonosDesk for
  private networks in the Windows firewall.
- Spotify, the queue, TV and line-in of a Sonos room cannot be passed on to the receiver.

## [0.1.0] – 2026-10-18

First public release.

### Highlights
- **Installer** for Windows 10/11 (per user, no administrator rights) with Start menu entry, optional
  desktop icon and "Start with Windows"; a portable single-file `.exe` is available as well.
- **Automatic discovery** of Sonos speakers via SSDP on all network adapters, with manual IP addresses
  for VPN, guest Wi-Fi and VLAN setups.
- **Playback:** play/pause, next/previous, seek, shuffle, repeat, crossfade, cover, title and artist –
  also for music started with **Spotify Connect**.
- **Volume** per room and group, mute, **volume profiles that fade smoothly** (duration adjustable).
- **Rooms and groups:** group with one click, group composer, colored **areas**, **party mode**,
  precise targets (whole group, single speaker, Ctrl multi-selection).
- **Queue** (play from a track, remove, clear), **sleep timer**, **sound per room** (bass, treble,
  loudness, night sound and speech enhancement on soundbars).
- **Speaker info:** right-click a speaker → *Info* shows IP address, host name, model, software and
  hardware version, MAC address and serial number.
- **Internet radio** from the open Radio Browser directory, *My stations* with folders, custom logos
  and own stream URLs; the current song is shown instead of stream names.
- **Windows integration:** tray icon with flyout and menu, media keys and Windows media overlay, global
  hotkeys, quick actions and schedules, light/dark theme, 8 languages.
- **Update hint:** SonosDesk checks GitHub for a newer release on start (can be switched off).

### Known limitations
- State is polled; UPnP event subscriptions are planned.
- No music library browsing or alarms yet.
- The Sonos Control API (cloud) backend is experimental and offers fewer features than the local backend.

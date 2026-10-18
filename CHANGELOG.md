# Changelog

All notable changes to SonosDesk are listed here. Versions follow [Semantic Versioning](https://semver.org/).
The section of a version is used as the text of its GitHub release.

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

# Features and supported devices

Status legend: ✅ implemented and covered by automated tests · 🧪 implemented, experimental ·
⏳ planned · ❌ not supported.

"Verified on hardware" means the feature was exercised against a real speaker (Sonos One, S2,
software 97.2) during development. Everything else is tested with simulated responses only.

## Local UPnP backend (default)

| Feature | Status | Verified on hardware | Notes |
| --- | --- | --- | --- |
| SSDP discovery, rediscovery, lost/recovered devices | ✅ | yes | multiple adapters, manual addresses, topology learning |
| Rooms and groups (`GetZoneGroupState`) | ✅ | yes | stereo pairs/satellites hidden, bridges ignored |
| Playback state, title, artist, album, source | ✅ | yes | radio "now playing" text via `streamContent` |
| Album art | ✅ | yes | via player (`/getaa`) or HTTPS image URL |
| Play, pause, next, previous | ✅ | no | sent to the group coordinator |
| Shuffle / repeat | ✅ | no | only for queue-based sources |
| Player volume, mute | ✅ | read: yes | `RenderingControl`; fixed line-out volume is detected |
| Group volume, group mute | ✅ | no | `GroupRenderingControl` with `SnapshotGroupVolume` |
| Join / leave / create group with exact members / dissolve | ✅ | no | `SetAVTransportURI x-rincon:` / `BecomeCoordinatorOfStandaloneGroup` |
| Sonos favorites (list, play on room/group) | ✅ | list: yes | streams directly, playlists/albums via queue |
| Internet radio directory ([Radio Browser](https://www.radio-browser.info)): popular, search, genre, country, "Meine Sender" | ✅ | yes | played as `x-rincon-mp3radio://…`; HLS stations are hidden |
| Custom logos for saved stations (shown in lists, now playing, flyout) | ✅ | no | stored as 256 px PNG in `%LOCALAPPDATA%\SonosDesk\logos` |
| Room list: click returns to the room's start page, group badge with member list | ✅ | single room only | |
| Targets: whole group, single speaker of a group (detached on play), Ctrl multi-selection (grouped on play) | ✅ | no | |
| Areas: named, colored room combinations, grouped with one click | ✅ | no | stored in settings (`roomPresets`) |
| Custom stream URLs (direct, .m3u, .pls), folders in "Meine Sender" | ✅ | no | HLS (.m3u8) rejected |
| Automatic logo lookup for stations without logo (Radio Browser name match, website icons) | ✅ | yes (SWR1 BW, list) | cached 6 h |
| "via SonosDesk" + logo in the official Sonos app for started streams | ✅ | no | sent as DIDL title / albumArtURI |
| Buffering indicator (TRANSITIONING or start pending, max. 20 s) | ✅ | no | |
| Start page cards reorderable via drag & drop | ✅ | – | order saved (`homeSectionOrder`) |
| UI languages de, en, fr, es, it, nl, pl, ru | ✅ | – | JSON catalogs in `SonosDesk.Core/Localization/Strings` |
| Radio "now playing": song/artist instead of stream URL, station as hint | ✅ | yes (1LIVE) | from the speaker's `streamContent`, otherwise ICY metadata of the stream |
| Spotify Connect (and other "Connect" apps via `x-sonos-vli`) recognized: title, artist, cover, skip | ✅ | yes (Spotify) | shown as "Spotify Connect"; play modes stay with the app |
| Seek within the track (progress slider) | ✅ | no | `Seek Unit=REL_TIME`; not for radio, line-in, TV, AirPlay |
| Queue: list (first 100), play from track, remove track, clear | ✅ | no | `Browse Q:0`, `Seek TRACK_NR`, `RemoveTrackFromQueue`, `RemoveAllTracksFromQueue` |
| Sleep timer (15–120 min, countdown, off) | ✅ | read: yes | `ConfigureSleepTimer` / `GetRemainingSleepTimerDuration` |
| Crossfade on/off | ✅ | read: yes | `SetCrossfadeMode` / `GetCrossfadeMode` |
| Sound per room: bass, treble, loudness; night sound & speech enhancement on soundbars | ✅ | no | `RenderingControl` `Set/GetBass`, `Set/GetTreble`, `Set/GetLoudness`, `Set/GetEQ` |
| Party mode: group all reachable rooms | ✅ | no | button in the grouping card and tray menu |
| Volume profiles fade from the current to the target volume (0–10 s, default 2 s) | ✅ | no | one command per 150 ms at most; all rooms fade in parallel |
| Speaker info (right-click → Info): IP, host name, model, software/hardware version, MAC, serial, player ID, role | ✅ | read: yes | from the device description; host name via local reverse DNS |
| Music library browsing | ⏳ | | |
| Event subscriptions (GENA) instead of polling | ⏳ | | needs an inbound HTTP listener + firewall rule |
| Alarms, line-in selection | ⏳ | | |

## Pioneer AV receivers (VSX) 🧪

Controlled via Pioneer IP control (TCP 8102) and the receiver's UPnP media renderer. Verified on a
Pioneer VSX-529 (VSX-830/SYXEV8).

| Feature | Status | Verified on hardware | Notes |
| --- | --- | --- | --- |
| Add by IP address, network search (SSDP MediaRenderer + Pioneer description) | ✅ | yes | |
| Status: power, volume, mute, input, front display | ✅ | yes | `?P`, `?V`, `?M`, `?F`, `?FL` |
| Volume 0–100 (100 = 0 dB reference), mute, on/off | ✅ | read: yes | `xxxVL`, `MO`/`MF`, `PO`/`PF` |
| Sound: listening mode (Auto Surround, Stereo, Extended Stereo …), bass/treble −6…+6 dB | ✅ | yes | `xxxxSR`, `?S`, `1TO`, `xxBA`, `xxTR` |
| Internet radio via the media renderer, display "Station - via SonosDesk" alternating with the song | ✅ | yes | stream relayed by SonosDesk (ICY title replaced, HTTPS → HTTP) |
| Coupling with a Sonos room (same internet radio station, start/stop) | ✅ | no | only streams Sonos plays from an http(s) URL |
| Info: model, software version, host name, control port | ✅ | yes | `?RGD`, `?SSI`, `?SSO`, description, reverse DNS |
| Volume profiles / quick actions with receivers (fade) | ✅ | no | |

## Sonos Control API backend (cloud) 🧪

Implemented from the [official documentation](https://docs.sonos.com/reference), tested with simulated
responses only. Requires your own integration credentials (see [control-api.md](control-api.md)).

| Feature | Status |
| --- | --- |
| OAuth authorization code flow, token refresh, DPAPI storage | 🧪 |
| Households, groups, players | 🧪 |
| Play, pause, toggle, next, previous, playback status, metadata | 🧪 |
| Group volume (set, relative, mute), player volume (set, mute) | 🧪 |
| Create group, modify group members | 🧪 |
| Playing radio stations from the directory | ❌ not implemented in this backend |
| Setting play modes, favorites, playlists | ❌ not implemented in this backend |
| Seek, queue, sleep timer, crossfade, sound settings | ❌ not implemented in this backend (shown as unavailable) |
| Real-time events (webhooks / websocket) | ❌ |

Players are flagged as `DiscoveredLocally`, `AuthorizedCloud` and/or `Controllable`, so the UI can
distinguish found, authorized and actually controllable players.

## Desktop integration

| Feature | Status |
| --- | --- |
| Main window (rooms, now playing, favorites, room volumes, grouping) | ✅ |
| Group composer: pick speakers, optionally a favorite and volume → "Gruppe erstellen" | ✅ |
| Save composed group as Quick Action (appears in tray) | ✅ |
| Notification-area icon: left click → flyout, right click → context menu, double click → window | ✅ |
| Context menu: play/pause, next/previous, volume ±, mute, room, favorites, volume profiles, quick actions, pause all, open, rescan, settings, exit | ✅ |
| Close to tray, start minimized, start with Windows | ✅ |
| Light / dark / system theme (Windows 11 Fluent) | ✅ |
| Global hotkeys (configurable, conflicts reported) | ✅ |
| Volume profiles, quick actions (multi-step), automations (time + weekdays), startup action | ✅ |
| In-window shortcuts: F5, Ctrl+Space, Ctrl+←/→, Ctrl+↑/↓, Ctrl+M, Ctrl+, , media keys | ✅ |
| Plugin system | ⏳ see [extensibility.md](extensibility.md) |
| Localization (8 languages, switchable in the settings) | ✅ |
| Windows media overlay & keyboard media keys (System Media Transport Controls), can be switched off | ✅ |
| Installer (Inno Setup, per user, Start menu, optional desktop icon and autostart, asks before deleting settings on uninstall) and portable exe | ✅ |
| Update hint: checks the latest GitHub release on start (banner + tray entry, "skip this version"); opens the release page, never installs by itself | ✅ |

## Speakers

Any Sonos player with local UPnP enabled should work (S1 and S2 firmware), including Sonos One/Era,
Five, Beam/Arc/Ray, Port/Amp/Connect. Boost/Bridge devices are ignored (not playable). Portable speakers
(Move/Roam) are only reachable while on Wi-Fi.

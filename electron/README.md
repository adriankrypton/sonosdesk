# SonosDesk desktop app (Electron)

The cross-platform SonosDesk client for **macOS (Apple Silicon and Intel)** and **Windows**. The window
looks and behaves like the WPF app: rooms on the left, the selected room's now-playing page on the
right, radio, Pioneer receivers, settings and a menu bar / notification area icon.

All Sonos, radio and receiver logic runs in `SonosDesk.Server` (`src/SonosDesk.Server`), a local .NET
process that this app starts and stops. The protocol between both is documented in
[`docs/desktop-api.md`](../docs/desktop-api.md).

```text
electron/
  main.js            main process: server lifecycle, window, tray, global shortcuts, start at login
  preload.js         the small bridge the page gets (window.sonosdesk)
  lib/               server process, app:// protocol + API proxy, tray, hotkeys, secret key, i18n
  renderer/          the UI: index.html, styles.css, js/ (lit-html templates, no bundler)
  scripts/           copy-files (i18n, lit-html, icons), check, publish-server, tray icon
  test/              node --test unit tests (formatting, hotkeys, ready line)
```

## How it works

- **Start-up:** `main.js` generates a per-run token and starts the server with `SONOSDESK_TOKEN`,
  `SONOSDESK_SECRET_KEY` (created once, stored encrypted with Electron `safeStorage` in
  `secret.bin`) and `SONOSDESK_LANGUAGE`. It waits for `SONOSDESK_READY http://127.0.0.1:<port>`
  (30 s, 120 s when `dotnet run` has to compile first) and then loads the UI. If the server does not
  come up, a dialog offers *Try again*, *Open logs* and *Quit*.
- **Requests:** the page is served from `app://sonosdesk/`. It calls the API same-origin
  (`app://sonosdesk/api/…`); the main process adds the bearer token and forwards the request to the
  server. Images use the same route, so the token never appears in the DOM. Only the WebSocket
  (`/api/events?token=…`) connects to the server directly.
- **State:** on start the page loads `GET /api/snapshot` and `GET /api/stations`, then keeps one
  WebSocket open. `snapshot`, `group`, `receiver`, `discovery`, `settings` and `notice` events update
  the state; the connection reconnects with back-off. Volume, seek and tone sliders are debounced
  (180 ms) and keep the local value for 2.5 s, play/pause is optimistic, and "Buffering …" shows until
  the room reports playback (at most 20 s), like in the WPF app.
- **Tray and shortcuts:** the page pushes a compact state to the main process, which builds the tray
  menu (now playing, play/pause, next, previous, louder, quieter, mute, rooms, My stations, pause all,
  open, settings, quit). Tray items, global shortcuts (`settings.hotkeys`) and the keyboard media keys
  (`settings.windowsMediaControls`) are sent back to the page, which runs them on the selected room or
  receiver.
- **Settings:** the settings page edits the server's `settings.json` (`PUT /api/settings` with the full
  object). Theme, start at login (`startWithWindows`), start hidden, close to tray, hotkeys and the
  language are applied by the main process right after saving.

## Run in development

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download), Node.js 22 and npm.

```sh
cd electron
npm install        # also copies lit-html, the language catalogs and icons into place
npm start
```

`npm start` runs the server from `../src/SonosDesk.Server/bin/Debug/net10.0/SonosDesk.Server(.exe)`
when it has been built, otherwise `dotnet run --project ../src/SonosDesk.Server -c Debug`. To use
another command, set `SONOSDESK_SERVER_CMD`, e.g.

```sh
SONOSDESK_SERVER_CMD="dotnet ../src/SonosDesk.Server/bin/Release/net10.0/SonosDesk.Server.dll" npm start
```

Other scripts:

| Script | Purpose |
| --- | --- |
| `npm run check` | `node --check` on every source file plus a link check of the renderer's ES module imports |
| `npm test` | unit tests (`node --test`) |
| `npm run copy-i18n` | copies `src/SonosDesk.Core/Localization/Strings/*.json` to `renderer/i18n/` |

Texts come from the shared catalogs in `src/SonosDesk.Core/Localization`. Strings that only the
desktop app needs live in `fragments/electron.<lang>.json` (all eight languages); after editing run
`py -I tools/merge-strings.py` in the repository root and then `npm run copy-i18n`.

Logs: `server.log` (server output) and `main.log` are written to the app's log folder
(`~/Library/Logs/SonosDesk` on macOS, `%LOCALAPPDATA%\SonosDesk\Electron\logs` on Windows).

## Build packages

The packages contain a self-contained, single-file server, so users need no .NET runtime.
`scripts/publish-server.mjs` publishes it into `electron/server/<arch>/`, where electron-builder picks
it up (`extraResources`: `server/${arch}` → `Resources/server`).

**macOS** (on a Mac; building there also gives the binaries the ad-hoc signature Apple Silicon needs):

```sh
cd electron
npm install
node scripts/publish-server.mjs --platform mac --arch x64,arm64
npm run dist:mac        # dist/SonosDesk-<version>-arm64.dmg, …-x64.dmg and .zip files
```

This is equivalent to `dotnet publish src/SonosDesk.Server -c Release -r osx-arm64 --self-contained
-o electron/server/arm64` and the same with `osx-x64` → `electron/server/x64`.

**Windows:**

```sh
cd electron
npm install
node scripts/publish-server.mjs --platform win --arch x64
npm run dist:win        # dist/SonosDesk Setup <version>.exe (NSIS)
```

The macOS and Windows servers both use `server/x64`, so publish the platform you are packaging right
before running electron-builder.

The macOS package uses `assets/icon.png` (1024 px); electron-builder converts it to `.icns`. A
hand-made `.icns` can replace it later.

## macOS notes

- **Unsigned app / Gatekeeper:** the packages are not signed with a Developer ID or notarized. On the
  first start, right-click SonosDesk in *Applications* and choose *Open* (or allow it in *System
  Settings → Privacy & Security*). If macOS reports the app as damaged after a download, run
  `xattr -cr /Applications/SonosDesk.app`.
- **Local network:** macOS asks whether SonosDesk may find devices on the local network. Allow it,
  otherwise no speakers are found. It can be changed later in *System Settings → Privacy & Security →
  Local Network*.
- **Firewall:** with the macOS firewall on, allow incoming connections for SonosDesk when asked; the
  Pioneer receivers fetch radio streams from SonosDesk.
- **Media keys:** the keyboard media keys (and global shortcuts) need the *Accessibility* permission
  (*System Settings → Privacy & Security → Accessibility*). Without it they are ignored.
- The app lives in the menu bar. Closing the window keeps it running; quit with ⌘Q or *Quit* in the
  menu bar menu.

## Windows notes

- Closing the window hides SonosDesk to the notification area when "Closing the window minimizes to the
  notification area" is on (default). Quit from the tray menu.
- With "Use the keyboard media keys" on, SonosDesk registers the media keys globally, so other players
  no longer receive them while SonosDesk runs.
- Windows Firewall asks once for `SonosDesk.Server`; allow private networks for discovery and receiver
  streaming.

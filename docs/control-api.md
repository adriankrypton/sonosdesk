# Sonos Control API backend (experimental)

The local UPnP backend is recommended. The Control API backend uses the official cloud API
(`https://api.ws.sonos.com/control/api/v1`) and is useful when the PC is not in the speakers' network.

## Why you need your own credentials

The API uses the OAuth 2.0 authorization-code flow with a **client secret**. An open-source desktop
application cannot keep a shared secret confidential, so SonosDesk ships **no credentials**. Each user
registers an own integration.

## Setup

1. Create an account at the [Sonos developer portal](https://developer.sonos.com/) and a
   *Control Integration*. Note the **client key** and **client secret**.
2. Register a **redirect URI**. Sonos requires a publicly routable **HTTPS** URL. It does not need to
   host anything useful: you only copy the URL the browser is redirected to (for example a static page
   on a domain you own).
3. In SonosDesk: *Einstellungen → Steuerung (Backend) → Sonos Control API einrichten*
   - enter client key and redirect URI,
   - enter the client secret and click **Secret speichern** (stored with DPAPI),
   - click **Im Browser anmelden** and log in with your Sonos account,
   - copy the complete URL from the browser's address bar after the redirect (it contains `code` and
     `state`) into **Weiterleitungs-URL** and click **Anmeldung abschließen**.
4. Select the backend *Offizielle Sonos Control API*, save and restart SonosDesk.

## Implementation notes

| Step | Endpoint |
| --- | --- |
| Authorize | `GET https://api.sonos.com/login/v3/oauth?client_id&response_type=code&state&scope=playback-control-all&redirect_uri` |
| Token / refresh | `POST https://api.sonos.com/login/v3/oauth/access` with HTTP Basic `client_key:client_secret` |
| Households | `GET /households` |
| Groups | `GET /households/{id}/groups` |
| Playback | `POST /groups/{id}/playback/play|pause|togglePlayPause|skipToNextTrack|skipToPreviousTrack`, `GET /groups/{id}/playback` |
| Metadata | `GET /groups/{id}/playbackMetadata` |
| Group volume | `GET/POST /groups/{id}/groupVolume`, `POST …/groupVolume/mute`, `POST …/groupVolume/relative` |
| Player volume | `GET/POST /players/{id}/playerVolume` (`volume`, `muted`) |
| Grouping | `POST /households/{id}/groups/createGroup`, `POST /groups/{id}/groups/modifyGroupMembers` |

- Tokens are refreshed 5 minutes before expiry; a 401 triggers one forced refresh and retry.
- `state` is single-use, expires after 15 minutes and is verified in constant time.
- Identifiers are validated before they are placed into URL paths.
- Not implemented: play modes, favorites, webhooks/events.

# Speaker discovery

SonosDesk combines several sources so speakers are found even in difficult networks.

## Sources

| Source | How | When |
| --- | --- | --- |
| **SSDP search** | `M-SEARCH` to `239.255.255.250:1900` on **every active IPv4 adapter**, targets `urn:schemas-upnp-org:device:ZonePlayer:1` and `upnp:rootdevice`, each sent twice, 3 s listen window | Start, every *Discovery interval* (default 60 s), on network change, "Erneut suchen" |
| **SSDP NOTIFY** | Passive listener on UDP 1900 (shared with the Windows SSDP service) for `ssdp:alive` / `ssdp:byebye` | Continuously, best effort |
| **Zone topology** | Every player's `GetZoneGroupState` lists all players of the household with their description URL | After each topology refresh |
| **Known devices** | Locations from the previous session (`known-devices.json`) | Start |
| **Manual addresses** | IPs added via right-click on the room list (*Lautsprecher per IP-Adresse hinzufügen*) or in *Einstellungen → Netzwerk* | Every round, and immediately after adding |

One reachable player is enough: all other rooms are learned from the topology.

## Processing

1. Datagrams are parsed defensively (`SsdpParser`): size ≤ 8 KB, ≤ 64 headers, valid start line,
   `USN` and `LOCATION` present. Headers used: `LOCATION`, `USN`, `ST`/`NT`, `NTS`, `SERVER`,
   `CACHE-CONTROL` (max-age), `X-RINCON-HOUSEHOLD`.
2. Only likely Sonos messages are kept (`RINCON_` in USN, `ZonePlayer` target or `Sonos` server header).
3. The `LOCATION` URL must point to the address that sent the response and pass the device URL policy
   (private/link-local IP literal, port 1400/1443, http/https).
4. Device descriptions are downloaded (max. 512 KB, 5 s timeout, one retry for transient errors) and
   parsed with `SafeXml`. Manufacturer or device type must identify a Sonos ZonePlayer.
5. Devices are **deduplicated by their RINCON id**. Embedded devices (`RINCON_…_MR`, `_MS`) map to the
   player. A fresh SSDP answer avoids re-downloading a description younger than 15 minutes.
6. Devices not seen via SSDP are probed directly. After **2 failed probes** in a row a device is reported
   as *lost*; when it answers again it is *recovered*. IP address changes are reported as *updated*.

## Network requirements

| Direction | Protocol / port | Purpose |
| --- | --- | --- |
| PC → speakers | UDP 1900 multicast (`239.255.255.250`) | SSDP search |
| Speakers → PC | UDP, ephemeral port (reply to search) | SSDP responses |
| Speakers → PC | UDP 1900 multicast | NOTIFY (optional) |
| PC → speakers | TCP 1400 | Device description, SOAP control, album art |
| PC → internet | HTTPS 443 | Album art from streaming services; Control API (only if enabled) |

Windows Firewall asks on first start whether SonosDesk may communicate on *private* networks. Allow it.
On networks classified as **public** Windows blocks inbound discovery traffic by default — switch the
network profile to *private* or allow SonosDesk for public networks.

## Troubleshooting

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| "Keine Sonos-Geräte gefunden" | Firewall blocks UDP responses | Allow SonosDesk in Windows Defender Firewall (private networks) |
| Found on Ethernet, not on Wi-Fi | Wi-Fi client/AP isolation, guest network | Use the main network or add a manual address |
| Works only when VPN is off | VPN routes or blocks multicast | Split tunneling for the LAN, or manual address |
| Speakers in another VLAN / subnet | Multicast is not routed | Manual address of one speaker (TCP 1400 must be routable) |
| Rooms appear, but commands fail | TCP 1400 blocked between PC and speaker | Check router / firewall rules |
| Speaker shows "Nicht erreichbar" | Speaker offline, sleeping or IP changed | Wait for the next round or press F5 |

Logs (`%LOCALAPPDATA%\SonosDesk\logs`) contain a summary per discovery round: interfaces used,
SSDP responses, online/new/lost devices.

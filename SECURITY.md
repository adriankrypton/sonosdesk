# Security Policy

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Use GitHub's private vulnerability reporting
(repository → *Security* → *Report a vulnerability*). Include steps to reproduce and the affected version.
You should get a first response within 7 days.

## Supported versions

Only the latest release receives security fixes while the project is in the 0.x phase.

## Security model

SonosDesk talks to devices on the local network and, optionally, to the official Sonos cloud API.
Network data is treated as untrusted:

| Area | Protection |
| --- | --- |
| SSDP | Datagrams > 8 KB or with > 64 headers are dropped. The `LOCATION` host must equal the sender address. |
| Device URLs | Only `http`/`https`, IP literals in private/link-local ranges, ports 1400/1443, no user info. Cloud metadata endpoints (`169.254.169.254`, `fd00:ec2::254`), loopback and public addresses are blocked. |
| Redirects | Never followed automatically; each hop is re-validated (max. 3, GET only). |
| DNS rebinding | Resolved addresses are checked again in the socket connect callback. |
| Device descriptions | Service URLs that point to another host than the description are ignored. |
| XML | DTDs prohibited, no external resolver, size limits (XXE / entity expansion safe). |
| Responses | Size limits per request type (512 KB – 4 MB), timeouts on every request. |
| Album art | Public hosts only via HTTPS on 443; HTTP only inside the local network; content type is checked. |
| Radio metadata (ICY) | Only public internet addresses (no LAN, loopback, CGNAT, metadata endpoints; checked again on connect), redirects re-validated, reads at most one metadata block (≤ 256 KB audio skipped), 8 s timeout, results cached 20 s. Stream URLs may contain session tokens and are never logged or displayed. Can be disabled in the settings. |
| Radio directory | HTTPS to `all.api.radio-browser.info` (fallback `de1.`), responses ≤ 4 MB, station URLs must be http(s) without user info, station ids validated as GUIDs. |
| Update check | One anonymous HTTPS request to `api.github.com` (latest release of this repository) on start, can be disabled. Release links outside `github.com/adriankrypton/sonosdesk` are ignored; nothing is downloaded or executed automatically. |
| Host names | Reverse DNS only for local speaker addresses, only when the speaker info is opened, 4 s timeout. |
| Installer | Per-user Inno Setup installer built by GitHub Actions from the tagged source; Inno Setup itself is pinned by version and SHA-256. Releases include `SHA256SUMS.txt`. |
| Stream relay (receivers) | TCP listener on a random port, started only when a receiver plays a stream. Only the IP address of the receiver the stream was started for may connect, URLs contain a random 128-bit token, GET/HEAD only, request header ≤ 8 KB, at most 8 connections. Upstream streams must be http(s) without user info; loopback and metadata addresses are blocked, redirects (max. 3) are re-validated. |
| Receiver control | IP control and UPnP requests only to private IPv4 addresses configured by the user; device descriptions are parsed with the safe XML settings, control URLs on other hosts are ignored. |
| Secrets | Control API client secret and OAuth tokens are encrypted with Windows DPAPI (current user) in `%LOCALAPPDATA%\SonosDesk\secrets`. They are never written to `settings.json` or logs. |
| OAuth | `state` is random (192 bit), single use, expires after 15 minutes and is compared in constant time. |
| Logs | No tokens, secrets or favorites metadata. URLs are logged without query strings. |
| Telemetry | None. |

The application runs with normal user rights (`asInvoker`) and needs no admin permissions.

<p align="center">
  <img src="assets/logo-256.png" width="150" alt="SonosDesk-Logo" />
</p>

<h1 align="center">SonosDesk</h1>

<p align="center">
  <b>Sonos-Lautsprecher direkt vom Desktop steuern – unter Windows und auf dem Mac, gleich neben der Uhr.</b><br/>
  Open Source · lokale Steuerung ohne Cloud-Konto · keine Telemetrie
</p>

<p align="center">
  <a href="https://github.com/adriankrypton/sonosdesk/releases/latest"><img alt="Neueste Version" src="https://img.shields.io/github/v/release/adriankrypton/sonosdesk?label=download&sort=semver&include_prereleases"></a>
  <a href="https://github.com/adriankrypton/sonosdesk/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/adriankrypton/sonosdesk/actions/workflows/ci.yml/badge.svg"></a>
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4">
  <img alt="macOS 12+" src="https://img.shields.io/badge/macOS-12%2B%20%C2%B7%20Apple%20Silicon%20%26%20Intel-000000">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4">
  <a href="LICENSE"><img alt="MIT-Lizenz" src="https://img.shields.io/badge/license-MIT-green"></a>
</p>

<p align="center">
  <a href="README.md">English</a> · <b>Deutsch</b>
</p>

---

SonosDesk findet deine Sonos-Lautsprecher automatisch. Du kannst abspielen, pausieren, Titel wechseln, die
Lautstärke ändern, Räume gruppieren und Radiosender starten – im App-Fenster **oder direkt über das Symbol im
Infobereich (Windows) bzw. in der Menüleiste (Mac)**, ohne zum Handy zu greifen. Auch Pioneer-AV-Receiver
lassen sich wie ein Sonos-Raum steuern.

> SonosDesk ist ein unabhängiges Community-Projekt und steht in keiner Verbindung zu Sonos, Inc.; es wird
> von Sonos weder unterstützt noch empfohlen. „Sonos“ ist eine Marke von Sonos, Inc.

## Inhalt

- [Versionshinweise](#versionshinweise)
- [Windows und Mac](#windows-und-mac)
- [Funktionen](#funktionen)
- [Download und Installation](#download-und-installation)
- [Erster Start](#erster-start)
- [Bedienung im Alltag](#bedienung-im-alltag)
- [Tastenkürzel](#tastenkürzel)
- [Probleme lösen](#probleme-lösen)
- [Datenschutz und Netzwerk](#datenschutz-und-netzwerk)
- [Unterstützte Lautsprecher und Einschränkungen](#unterstützte-lautsprecher-und-einschränkungen)
- [Für Entwickler](#für-entwickler)
- [Lizenz](#lizenz)

## Versionshinweise

### 0.1.1 – 19. Oktober 2026 · Vorabversion

- **SonosDesk für den Mac** (Apple Silicon und Intel, ab macOS 12) mit Symbol in der Menüleiste
- **Pioneer-AV-Receiver (VSX)** funktionieren wie Sonos-Räume: Raumliste, aktuelle Wiedergabe, Abspielen/Stopp,
  Lautstärke 0–100, Stummschalten, Klang (Hörmodus, Bässe/Höhen), *Meine Sender*; Rechtsklick → *Info*, Ein/Aus
- Internetradio auf dem Receiver; auf dem Display steht **„Sender - via SonosDesk“**, alle 10 s im Wechsel mit dem Song
- Receiver mit einem Sonos-Raum gruppieren: Er spielt denselben Internetradio-Sender und startet/stoppt mit
- Beim Aktualisieren wird angezeigt, wie viele Sonos-Geräte, weitere Media-Player (DMR) und sonstige UPnP-Geräte es gibt
- *Meine Sender* lassen sich umbenennen und löschen

### 0.1.0 – 18. Oktober 2026 · erste öffentliche Version

- Installer für Windows 10/11 (pro Benutzer, ohne Administratorrechte) und eine portable Einzeldatei
- Automatische Lautsprechersuche, manuelle IP-Adressen für VPN, Gast-WLAN und VLANs
- Wiedergabe mit Spulen, Zufall, Wiederholen und Überblenden – auch bei **Spotify Connect**
- Lautstärke pro Raum und Gruppe; **Lautstärkeprofile blenden sanft über** statt zu springen
- Gruppen, Gruppen-Assistent, farbige Bereiche, Party-Modus, Mehrfachauswahl mit Strg
- Warteschlange, Schlaf-Timer, Klang pro Raum (Bässe, Höhen, Loudness, Nachtmodus, Sprachverbesserung)
- **Lautsprecher-Info** per Rechtsklick: IP-Adresse, Hostname, Modell, Software-/Hardware-Version, MAC, Seriennummer
- Internetradio (Radio Browser) mit *Meine Sender*, Ordnern, eigenen Logos und eigenen Stream-URLs
- Tray-Flyout und -Menü, Medientasten, Windows-Medienanzeige, Tastenkürzel, Schnellaktionen, Zeitpläne, 8 Sprachen
- Update-Hinweis, sobald eine neue Version veröffentlicht ist

Alle Versionen im Detail: [CHANGELOG.md](CHANGELOG.md) (englisch) · Downloads: [Releases](https://github.com/adriankrypton/sonosdesk/releases)

## Windows und Mac

Beide Apps nutzen denselben Code für Gerätesuche, Wiedergabe, Radio und Receiver. Die Windows-App ist die
ursprüngliche WPF-App; die Mac-App ist mit 0.1.1 neu und hat noch nicht alle Extras.

| | Windows | Mac |
| --- | :---: | :---: |
| Räume, Gruppen, aktuelle Wiedergabe, Spulen, Zufall/Wiederholen, Überblenden, Schlaf-Timer | ✓ | ✓ |
| Lautstärke pro Raum und Gruppe, Stummschalten, Klang (Bässe, Höhen, Loudness, Nachtmodus, Sprache) | ✓ | ✓ |
| Favoriten, Warteschlange, Party-Modus, Räume gruppieren/trennen | ✓ | ✓ |
| Internetradio, *Meine Sender* (umbenennen, löschen, eigenes Logo, eigene Stream-URL) | ✓ | ✓ |
| Pioneer-Receiver (Radio, Lautstärke, Klang, Hörmodus, Kopplung mit einem Raum) | ✓ | ✓ |
| Lautsprecher-Info (Rechtsklick → *Info*) | ✓ | ✓ |
| Menü im Infobereich / in der Menüleiste, globale Tastenkürzel, Medientasten, Autostart, hell/dunkel | ✓ | ✓ |
| Update-Hinweis bei neuen Versionen | ✓ | ✓ |
| Gruppen-Assistent, Bereiche, Sender-Ordner | ✓ | – |
| Lautstärkeprofile, Schnellaktionen und Zeitpläne bearbeiten | ✓ | – ¹ |
| Tray-Flyout, Windows-Medienanzeige, Karten der Startseite anordnen | ✓ | – |
| 8 Sprachen | ✓ | ✓ |

¹ Zeitpläne, die schon in den Einstellungen stehen (z. B. unter Windows angelegt), laufen auch auf dem Mac.

## Funktionen

**Steuerung im Alltag**
- Abspielen/Pause, vor/zurück, im Titel spulen, Zufall, Wiederholen, Überblenden
- Titel, Interpret, Album und Cover – auch für Musik, die in der Spotify-App per **Spotify Connect** gestartet wurde
- Lautstärke pro Raum und für ganze Gruppen, Stummschalten
- **Lautstärkeprofile** wie „Abend“ oder „Party“ – die Lautstärke blendet über eine einstellbare Zeit über (Windows)
- **Warteschlange:** sehen, was als Nächstes kommt, ab einem Titel abspielen, Titel entfernen, leeren
- **Schlaf-Timer** von 15 bis 120 Minuten mit Countdown
- **Klang pro Raum:** Bässe, Höhen, Loudness – bei Soundbars zusätzlich Nachtmodus und Sprachverbesserung
- **Lautsprecher-Info:** Rechtsklick auf einen Lautsprecher zeigt IP-Adresse, Hostname, Modell, Versionen, MAC und Seriennummer

**Räume und Gruppen**
- Räume mit einem Klick gruppieren und trennen, **Party-Modus** (alle Räume spielen dasselbe)
- **Gruppe erstellen:** Lautsprecher, Favorit und Lautstärke wählen – fertig (Windows)
- **Bereiche:** benannte, farbige Raum-Kombinationen wie „Erdgeschoss“, mit einem Klick gruppiert (Windows)
- Klick auf eine Gruppe spielt in der ganzen Gruppe, Klick auf einen einzelnen Lautsprecher nur dort, mit
  Strg+Klick wählst du mehrere Räume (Windows)

**Musik**
- Deine **Sonos-Favoriten** („Mein Sonos“)
- **Internetradio** mit Tausenden Sendern aus dem offenen Verzeichnis [Radio Browser](https://www.radio-browser.info):
  Suche, Genres, Länder, *Meine Sender* mit eigenen Logos und eigenen Stream-URLs
- Beim Radio zeigt SonosDesk den **Song, der gerade läuft**, statt kryptischer Stream-Namen
- Gestartete Sender erscheinen in der offiziellen Sonos-App als „Sender · via SonosDesk“

**AV-Receiver (Pioneer VSX)**
- Receiver per Rechtsklick in die Raumliste hinzufügen; danach funktioniert er wie ein Sonos-Raum
  (Abspielen/Stopp, Lautstärke, Stummschalten, Hörmodus, Bässe/Höhen, *Meine Sender*, Info)
- Internetradio über den Netzwerk-Player des Receivers – auf dem Display steht „Sender - via SonosDesk“
- Mit einem Sonos-Raum gruppieren: Der Receiver spielt mit, wenn dort Internetradio läuft

**Integration in den Desktop**
- **Windows:** Symbol im Infobereich – Linksklick öffnet ein kompaktes Flyout, Rechtsklick ein vollständiges
  Menü; Medientasten und die Windows-Medienanzeige steuern den aktiven Raum
- **Mac:** Symbol in der Menüleiste mit aktueller Wiedergabe, Abspielen/Pause, vor/zurück, Lautstärke,
  Räumen und *Meine Sender*
- SonosDesk läuft weiter, wenn das Fenster geschlossen ist
- Globale Tastenkürzel, Start bei der Anmeldung, helles/dunkles Design
- **8 Sprachen:** Deutsch, English, Français, Español, Italiano, Nederlands, Polski, Русский
- **Update-Hinweis**, sobald eine neue Version auf GitHub veröffentlicht ist

Die ausführliche Übersicht (auch, was auf echter Hardware geprüft wurde) steht in
[docs/features.md](docs/features.md) (englisch).

## Download und Installation

Öffne die [Releases](https://github.com/adriankrypton/sonosdesk/releases) und wähle:

| Datei | Wofür |
| --- | --- |
| `SonosDesk-Setup-X.Y.Z-win-x64.exe` | **Windows, empfohlen.** Installiert SonosDesk für dein Benutzerkonto (ohne Administratorrechte), legt einen Startmenü-Eintrag an, optional ein Desktop-Symbol und „Mit Windows starten“. |
| `SonosDesk-X.Y.Z-win-x64-portable.exe` | Windows, portable Einzeldatei – aus einem beliebigen Ordner starten, es wird nichts installiert. |
| `SonosDesk-X.Y.Z-mac-arm64.dmg` | **Mac mit Apple Silicon** (M1, M2, M3, M4 …). |
| `SonosDesk-X.Y.Z-mac-x64.dmg` | **Mac mit Intel-Prozessor.** |
| `…-mac-arm64.zip`, `…-mac-x64.zip` | Dieselbe Mac-App als Zip-Archiv. |
| `SHA256SUMS.txt`, `SHA256SUMS-macos.txt` | Prüfsummen zum Kontrollieren der Downloads (`Get-FileHash <Datei>` in PowerShell, `shasum -a 256 <Datei>` im Mac-Terminal). |

Alle Versionen brauchen Sonos-Lautsprecher im selben Heimnetz (S1- oder S2-System). Eine separate
.NET-Laufzeit ist nicht nötig.

### Windows

Voraussetzungen: Windows 10 (ab Version 2004) oder Windows 11, 64 Bit.

> **Windows SmartScreen:** SonosDesk ist noch nicht digital signiert. Windows zeigt daher eventuell
> „Der Computer wurde durch Windows geschützt“. Klicke auf **Weitere Informationen → Trotzdem ausführen**.

**Deinstallieren:** *Windows-Einstellungen → Apps → Installierte Apps → SonosDesk → Deinstallieren*. Dabei
wirst du gefragt, ob auch deine Einstellungen (`%LOCALAPPDATA%\SonosDesk`) gelöscht werden sollen. Bei der
portablen Version schaltest du in den Einstellungen *Mit Windows starten* aus, beendest SonosDesk und
löschst die Datei.

### Mac

Voraussetzung: macOS 12 Monterey oder neuer. Welcher Mac? Apple-Menü → *Über diesen Mac*:
„Chip Apple M…“ = Apple Silicon (`arm64`), „Prozessor Intel …“ = Intel (`x64`).

1. Die `.dmg` öffnen und **SonosDesk** in **Programme** ziehen.
2. SonosDesk starten. Die App ist noch nicht von Apple notarisiert, daher blockiert macOS den ersten Start:
   **Systemeinstellungen → Datenschutz & Sicherheit** öffnen, nach unten scrollen und bei SonosDesk auf
   **Dennoch öffnen** klicken. (Alternativ im Terminal: `xattr -dr com.apple.quarantine /Applications/SonosDesk.app`.)
3. **„SonosDesk möchte Geräte im lokalen Netzwerk finden“** erlauben – sonst werden keine Lautsprecher gefunden.
   Später änderbar unter *Systemeinstellungen → Datenschutz & Sicherheit → Lokales Netzwerk*.

SonosDesk läuft dann in der Menüleiste weiter, auch wenn das Fenster geschlossen ist. Beenden mit **⌘Q**
oder über das Menüleisten-Symbol.

**Deinstallieren:** SonosDesk beenden, aus *Programme* in den Papierkorb ziehen und bei Bedarf die
Einstellungen in `~/Library/Application Support/SonosDesk` löschen.

### Updates

SonosDesk prüft beim Start, ob es eine neuere Version gibt, und zeigt dann einen Hinweis. Unter Windows
startest du das neue Setup – es aktualisiert die vorhandene Installation, deine Einstellungen bleiben erhalten.
Auf dem Mac ersetzt du die App in *Programme* durch die neue. Die Prüfung lässt sich unter
*Einstellungen → Updates* abschalten.

## Erster Start

1. Starte SonosDesk. Wenn Windows nach der Firewall fragt, erlaube den Zugriff in **privaten Netzwerken**;
   auf dem Mac den Zugriff auf das lokale Netzwerk erlauben.
2. Nach wenigen Sekunden erscheinen deine Räume links. Beim nächsten Start ist der zuletzt genutzte Raum ausgewählt.
3. Nichts gefunden? Rechtsklick in die leere Raumliste → **Lautsprecher per IP-Adresse hinzufügen …** und die
   IP eines beliebigen Lautsprechers eingeben (Sonos-App → *Einstellungen → System → Über mein System*). Ein
   Lautsprecher reicht, die anderen Räume werden darüber gefunden.

Die Sprache richtet sich nach dem System und lässt sich unter *Einstellungen* ändern.

## Bedienung im Alltag

| Wo | Was du tun kannst |
| --- | --- |
| Raumliste (links) | Raum oder Gruppe anklicken, um sie zu steuern · **Rechtsklick → Info** zeigt die Lautsprecher-Details · Rechtsklick in die Liste fügt einen Lautsprecher oder Receiver per IP-Adresse hinzu |
| Aktuelle Wiedergabe | Spulen, Zufall/Wiederholen, Überblenden und Schlaf-Timer · Favoriten · Raumlautstärken · Warteschlange · Klang · Gruppieren und Party-Modus |
| *Radio* | Sender suchen und stöbern, ☆ speichert sie unter *Meine Sender*; umbenennen, löschen oder ein eigenes Logo vergeben; eigene Stream-URL hinzufügen |
| Receiver | Dieselbe Seite wie ein Raum: *Meine Sender* antippen und abspielen, Lautstärke, Hörmodus, Bässe/Höhen, Kopplung mit einem Sonos-Raum |
| Symbol im Infobereich (Windows) | Linksklick: Flyout · Rechtsklick: Räume, Favoriten, Sender, Bereiche, Lautstärkeprofile, Party-Modus, Schlaf-Timer, Schnellaktionen, Beenden · Doppelklick: Hauptfenster |
| Symbol in der Menüleiste (Mac) | Aktuelle Wiedergabe, Abspielen/Pause, vor/zurück, lauter/leiser, stumm, Räume, *Meine Sender*, alle pausieren, öffnen, Einstellungen, beenden |
| *Einstellungen* | Design, Sprache, Start bei der Anmeldung, Gerätesuche und manuelle Adressen, Lautstärkeschritt, Tastenkürzel, Updates (unter Windows außerdem Medienanzeige, Profile, Schnellaktionen, Zeitpläne) |

Nur unter Windows: einen einzelnen Lautsprecher einer Gruppe anklicken, um nur dort zu spielen, mit
**Strg+Klick** mehrere Räume wählen und dann einen Favoriten oder Sender starten (genau diese Räume werden
gruppiert), die Seiten *Gruppe erstellen* und *Bereiche* sowie Karten der Startseite über den ☰-Griff verschieben.

Das Schließen des Hauptfensters lässt SonosDesk im Infobereich bzw. in der Menüleiste weiterlaufen (unter
Windows in den Einstellungen änderbar). Ganz beenden kannst du es mit **Beenden** im Tray-Menü oder mit
**⌘Q** auf dem Mac.

## Tastenkürzel

| Wo | Windows | Mac |
| --- | --- | --- |
| Überall (anpassbar) | `Strg+Alt+P` Abspielen/Pause · `Strg+Alt+←/→` zurück/vor · `Strg+Alt+↑/↓` Lautstärke | dasselbe mit `⌃ Control` + `⌥ Option` |
| Medientasten | Abspielen/Pause, vor, zurück im aktiven Raum (abschaltbar) | dasselbe (macOS fragt nach der Berechtigung *Bedienungshilfen*) |
| Hauptfenster | `F5` neu suchen · `Strg+Leertaste` Abspielen/Pause · `Strg+←/→` Titel · `Strg+↑/↓` Lautstärke · `Strg+M` stumm · `Strg+,` Einstellungen | `F5` · `⌘Leertaste` · `⌘←/→` · `⌘↑/↓` · `⌘,` Einstellungen · `⌘Q` beenden |

## Probleme lösen

**Es werden keine Lautsprecher gefunden**
- Computer und Lautsprecher müssen im selben Netz sein – Gast-WLAN, VPN und getrennte VLANs blockieren die Suche oft.
- Windows: SonosDesk in der Windows-Firewall für private Netzwerke erlauben (UDP 1900 und TCP 1400 im lokalen Netz).
- Mac: unter *Systemeinstellungen → Datenschutz & Sicherheit → Lokales Netzwerk* muss SonosDesk eingeschaltet sein.
- Lautsprecher per IP-Adresse hinzufügen (Rechtsklick in die Raumliste oder *Einstellungen → Netzwerk & Gerätesuche*).
- Weitere Hilfe: [docs/discovery.md](docs/discovery.md) (englisch).

**Der Receiver spielt keinen Sender** – der Receiver holt den Stream bei SonosDesk auf dem Computer ab.
Erlaube SonosDesk in der Firewall (Windows: private Netzwerke; Mac: *Systemeinstellungen → Netzwerk → Firewall*,
eingehende Verbindungen erlauben) und aktiviere am Receiver die Netzwerksteuerung („Network Standby“).
SonosDesk muss laufen, solange der Receiver spielt.

**Die Medientasten steuern die falsche App** – das System schickt sie an die zuletzt aktive Medien-App.
Schalte die Medientasten in den Einstellungen aus, wenn du sie lieber für andere Apps nutzt.

**Der Mac meldet „SonosDesk ist beschädigt“ oder lässt sich nicht öffnen** – das ist die Gatekeeper-Warnung
für nicht notarisierte Apps. Nutze *Dennoch öffnen* unter *Datenschutz & Sicherheit* oder den `xattr`-Befehl
aus [Mac](#mac).

**Ein Befehl schlägt fehl** – SonosDesk zeigt den Fehler oben im Fenster an. Protokolle für Fehlerberichte
findest du unter *Einstellungen → Über SonosDesk → Protokolle öffnen* (Windows: `%LOCALAPPDATA%\SonosDesk\logs`;
Mac: `~/Library/Logs/SonosDesk` und `~/Library/Application Support/SonosDesk/logs`). Sie enthalten keine
Passwörter oder Tokens.

**Fehler gefunden oder eine Idee?** Eröffne ein [Issue](https://github.com/adriankrypton/sonosdesk/issues) –
gerne auch auf Deutsch.

## Datenschutz und Netzwerk

SonosDesk hat keine Telemetrie und braucht kein Konto. Es spricht mit deinen Lautsprechern im lokalen Netz
und verbindet sich nur für Folgendes mit dem Internet:

| Verbindung | Zweck | Optional |
| --- | --- | --- |
| Lautsprecher, TCP 1400 / UDP 1900 (lokal) | Suche, Steuerung, Metadaten, Cover | – |
| Lokales DNS | Hostname in der Lautsprecher-Info | nur beim Öffnen von *Info* |
| Cover- und Logo-Server (HTTPS) | Albumcover und Senderlogos | – |
| `*.api.radio-browser.info` | Radioverzeichnis | nur auf der Radio-Seite |
| Server der Radiostreams | aktuellen Song eines Senders lesen (ICY-Metadaten) | ja, in den Einstellungen |
| `api.github.com` | nach neuer Version suchen | ja, in den Einstellungen |
| AV-Receiver → Computer (lokal, zufälliger TCP-Port) | der Receiver holt Internetradio-Streams bei SonosDesk ab | nur solange ein Receiver einen Stream spielt |
| AV-Receiver, TCP 8102 / 8080 (lokal) | Pioneer-Netzwerksteuerung und Media-Renderer | nur mit Receivern |
| `api.sonos.com`, `api.ws.sonos.com` | Sonos Control API, nur wenn du das Cloud-Backend aktivierst | ja |

Auf dem Mac spricht das Fenster mit einem kleinen SonosDesk-Dienst auf demselben Computer; er lauscht nur auf
`127.0.0.1` und nimmt nur Anfragen der App an (zufälliger Schlüssel bei jedem Start).

Einstellungen liegen in `%LOCALAPPDATA%\SonosDesk` (Windows) bzw. `~/Library/Application Support/SonosDesk`
(Mac). Geheimnisse (Control-API-Tokens) werden verschlüsselt – unter Windows mit DPAPI, auf dem Mac mit einem
Schlüssel im Schlüsselbund – und nie in die Einstellungsdatei geschrieben. Siehe [SECURITY.md](SECURITY.md) (englisch).

## Unterstützte Lautsprecher und Einschränkungen

Jeder Sonos-Player mit lokaler Steuerung funktioniert, z. B. One/Era, Five, Beam/Arc/Ray, Port/Amp/Connect;
Move/Roam nur, solange sie im WLAN sind. Boost/Bridge werden ignoriert, weil sie nicht abspielen können.

Aktuelle Einschränkungen:
- Der Zustand wird regelmäßig abgefragt (alle 2 s für den sichtbaren Raum); Push-Ereignisse sind geplant.
- Noch keine Musikbibliothek und keine Wecker.
- Die Mac-App ist neu: Was ihr noch fehlt, steht unter [Windows und Mac](#windows-und-mac). Sie ist noch nicht
  von Apple notarisiert.
- Das optionale **Sonos-Control-API**-Backend ist experimentell, braucht eigene Entwickler-Zugangsdaten und
  kann weniger (keine Favoriten, Warteschlange, Schlaf-Timer, Klangeinstellungen). Siehe
  [docs/control-api.md](docs/control-api.md).
- Viele Befehle sind mit automatischen Tests gegen simulierte Lautsprecher abgesichert; welche zusätzlich auf
  echter Hardware geprüft wurden, steht in [docs/features.md](docs/features.md).

## Für Entwickler

SonosDesk ist in C# / .NET 10 geschrieben. Die Windows-App ist WPF (MVVM mit CommunityToolkit.Mvvm,
Microsoft.Extensions DI/Logging, xUnit). Die Mac-App ist eine [Electron](electron/README.md)-Oberfläche, die
`SonosDesk.Server` startet – einen lokalen .NET-Dienst aus demselben Core- und Infrastructure-Code
([API](docs/desktop-api.md), [Architektur](docs/architecture.md)). Zum Bauen brauchst du das
[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0); die WPF-App braucht Windows.

**Windows-App**

```powershell
dotnet restore SonosDesk.sln
dotnet build SonosDesk.sln -c Release
dotnet test --solution SonosDesk.sln -c Release      # keine Lautsprecher nötig
dotnet run --project src/SonosDesk.App -c Release
```

Installer (braucht [Inno Setup 7](https://jrsoftware.org/isinfo.php)); veröffentlicht die App und erzeugt
`artifacts/installer/SonosDesk-Setup-<Version>-win-x64.exe`:

```powershell
./tools/build-installer.ps1
```

**Mac-App** (auf einem Mac mit .NET 10 SDK und Node.js 22; `npm start` funktioniert zum Entwickeln auch unter Windows)

```bash
dotnet build src/SonosDesk.Server
cd electron
npm ci
npm start                                                    # Entwicklung, startet den Dienst mit
node scripts/publish-server.mjs --platform mac --arch x64,arm64
npx electron-builder --mac --x64 --arm64 --publish never     # dist/*.dmg und *.zip
```

| Ordner | Inhalt |
| --- | --- |
| `src/SonosDesk.Core` | Domänenmodelle, Schnittstellen, Schnellaktionen, Einstellungen, Übersetzungen (ohne Oberfläche) |
| `src/SonosDesk.Infrastructure` | SSDP-Suche, UPnP/SOAP, Control API, Radioverzeichnis, Receiver, Speicherung |
| `src/SonosDesk.Windows` | Tray-Symbol, globale Tastenkürzel, Autostart, DPAPI |
| `src/SonosDesk.App` | WPF-Ansichten und ViewModels (Windows-App) |
| `src/SonosDesk.Server` | lokaler REST-/WebSocket-Dienst für die Mac-App (plattformübergreifend) |
| `electron/` | Electron-App für den Mac (Menüleiste, Wiedergabe, Radio, Receiver, Einstellungen) |
| `installer/` | Inno-Setup-Skript |
| `tests/` | deterministische xUnit-Tests mit simulierten Netzwerkantworten |

**Releases:** Die CI baut und testet bei jedem Push beide Apps und stellt den Windows-Installer sowie die
Mac-Dateien (`.dmg`/`.zip`) als Artefakte bereit. Für eine neue Version erhöhst du `<Version>` in
`Directory.Build.props`, ergänzt einen Abschnitt in `CHANGELOG.md` und pushst dann einen Tag wie `v0.2.0` oder
startest im Actions-Tab den Workflow *Release*. Er erstellt ein GitHub-Release mit Windows-Setup, portabler
Exe, Zip, den Mac-Apps und Prüfsummen; der Changelog-Abschnitt wird zum Release-Text.

- UI-Texte liegen in `src/SonosDesk.Core/Localization/fragments/*.{lang}.json`; nach dem Bearbeiten
  `python tools/merge-strings.py` ausführen. Neue Übersetzungen sind willkommen.
- Kommandozeilenoptionen unter Windows: `--minimized` (im Infobereich starten), `--autostart` (vom Windows-Autostart genutzt).

Dokumentation (englisch): [Architektur](docs/architecture.md) · [Gerätesuche](docs/discovery.md) ·
[Funktionen](docs/features.md) · [Schnellaktionen & Erweiterbarkeit](docs/extensibility.md) ·
[Control API einrichten](docs/control-api.md) · [Desktop-API](docs/desktop-api.md) ·
[Mitmachen](CONTRIBUTING.md) · [Sicherheit](SECURITY.md) · [Drittanbieter-Hinweise](THIRD-PARTY-NOTICES.md)

## Lizenz

[MIT](LICENSE) © [adriankrypton](https://github.com/adriankrypton) und SonosDesk-Mitwirkende.
Senderdaten stellt zur Laufzeit das Community-Projekt [Radio Browser](https://www.radio-browser.info) bereit.

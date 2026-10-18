# Quick actions, automations and extensibility

## Quick actions

A quick action is a named list of steps. Steps without `room` target the **active room** (selected in the
window, flyout or tray). They run in order; a failing step is reported and the remaining steps continue.

| `kind` | Uses | Effect |
| --- | --- | --- |
| `pauseAll` | – | Pauses every playing group |
| `play`, `pause`, `togglePlayPause`, `next`, `previous` | `room` | Transport command for the room's group |
| `setVolume` | `room`, `value` 0–100 | Active room: group volume if grouped; explicit room: that player |
| `adjustVolume` | `room`, `value` ±100 | Relative change, same targeting |
| `applyVolumeProfile` | `argument` = profile name | Applies a volume profile |
| `createGroup` | `argument` = rooms (comma separated), `room` = coordinator | Forms a group of exactly these rooms |
| `playFavorite` | `argument` = favorite name, `room` | Plays a Sonos favorite on the room's group |
| `playRadioStation` | `argument` = name in *Meine Sender*, `room` | Plays a saved radio station on the room's group |
| `joinRoom` | `room`, `argument` = target room | Adds a room to another room's group |
| `leaveGroup` | `room` | Makes the room standalone |

Example (`%LOCALAPPDATA%\SonosDesk\settings.json`):

```json
{
  "quickActions": [
    {
      "id": "party-unten",
      "name": "Party unten",
      "showInTray": true,
      "steps": [
        { "kind": "createGroup", "room": "Wohnzimmer", "argument": "Wohnzimmer, Küche, Esszimmer" },
        { "kind": "setVolume", "room": "Küche", "value": 30 },
        { "kind": "playFavorite", "room": "Wohnzimmer", "argument": "1LIVE DIGGI" }
      ]
    }
  ],
  "automations": [
    { "id": "morning", "name": "Morgens", "enabled": true, "time": "06:45:00",
      "days": ["monday", "tuesday", "wednesday", "thursday", "friday"], "quickActionId": "party-unten" }
  ]
}
```

The group composer in the UI (*Gruppe erstellen → Als Quick Action speichern*) produces exactly this
structure. Edits to the file are picked up on the next start; invalid entries are dropped, not fatal.

## Automations

`AutomationScheduler` checks every 20 s and fires each occurrence once. Occurrences missed by more than
five minutes (PC asleep/off) are skipped instead of being executed late. The **startup action** runs once
when SonosDesk was started by Windows autostart and rooms were found within one minute.

## Global hotkeys

Configured as text (`Ctrl+Alt+Space`, `Win+F5`, `MediaPlayPause`). Keys without modifiers are only
allowed for media/volume keys and F13–F24. Combinations used by other applications are reported.

## Plugin system (planned)

A plugin runtime is intentionally **not** implemented yet; the core control path must be stable first.
The planned design:

- Plugins are .NET assemblies in `%LOCALAPPDATA%\SonosDesk\plugins\<name>\` with a manifest
  (`id`, `version`, `minHostVersion`, `entryType`).
- They receive a narrow host API: read-only state (`ISonosStateService` snapshots), the quick action
  executor and a logger — not the raw backends or secrets.
- Each plugin is loaded into its own `AssemblyLoadContext`, can be enabled/disabled in settings, and
  exceptions are isolated and logged; a crashing plugin is disabled.
- Version compatibility is checked against `minHostVersion` before loading.

Until then, the extension points are quick action step kinds (`QuickActionStepKind` +
`QuickActionExecutor`) and new backends (`ISonosControlService`).

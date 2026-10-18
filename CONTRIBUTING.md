# Contributing to SonosDesk

Thanks for helping! This project aims to be a **reliable** Sonos controller for Windows, so correctness
beats feature count.

## Getting started

Requirements: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0),
optionally Visual Studio 2026 / Rider / VS Code with C# Dev Kit.

```powershell
git clone <your fork>
cd sonosdesk
dotnet restore SonosDesk.sln
dotnet build SonosDesk.sln -c Release
dotnet test --solution SonosDesk.sln -c Release
dotnet run --project src/SonosDesk.App
```

Release builds treat warnings as errors — please keep the build warning-free.

## Project layout

| Project | Purpose | May reference |
| --- | --- | --- |
| `SonosDesk.Core` | Domain models, interfaces, orchestration (state polling, quick actions, scheduler) | – |
| `SonosDesk.Infrastructure` | SSDP discovery, UPnP/SOAP backend, Control API backend, persistence, logging | Core |
| `SonosDesk.Windows` | Tray icon, global hotkeys, autostart, DPAPI secret store | Core |
| `SonosDesk.App` | WPF UI (MVVM), composition root | all |

Core and Infrastructure target plain `net10.0` and must not use WPF or WinForms.
See [docs/architecture.md](docs/architecture.md).

## Guidelines

- **No fake features.** If a backend can't do something, advertise it through `SonosCapabilities` and
  throw `SonosException(SonosErrorKind.NotSupported, …)`.
- **Never invent APIs.** UPnP actions must exist on Sonos players; Control API calls must match
  <https://docs.sonos.com/reference>. Link the source in your PR.
- **Untrusted input.** Use `SafeXml`, `DeviceUrlValidator` and `GuardedHttpClient` for anything that
  comes from the network.
- **Async all the way** with `CancellationToken`; nothing blocking on the UI thread.
- **Tests** must not need real speakers. Use the fakes in `tests/*/TestSupport.cs` / `Fakes.cs`.
- **Logging** via `ILogger<T>`, structured templates, no secrets or personal data.
- User-facing strings are localized: add keys to the fragments in
  `src/SonosDesk.Core/Localization/fragments/*.{lang}.json` (German and English at least, other
  languages via translation), run `python tools/merge-strings.py`, then use `L.T("key")` /
  `L.F("key", args)` in C# and `{l:T key}` in XAML. The `LocalizationTests` check that every
  language has every key with the same placeholders.
- Follow `.editorconfig`; nullable reference types are enabled everywhere.
- No `///` XML documentation comments; use a short `//` comment only where the reason is not obvious.
- Note user-visible changes in `CHANGELOG.md` under the next version.

## Pull requests

1. Open an issue first for larger changes.
2. Keep PRs focused; include tests.
3. Fill in the PR template (build, tests, real-device check if possible).

By contributing you agree that your contribution is licensed under the MIT license.

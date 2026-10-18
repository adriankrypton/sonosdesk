# CLAUDE.md — SonosDesk

## 1. Project Overview

**SonosDesk** is an open-source Windows desktop application written in C# for discovering, monitoring, and controlling Sonos speakers.

The application provides a modern Windows UI, automatic speaker discovery, playback controls, room and group management, and a Windows system tray menu for quick access without opening the main window.

The long-term goal is to build a reliable, extensible, community-driven alternative for desktop Sonos control.

Priorities, in order:

1. Reliability and correct device discovery.
2. Working playback and volume controls.
3. Fast and intuitive Windows tray integration.
4. Clean architecture and automated tests.
5. Extensibility and open-source maintainability.

Do not implement features as fake UI controls or successful operations that are not actually supported.

## 2. Technology Stack

Use the following technologies unless the existing repository provides a compelling reason to change them:

- Language: C#
- Framework: .NET 10 LTS
- Desktop UI: WPF
- Architecture: MVVM
- MVVM toolkit: CommunityToolkit.Mvvm
- Dependency injection: Microsoft.Extensions.DependencyInjection
- Logging: Microsoft.Extensions.Logging
- Serialization: System.Text.Json
- Testing: xUnit
- Version control: Git
- CI/CD: GitHub Actions
- License: MIT

Use stable, compatible package versions. Check target framework compatibility and package documentation before adding dependencies.

Do not introduce unnecessary frameworks or abstractions.

## 3. Development Principles

- Inspect the repository before modifying files.
- Preserve existing working code unless a change is necessary.
- Prefer small, incremental, buildable changes.
- Write production-quality C# code.
- Use nullable reference types.
- Use asynchronous APIs for network and I/O operations.
- Pass CancellationToken to long-running operations where appropriate.
- Use dependency injection for services.
- Keep business logic out of UI code-behind.
- Follow SOLID principles pragmatically.
- Use descriptive names and consistent formatting.
- Avoid duplicated logic, unnecessary complexity, and premature abstraction.
- Do not leave unexplained TODOs, stubs, or placeholder implementations.
- Do not claim that code builds or tests pass unless the commands were actually executed successfully.

## 4. Architecture

Keep responsibilities separated into appropriate projects or modules.

Recommended structure:

```text
src/
  SonosDesk.App/
    Views/
    ViewModels/
    Resources/
  SonosDesk.Core/
    Models/
    Interfaces/
    Events/
  SonosDesk.Infrastructure/
    Discovery/
    Networking/
    Upnp/
    ControlApi/
    Persistence/
  SonosDesk.Windows/
    Tray/
    Hotkeys/
    Startup/

tests/
  SonosDesk.Core.Tests/
  SonosDesk.Infrastructure.Tests/

docs/
.github/
CLAUDE.md
README.md
CONTRIBUTING.md
SECURITY.md
LICENSE
SonosDesk.sln
```

Adapt this structure to the existing solution when appropriate. Do not create separate projects solely to follow this example if doing so adds no practical value.

Core domain models must not depend on WPF or Windows-specific UI components.

Use interfaces for network discovery, player control, topology, persistence, and other services where implementations need to be interchangeable or tested independently.

## 5. Sonos Auto-Discovery

Automatic device discovery is a core feature and must be implemented as a reliable, continuously maintainable service.

Create an `ISonosDiscoveryService` with an appropriate implementation.

Requirements:

- Use SSDP over UDP multicast.
- SSDP multicast address: `239.255.255.250`.
- SSDP port: `1900`.
- Send appropriate M-SEARCH requests for supported UPnP device and service types.
- Parse SSDP responses defensively.
- Handle relevant headers, including `LOCATION`, `USN`, `ST`, `SERVER`, and `CACHE-CONTROL`.
- Retrieve and parse device descriptions over HTTP.
- Extract device UUIDs, model information, manufacturer, friendly name, and service endpoints.
- Deduplicate devices using stable identifiers.
- Support repeated discovery and device rediscovery.
- Detect devices that disappear or become unreachable.
- Support cancellation, timeouts, bounded retries, and structured logging.
- Handle multiple network interfaces where practical.
- Handle network changes, firewall restrictions, and unavailable interfaces gracefully.
- Never block the UI thread.
- Do not hardcode speaker IP addresses.
- Do not assume one SSDP response contains the complete room topology.

Implement a separate topology service for discovering rooms, players, and groups.

### Network security

Treat SSDP responses and device descriptions as untrusted network input.

- Validate URL schemes and target addresses.
- Restrict device-description requests to expected local network destinations.
- Validate redirects and destination ports.
- Prevent accidental requests to public hosts or cloud metadata endpoints.
- Limit response sizes and XML parsing resources.
- Disable unsafe XML external entity processing.
- Use sensible HTTP timeouts.
- Avoid unrestricted retries and uncontrolled background tasks.

Discovery must remain useful when some devices cannot be reached.

## 6. Sonos Control Backends

Keep device discovery separate from playback control.

Define appropriate interfaces, including:

- `ISonosDiscoveryService`
- `ISonosControlService`
- `ISonosPlaybackService`
- `ISonosVolumeService`
- `ISonosTopologyService`
- `ISonosGroupService`
- `ISonosConfigurationService`

Support interchangeable backends where practical.

### Local UPnP/SOAP

Use local UPnP/SOAP services only for operations supported by the target device and its firmware.

Implement HTTP and SOAP communication in infrastructure services, not in ViewModels.

Handle SOAP faults, HTTP errors, timeouts, and unavailable devices explicitly.

### Official Sonos Control API

Keep the official Sonos Control API in a separate integration module.

- Follow current official Sonos documentation.
- Implement the required authorization flow correctly.
- Never embed client secrets in source code.
- Store credentials and tokens securely using suitable Windows facilities.
- Distinguish discovered devices from authorized and controllable devices.
- Document any functionality unavailable through a particular backend.

Never invent endpoints, scopes, request formats, or capabilities.

Verify current API requirements before implementation.

## 7. Playback and Room Management

Implement and test supported operations for:

- Play and pause.
- Next and previous track.
- Current playback state.
- Track title, artist, album, and artwork where available.
- Current source and playback mode where available.
- Per-player volume.
- Group volume.
- Mute where supported.
- Room and player listing.
- Group creation and removal.
- Adding and removing players from groups.
- Synchronizing player and group state.

Represent unavailable metadata explicitly rather than inventing values.

Use typed domain models and clear error results.

Avoid frequent polling when event subscriptions or more efficient supported update mechanisms are available.

## 8. Windows User Interface

Build the main application with WPF and MVVM.

Use CommunityToolkit.Mvvm where appropriate.

The main window should provide:

- A list of discovered speakers and rooms.
- Device connectivity and discovery status.
- Current playback information.
- Album artwork when available.
- Playback and track navigation controls.
- Volume controls.
- Room and group management.
- A manual rediscovery action.
- Settings and configuration.
- Useful loading, empty, success, and error states.

Requirements:

- Support light and dark themes.
- Keep the interface responsive.
- Run network operations asynchronously.
- Use commands and observable properties.
- Avoid business logic in code-behind.
- Dispose of resources correctly.
- Provide accessible labels and keyboard navigation.

## 9. Windows System Tray

The tray integration is a key product feature, not an optional afterthought.

Implement a Windows notification-area icon with a compact context menu or popup.

The menu should support:

- Play/pause.
- Previous and next track.
- Volume adjustments.
- Selecting a room.
- Displaying current track information where practical.
- Quick actions.
- Opening the main application.
- Triggering discovery.
- Opening settings.
- Explicitly exiting the application.

The app must continue operating when its main window is hidden or minimized.

Closing the main window should hide it to the tray when this behavior is enabled.

Provide an explicit Exit command that stops background services and shuts down cleanly.

Handle Windows startup, icon lifetime, popup ownership, window activation, and disposal correctly.

Choose a maintained tray library or implement a small Windows-specific abstraction. Keep this functionality isolated from the core domain.

## 10. Quick Actions and Future Extensions

Design for future features without overengineering the initial implementation.

Potential features include:

- Pause all speakers.
- Apply predefined volume levels.
- Select a default room.
- Start playback on a selected room.
- Saved room and group presets.
- Global media hotkeys.
- User-defined shortcuts.
- Scheduled automations.
- User-defined action sequences.
- A documented plugin API.
- Optional community integrations.

Implement foundational features first. Do not build a complex plugin execution environment until the core control functionality is reliable.

## 11. Configuration and Persistence

Store per-user settings under an appropriate directory in `%LOCALAPPDATA%`.

Support settings such as:

- Theme.
- Start with Windows.
- Minimize to tray.
- Discovery interval.
- Default room.
- Saved quick actions.
- Volume profiles.
- Keyboard shortcuts.

Use versioned JSON configuration where appropriate.

Handle missing, invalid, and outdated configuration files safely.

Use atomic writes when practical.

Never store secrets or OAuth tokens in plaintext JSON configuration.

## 12. Logging and Error Handling

Use Microsoft.Extensions.Logging.

Log:

- Discovery start and completion.
- Device additions and removals.
- Connection failures.
- Control operation failures.
- API authorization errors.
- Unexpected exceptions.

Use structured logging and appropriate log levels.

Never log credentials, access tokens, or sensitive user data.

Expected network failures must not crash the application.

Display actionable user-facing errors without exposing internal stack traces.

## 13. Testing

Use xUnit.

Write deterministic tests for:

- SSDP response parsing.
- Device description parsing.
- UUID deduplication.
- Malformed XML.
- Invalid and unsafe URLs.
- Timeouts and cancellation.
- Discovery retries.
- Simulated device availability changes.
- Playback and volume models.
- Group and topology models.
- Configuration serialization.
- Error handling.

Use mocked or simulated network responses.

Tests must not require physical Sonos devices.

Keep protocol parsing and business logic testable independently from WPF.

Run the full test suite after significant changes.

## 14. Open-Source Requirements

Prepare SonosDesk for public GitHub development.

Maintain:

- `README.md`
- `CONTRIBUTING.md`
- `SECURITY.md`
- `LICENSE`
- Issue templates.
- Pull request template.
- GitHub Actions workflows.
- Build and test instructions.
- Architecture documentation.
- Discovery troubleshooting documentation.
- A list of supported features and limitations.

Use the MIT license unless the repository already specifies another license.

Check dependency licenses and avoid distributing proprietary Sonos assets without permission.

Do not commit generated build artifacts, credentials, machine-specific settings, or secrets.

## 15. Build and CI Requirements

After changes, execute the relevant commands whenever the environment permits:

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

If the solution targets Windows-specific frameworks, use an appropriate Windows build environment or enable the required cross-targeting settings where supported.

Fix compiler errors and test failures before declaring work complete.

If commands cannot run, explain exactly why and do not claim successful verification.

Use GitHub Actions to automate restore, build, and tests on supported runners.

## 16. Working Method

For each development task:

1. Inspect the repository and current implementation.
2. Identify the relevant architecture and existing tests.
3. State a concise implementation plan.
4. Implement the feature in complete source files.
5. Add or update tests.
6. Build and test the solution.
7. Fix discovered problems.
8. Update documentation when behavior or setup changes.
9. Summarize modified files, results, and limitations.

Do not stop after producing a plan. Proceed with implementation unless blocked by missing credentials, unavailable infrastructure, or a genuinely necessary decision.

Prefer sensible defaults over unnecessary questions.

Do not silently replace working code with simplified examples.

Do not mark incomplete features as complete.

## 17. Definition of Done

A feature is complete only when:

- Its behavior is implemented rather than represented by a placeholder.
- Its architecture fits the project.
- Relevant tests are present.
- The build succeeds when the environment supports it.
- Errors and cancellation are handled.
- Documentation is updated where necessary.
- Known limitations are explicitly stated.

## 18. First Task

Begin by inspecting the repository and its existing solution.

Then:

1. Identify missing or incomplete project components.
2. Establish or repair the .NET solution structure.
3. Implement SSDP discovery and device-description parsing.
4. Add deterministic discovery tests.
5. Build a basic WPF interface that lists discovered devices.
6. Prepare the service architecture for playback control and tray integration.
7. Update the README with build and troubleshooting instructions.

Keep the solution buildable throughout development.

**The primary goal is a real, reliable, maintainable open-source Sonos control application for Windows—not a prototype that only looks functional.**
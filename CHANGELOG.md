# Changelog

English | [中文](CHANGELOG.zh-CN.md)

Notable changes to this project. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
versions follow [Semantic Versioning](https://semver.org/). Item-by-item notes for users are in the in-app help's
"Changelog" ([`help.en.md`](src/MacroHub/wwwroot/help.en.md)).

## [Unreleased]

## [0.2.0] - 2026-09-20

### Added
- Unreal Engine plugin (phase 2, `unreal/MacroKeyboard`, UE 5.8):
  - Named pipe client (background thread, automatic reconnect), protocol v2 handshake, events delivered once per frame on the game thread (`UMacroKeyboardSubsystem`, C++ and Blueprint events).
  - Editor context detection (Level Editor / Sequencer / Animation Editor / Animation Blueprint / PIE / Simulate), reported to MacroHub.
  - Per-context bindings: editor shortcuts (routed through Slate), shortcuts looked up by command name, console commands, timeline scrub and play / pause (Sequencer and animation previews).
  - Visual binding panel (Window ▸ Tools ▸ MacroKeyboard, also at the top of its Editor Preferences page): draws the pad from the layout MacroHub sends, edits a control in place when clicked, highlights physical presses and can follow the editor's context; card layout.
  - Status bar entry with a dot showing whether MacroHub and the pad are connected.
  - Per-context knob settings: detents per trigger, speed acceleration and a hold-to-turn multiplier; bindings can have a display name.
  - Per-context pad backlight (can be handed back to MacroHub).
  - Logging off by default: `Log Control Events` in Editor Preferences, `Log Raw Hub Events` in Project Settings.
  - Console commands `MacroKeyboard.OpenPanel [control]` / `MacroKeyboard.Status`.
  - Guide: [`docs/ue-plugin.md`](docs/ue-plugin.md).
- Pad backlight: mode, brightness (6 levels), colour, speed and direction; driven by MacroHub, per layer, or by an application through the `lighting` protocol message.
- W909 2.4G receiver (`VID_B6A4&PID_4101`); the default device match covers cable / 2.4G / Bluetooth and older configurations are completed on start.
- Connection and battery: `/api/state` gains `transport` and `battery`, shown in the web top bar and the tray; the battery comes from the read-only status query (feature 0x81, every 30 s, and right after the pad wakes up).
- Tray icon: the diamond inside a ring open at the bottom that shows the battery; hover for status, left-click opens the setup page, the right-click menu shows status and quits. `--no-tray` turns it off.
- English and Chinese: web UI and help (EN / 中文 switch in the top bar), tray (follows the Windows display language), default configuration (`hub.en.json`), Unreal plugin (follows the editor language; zh-Hans localization target) and all documentation.
- Device protocol notes: [`docs/device-protocol.md`](docs/device-protocol.md).

### Fixed
- Turning the knob while holding it: the W909 firmware sends an all-zero vendor report before every knob step, so the knob press looked released on the first step and its real release was then dropped. MacroHub now treats "released, then a turn within 10 ms" as a turn frame, so knob press and release follow your finger.
- The "Add" button in the "HID devices on this PC" list appends to the device match instead of replacing it.

### Changed
- Protocol v2 `welcome` / `profile` carry each control's `rect` (layout units) and `padConnected`, so clients can draw the pad the way the web UI does.

## 0.1.0 - 2026-09-17

Phase 1: first release of the Windows 11 system layer.

### Added
- W909 macro pad support (stock firmware, no flashing): the vendor HID bitmap is read directly as each key's physical identity and correlated with the low-level keyboard hook, so only the pad's own keystrokes are intercepted; firmware mode switching, knob press and joystick press supported.
- Layered function model: control → layer → function → action (shortcut, text, macro, run program, mouse, switch layer, forward, pass through, block); per-layer handling of unbound keys (inherit / pass through / block).
- Application profiles: switch layers by foreground process (wildcards allowed), override function actions, forward to applications (off / function events / raw controls).
- Input shaping: knob every N detents, minimum interval, pause reset; auto-repeat while held; shared settings with per-layer overrides.
- Knob volume guard: restores the system volume when the knob does something else; no double step when bound to the volume keys.
- App client protocol v2: named pipe `\\.\pipe\MacroHub` and WebSocket; `welcome` / `profile` / `control` / `function` / `context` / `ping`; event sequence numbers and timestamps.
- Web setup page: visual key editing, key learning, layer management, function library search, application profiles, live events, interception test bench, config JSON import / export, built-in help.
- Release and deployment: self-contained single-file package, per-user install / uninstall scripts, start at sign-in (normal or elevated), rolling log file, single instance.

### Security
- The local web API and WebSocket only accept the local page and local non-browser clients (Host and Origin checked), preventing cross-site calls from web pages and DNS rebinding.
- The named pipe only accepts the current Windows user.

[Unreleased]: ../../compare/v0.2.0...HEAD
[0.2.0]: ../../releases/tag/v0.2.0

# Development guide

English | [中文](development.zh-CN.md)

## Environment

| Tool | Version | Used for |
|---|---|---|
| .NET SDK | 10.0 (`global.json` allows newer feature bands of the same major version) | Building, unit tests, publishing |
| Node.js | 22+ | End-to-end tests (no npm dependencies) |
| PowerShell | Windows PowerShell 5.1 or PowerShell 7 | Scripts |
| Windows 11 | — | The keyboard hook, HID and SendInput only work in an interactive desktop session |
| Unreal Engine | 5.8 (source or launcher build) | The Unreal plugin |

## Repository layout

```
.
├─ src/
│  ├─ MacroHub.Core/          Platform-independent logic: configuration model and validation, key chords, vendor bitmap
│  │                          decoding, routing, interception correlation (SuppressionCorrelator), input shaping
│  │                          (ControlStepper), lighting, battery, connection type
│  └─ MacroHub/               The Windows daemon (ASP.NET Core minimal API, WinExe)
│     ├─ Input/               Direct HID reading (PadHidReader), LL keyboard hook and foreground tracking (InputThread),
│     │                       backlight and battery (PadLighting)
│     ├─ Output/              Action execution (SendInput, running programs)
│     ├─ Clients/             App client registry, named pipe server
│     ├─ Audio/               Knob volume guard (Core Audio)
│     ├─ Hosting/             Local-origin check middleware, file log, tray icon
│     ├─ Native/              Win32 / HID P/Invoke
│     ├─ HubEngine.cs         Main event flow: physical signal → routing → interception → execute / forward
│     ├─ Program.cs           Command line, HTTP API, WebSocket
│     ├─ defaults/            Default configuration: hub.json (Chinese) / hub.en.json (English), copied on first start
│     │                       to %APPDATA%\MacroHub\hub.json according to the Windows display language
│     └─ wwwroot/             Web UI (plain JS, no build step), i18n.js (English / Chinese), help help.en.md / help.zh-CN.md
├─ tests/
│  ├─ MacroHub.Core.Tests/    xUnit unit tests
│  └─ e2e/e2e.mjs             End-to-end tests
├─ unreal/MacroKeyboard/      Unreal Engine plugin (runtime + editor modules, localization target MacroKeyboard)
├─ tools/                     Hardware diagnostics and test helpers (see tools/README.md)
├─ scripts/                   run / publish / install / uninstall
├─ docs/                      Architecture, protocol, device protocol, installation, Unreal plugin, development (English / Chinese)
└─ .github/                   CI, release workflow, issue / PR templates
```

## Build and run

```powershell
dotnet build MacroHub.slnx -c Release
.\scripts\run.ps1                 # build Release and start (config %APPDATA%\MacroHub\hub.json), open the page
.\scripts\run.ps1 -Console        # run in this console with the log shown; Ctrl+C quits
.\scripts\run.ps1 -Config .\dev.json   # use a separate development configuration
```

### Command line

| Argument | Default | Meaning |
|---|---|---|
| `--config <path>` | `%APPDATA%\MacroHub\hub.json` | Configuration file; created from `defaults/` when missing (Chinese or English by the Windows display language) |
| `--port <n>` | `17900` | Port of the web UI / API / WebSocket (listens on 127.0.0.1 only) |
| `--pipe <name>` | `MacroHub` | App named pipe `\\.\pipe\<name>`; only one instance per name |
| `--log-dir <path>` | `%LOCALAPPDATA%\MacroHub\logs` | Rolling log (5 MB × 4) |
| `--open` | — | Open the page after starting; if an instance is already running, only open its page |
| `--no-tray` | — | No notification-area icon (headless / service use) |
| `--test-mode` | — | Enables `/api/diag/foreground`, for end-to-end tests only; implies `--no-tray` |

Exit codes: `0` normal, `3` an instance is already running.

## Tests

```powershell
dotnet test MacroHub.slnx -c Release     # unit tests (run in CI)
node tests\e2e\e2e.mjs                   # end-to-end tests (build src/MacroHub and tools/KeyTarget in Debug first)
```

The end-to-end tests start a separate Hub (port 17901, pipe `MacroHub-e2e`, test mode, test configuration; your
configuration and pad are untouched) and the `KeyTarget` window, simulate controls through the HTTP API and check:
routing, shortcut injection, text, macros, hold in sync, layer switching, WebSocket function forwarding, named pipe v2
(handshake, raw controls, context, configuration push, disconnect fallback), knob volume guard, input shaping.

- Routing uses a pinned foreground (test mode), so the logic checks do not depend on what you are doing on the PC.
- Key injection checks need `KeyTarget` to really have focus; when focus is taken they are marked **SKIP** — rerun while idle.
- Only keys carrying MacroHub's injection marker are counted, so typing on your real keyboard causes no false results.
- They need an interactive desktop and so do not run in CI; run them locally before submitting changes to the input / execution paths.

```powershell
dotnet build src\MacroHub -c Debug; dotnet build tools\KeyTarget -c Debug; node tests\e2e\e2e.mjs
```

Physical interception check: start `tools/InputRecorder` first (as an earlier-installed side hook), then start the Hub
and activate the "Interception test · block all" layer, press keys on the pad and count leaks with
`py tools\leak_check.py`; see [tools/README.md](../tools/README.md).

## Configuration file (hub.json)

| Level | Key | Meaning |
|---|---|---|
| Device | `device.match[]` | HID device path substrings; the defaults cover the W909 cable / 2.4G / Bluetooth |
| Device | `device.controls[]` | Control id ↔ hardware signatures: `vendor:<bit>` (physical identity), `key:<VK hex>` (stock key code), `consumer:<usage>` |
| Device | `device.controls[].input` | Input shaping: `stepDetents` (knob every N detents), `minIntervalMs`, `resetMs`, `repeat` / `repeatDelayMs` / `repeatIntervalMs` (auto-repeat); does not affect "forward raw controls" |
| System | `functions[]` | Semantic functions + default action |
| System | `layers[]` | Control → function; unmapped controls follow `fallback` (`inherit` / `passthrough` / `block`); `input` overrides input shaping for the layer; `lighting` is the layer's backlight |
| Application | `apps[]` | `processes` (`*` allowed), `layer` (switch while in front), `overrides` (function → action), `forward`: `off` / `functions` / `controls` (the old `forwardAll: true` migrates to `functions`) |
| Global | `suppression` | `correlate` (default) / `codes` / `off` |
| Global | `unbound` | Unbound controls: `passthrough` / `block` |
| Global | `knobVolumeGuard` | Restore the system volume the knob changed when the knob does something else (default `true`) |
| Global | `lighting` | `enabled` (MacroHub drives the backlight), `default` (mode, brightness 1–6, speed, direction, colour), `followLayer` |

Action types: `keys` (`"Ctrl+Shift+S"`, sequences `"Ctrl+K, Ctrl+C"`, `hold:true` for hold in sync), `text`, `macro`
(keys / text / delayMs steps), `run`, `mouse` (button / hold or wheel / hWheel), `layer` (next / prev / set), `forward`,
`passthrough`, `none`.

Default layers: Stock keypad (pass-through) · Office · UE Editor (used while `UnrealEditor.exe` is in front) · Game (used
while `*-Win64-Shipping.exe` and similar are in front; hold in sync) · Interception test · block all.
`defaults/hub.json` and `hub.en.json` may only differ in display names; the unit test
`EnglishDefaultsDifferOnlyInDisplayNames` checks it.

Saving validates the whole configuration (`HubConfig.Validate`); old fields migrate on load (`HubConfig.Normalize`).

## HTTP API

| Method | Path | Meaning |
|---|---|---|
| GET | `/api/state` | Device, connection (`transport`), battery (`battery`), hook, foreground, layer, clients, interception statistics |
| GET / PUT | `/api/config` | Read / save and apply live (validation failures return 400 + `errors`) |
| POST | `/api/config/validate` · `/api/config/reset[?lang=zh\|en]` | Validate / restore defaults (in that language; default: Windows display language) |
| POST | `/api/simulate` | `{control, phase, tap}` simulate a control (full routing and execution) |
| POST | `/api/execute` | `{action}` run an action directly |
| POST | `/api/layer` | `{op:"next"|"prev"|"set", layer}` |
| POST / DELETE | `/api/learn/{control}` · `/api/learn` | Learn signatures / cancel |
| GET | `/api/lighting` | Backlight configuration, the effective value and what was last written to the pad |
| POST | `/api/lighting/preview` · `/api/lighting/apply` | Preview (written to the pad but not saved) / write again from the configuration |
| POST | `/api/tuning` | `{correlateWaitMs}` how long the hook waits for the physical report |
| DELETE | `/api/stats` | Reset interception statistics |
| GET | `/api/keys` · `/api/devices` | Key names / HID devices on this PC |
| GET | `/api/names` | `[Chinese, English]` pairs of the default display names, so the UI can show untouched defaults in its language |
| GET / POST | `/api/diag/volume` · `/api/diag/volume/arm` | Knob volume guard status / arm a guard window manually (tests) |
| POST | `/api/diag/foreground` | Pin the routing foreground `{pid, process}`, empty object to release; `--test-mode` only |

All requests must come from the local page or a local non-browser client (see [SECURITY.md](../SECURITY.md)).

## Changing the web UI

- The UI is plain HTML / CSS / JS in `src/MacroHub/wwwroot`, copied to the output on build; during development you can copy files into `bin/.../wwwroot` and refresh (static files are not cached).
- **UI text must exist in Chinese and English**: write the Chinese in the code wrapped in `t('中文 {0}', value)` and add the English to the `EN` table in `wwwroot/i18n.js`; static text in `index.html` is translated from the same table at load (paragraphs with mixed markup are replaced wholesale through `data-i18n-html`).
- **User-visible changes update `help.en.md` and `help.zh-CN.md`** (including their changelog sections) and the Unreleased section of `CHANGELOG.md` / `CHANGELOG.zh-CN.md`. Linked headings use `{#id}` anchors, identical in both languages.
- Tray text lives in `Hosting/TrayIcon.cs` as `L("中文", "English")` and follows the Windows display language.

## Unreal plugin

The plugin source is in `unreal/MacroKeyboard`; the user guide is [ue-plugin.md](ue-plugin.md).

```powershell
# 1. Link it into a test project (a directory junction, no administrator rights; one source, both places stay in sync)
New-Item -ItemType Junction -Path "<ProjectDir>\Plugins\MacroKeyboard" -Target "<Repo>\unreal\MacroKeyboard"

# 2. Build (Blueprint project + C++ plugin: build the editor target with -Project)
<Engine>\Engine\Build\BatchFiles\Build.bat UnrealEditor Win64 Development -Project="<Project>.uproject" -WaitMutex

# 3. Start the editor
<Engine>\Engine\Binaries\Win64\UnrealEditor.exe "<Project>.uproject"
```

Debugging:

- Log category `LogMacroKeyboard`; per-key logging is off by default. Turn on `Log Control Events` under Editor Preferences → Plugins → MacroKeyboard (Editor) → Diagnostics, or use `log LogMacroKeyboard Verbose` for a moment.
- Plugin settings: Project Settings → Plugins → MacroKeyboard (pipe name, AppId, enabled, raw event log).
- On the MacroHub side, "App clients" at the top of the web UI should list `unreal-editor` with the context the plugin reports.
- The UE application profile must "forward raw controls" (the default); while the plugin is not connected the Hub falls back to its shortcut layer.
- Without a physical pad, simulate with `POST /api/simulate {"control":"KNOB_CW"}` (UE must be in front, or pin the foreground with `--test-mode`).
- `MacroKeyboard.OpenPanel KNOB_CW` opens the panel with that control selected; the `-culture=zh-Hans` / `-culture=en` command line switches the editor language for one run to check translations.

### Plugin localization

The plugin's source language is English (`LOCTEXT` / `NSLOCTEXT`); Simplified Chinese comes from the localization target
`MacroKeyboard` (`Config/Localization/MacroKeyboard.ini`) and follows the editor language. Besides the text in the
source, property names, tooltips and categories of the settings classes are gathered too.

1. After adding or changing text, rebuild the plugin and run the gather once:
   ```powershell
   cd <ProjectDir>
   <Engine>\Engine\Binaries\Win64\UnrealEditor-Cmd.exe "<Project>.uproject" -run=GatherText `
     -config="Plugins/MacroKeyboard/Config/Localization/MacroKeyboard.ini" -unattended -nullrhi
   ```
2. Fill in `msgstr` for the new entries in `Content/Localization/MacroKeyboard/zh-Hans/MacroKeyboard.po` (`msgctxt` is "namespace,key").
3. Run the command from step 1 again: it imports the translations and writes `zh-Hans/MacroKeyboard.locres`.
4. Commit everything under `Content/Localization/MacroKeyboard` (manifest, archives, po, locres).

## Release process

1. Update `VersionPrefix` in `Directory.Build.props` and `CHANGELOG.md` / `CHANGELOG.zh-CN.md`.
2. Run the unit and end-to-end tests locally.
3. Tag and push: `git tag v0.2.0 && git push origin v0.2.0`.
4. GitHub Actions `release.yml` runs `scripts/publish.ps1` (x64 and ARM64), creates the GitHub Release and uploads the zip and `.sha256`.

Local packaging: `.\scripts\publish.ps1 [-Version 0.2.0] [-Runtime win-arm64] [-SkipTests]`, output in `artifacts/`.

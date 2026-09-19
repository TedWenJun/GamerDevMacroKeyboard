# Unreal plugin guide (MacroKeyboard)

English | [中文](ue-plugin.zh-CN.md)

Makes the macro pad do different things in Unreal Editor depending on **the editor you are working in**: PIE and tool
switching in the Level Editor, frame-by-frame timeline scrubbing on the knob in Sequencer and the animation editors.

- Engine: **UE 5.8** (source or launcher build; this project is tested on a source build)
- Requirement: install and run [MacroHub](installation.md) first; the plugin connects over the named pipe `\\.\pipe\MacroHub`
- Source: [`unreal/MacroKeyboard`](../unreal/MacroKeyboard); building, development and localization are in [development.md](development.md#unreal-plugin)
- Interface language: follows the editor language (Editor Preferences → Region & Language); English and Simplified Chinese

## Installation

1. Copy `unreal/MacroKeyboard` (or create a directory junction) to your project's `Plugins/MacroKeyboard`.
2. Build the editor target once with `-Project` (Blueprint projects too):
   ```powershell
   <EngineDir>\Engine\Build\BatchFiles\Build.bat UnrealEditor Win64 Development -Project="<Project>.uproject" -WaitMutex
   ```
3. Start the editor. The Output Log shows `LogMacroKeyboard: connected to MacroHub …` and a macro pad button appears in the status bar.
4. In MacroHub, "App clients" at the top should list `unreal-editor` with the context the plugin reports.

If the log says `MacroHub is set to forward="functions"`: in MacroHub choose **Applications → Unreal Editor → Forward to
the app client → Forward raw controls**, so the plugin decides what each control does.

## How it works

```
macro pad → MacroHub (recognises the pad, intercepts the stock keys, forwards raw controls by foreground app)
          → plugin (works out which editor is in use → looks up the binding → runs it)
```

- While the plugin is connected, MacroHub stops simulating shortcuts for UE and leaves the decision to the plugin.
- When the plugin is not running (or the editor is closed), MacroHub falls back to its own "UE Editor" layer shortcuts, so the pad keeps working.
- Events only arrive while UE is in front.
- The plugin needs MacroHub running: MacroHub recognises the pad, intercepts its stock keys, reads the battery and writes the backlight.

## Status bar

The macro pad button on the right of the editor's status bar opens the binding panel; its dot shows the connection:
green = pad online, orange = MacroHub connected but no pad found, grey = MacroHub not connected. Hover for details.

## Contexts

| Context id | When |
|---|---|
| `pie` / `simulate` | Playing in Editor / Simulate |
| `sequencer` | The current tab is Sequencer |
| `animation` | The current tab is an animation / skeletal mesh editor |
| `animBlueprint` | The current tab is an Animation Blueprint |
| `levelEditor` | The Level Editor (viewport or its tabs) |
| `editor:<tab id>` | Any other editor; the log prints the id, which you can use in bindings |

Context changes are reported to MacroHub live and shown in its interface (handy for checking the detection).

## Bindings

### Visual panel (recommended)

Open it from the status bar button, **Window ▸ Tools ▸ MacroKeyboard**, the console (`MacroKeyboard.OpenPanel [control]`,
also bound to the knob press by default), or **Editor Preferences → Plugins → MacroKeyboard (Editor)** (the panel sits
at the top of that page and can be opened as its own window with one click).

The panel draws the whole pad from the layout MacroHub sends: key caps, the round knob (turn left / turn right / press
zones) and the round joystick (four directions + press), with the pad lighting on the right.

- Click any key cap, knob zone or joystick direction to edit what it does in the current context; each cap shows its binding, and a `↳` prefix means it is inherited from "All contexts".
- Pressing a physical key highlights it live, which confirms MacroHub recognises it.
- With "Follow editor" ticked, the edited context follows the editor's focus (the panel's own tab is ignored, so it keeps showing the editor you came from); untick it to choose a context yourself.
- Action type, shortcut (click, then press the combination), command name, console command, frames, trigger on release and so on are edited in place below; changes apply immediately and are saved to Editor Preferences.
- **Display name**: replaces the binding summary on the key cap (e.g. show `Alt+H` as "Hide selection"); leave empty for the default. Saved per binding, so each context can use its own name.
- **Knob** (timeline scrub):
  - `Detents per trigger`: the W909 reports 2 steps per click, so 2 means one move per click (the default).
  - `Speed acceleration`: turning quickly moves further per step (×2 under 90 ms between steps, ×5 under 40 ms); turn it off when every click must be exactly one frame (off by default).
  - `Hold-to-turn multiplier`: turning while holding the knob down multiplies each step by this, and releasing goes straight back to normal. Above 1, the knob press's own binding in that context runs on release, and not at all if the knob was turned while held.
  - All saved per binding, so each context has its own.
- **Restore default bindings…** (bottom right): replaces every binding in every context with the plugin's defaults, after asking.

`MacroKeyboard.Status` prints the connection, the detected context and the number of controls and bindings to the log,
which helps when troubleshooting.

### Pad lighting

"Pad lighting" on the right of the panel: with **Take over** ticked the editor controls the backlight (mode, brightness,
colour, speed, direction); untick it to hand it back to MacroHub. By default all contexts share one lighting; tick
**Per context** to give the current context its own, applied whenever it comes to the front.

### Settings list

**Editor Preferences → Plugins → MacroKeyboard (Editor)** keeps the raw list below the panel:

| Field | Meaning |
|---|---|
| Context | Context id; empty means "all contexts", and a binding for a specific context wins |
| Control | Control id: `K1`…`K9`, `K0`, `KDOT`, `KENTER`, `KMINUS`, `KPLUS`, `KSPACE`, `KNOB_CW/CCW/PRESS`, `JOY_UP/DOWN/LEFT/RIGHT/PRESS` |
| Action | See below |
| Amount | Frames per timeline scrub step (the knob direction gives the sign) |
| Detents Per Trigger / Speed Acceleration / Hold Multiplier | Knob settings, see above |
| Display Name | Name shown on the key cap |
| On Release | Trigger on release (default: on press) |

| Action | Does |
|---|---|
| `Chord` | Sends a shortcut inside the editor (routed through Slate, not the operating system, so other programs are unaffected) |
| `UICommand` | Looks up the shortcut currently bound to a command by name and sends it, e.g. `Sequencer` / `TogglePlay`; follows the user's own key bindings |
| `ConsoleCommand` | Runs a console / editor command, e.g. `stat fps` |
| `TimelineScrub` | Moves the Sequencer playhead or the animation preview time |
| `TimelinePlayPause` | Sequencer play / pause; toggles preview playback in the animation editors |
| `None` | Blocks the control (so a context ignores it) |

### Default bindings

| Context | Bindings |
|---|---|
| Level Editor | 1 = PIE, 2 = Simulate, 3 = stop, 4 = Live Coding compile, 5 = save all, 7/8/9 = move / rotate / scale, 0 = focus, Space = content drawer |
| Sequencer | knob = frame-by-frame scrub, Space = play / pause, joystick left / right = previous / next key, joystick press = select range to playhead |
| Animation Editor | knob = frame-by-frame preview scrub, Space = play / pause |
| All contexts | knob = undo / redo (unless overridden above), joystick = arrow keys, ENTER = Enter |
| PIE | 1 = `stat fps`, 2 = `stat unit`, 3 = `show collision` |

Changes apply immediately; no editor restart needed.

## Blueprint access

`UMacroKeyboardSubsystem` (an engine subsystem) exposes:

- Event `OnControlEventBP(Event)`: control id, press / release, kind (key / knob / joystick), part (cw / ccw…), sequence number and timestamp.
- `IsConnected` / `IsPadConnected` / `GetConnectionState` / `DescribeConnection`: connection state.
- `ReportContext(Name, Detail)`: report your own context (for example from your own editor tools).
- `SetLighting(Spec)` / `ResetLighting()`: set the pad's backlight / hand it back to MacroHub.

## Troubleshooting

| Symptom | What to do |
|---|---|
| No `connected to MacroHub` in the log | Make sure MacroHub is running; the pipe name in Project Settings → Plugins → MacroKeyboard must match MacroHub's `--pipe` |
| Connected, but keys do nothing | Make sure UE is in front and the application profile forwards raw controls; turn on logging and look for `no binding in context '…'` |
| The log shows `(not handled)` | That shortcut has no command in the current focus, or the editor window lacks keyboard focus; click the editor window and try again |
| `command 'X.Y' not found` | The command name or its binding context is wrong; Editor Preferences → Keyboard Shortcuts shows which context a command belongs to |
| Lighting does not change | Make sure "Take over" is ticked in the panel and not also on in MacroHub's page (with both on, the last write wins) |
| Want to see every event | Logging is off by default. Editor Preferences → Plugins → MacroKeyboard (Editor) → Diagnostics → **Log Control Events** logs what the editor did with each press (`KNOB_CW [sequencer] -> …`); Project Settings → Plugins → MacroKeyboard → Diagnostics → **Log Raw Hub Events** logs the raw events from MacroHub. When off, the same lines are available with `log LogMacroKeyboard Verbose` |

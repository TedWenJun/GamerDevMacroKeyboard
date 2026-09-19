# MacroHub help

MacroHub is the **system layer** for a macro pad: it turns every physical control on the pad into a configurable
"function", switches automatically with the application in front, and can hand function events to applications (such
as the Unreal Engine plugin) to handle natively. This page follows the features; recent changes are listed under
[Changelog](#changelog) at the end.

> Setup page: <http://127.0.0.1:17900/> · this page: <http://127.0.0.1:17900/help.html> · source: `src/MacroHub/wwwroot/help.en.md` (中文: `help.zh-CN.md`)

---

## Quick start {#quick-start}

1. Start it: installed builds open **MacroHub** from the Start menu (or run `MacroHub.exe --open`); from source, run `.\scripts\run.ps1`. To send shortcuts to elevated windows, run it as administrator (source: `run.ps1 -Elevated`; installed: `install.ps1 -AutoStartElevated`). The setup page opens in your browser.
2. Check the top bar: **Pad online** and **Keyboard hook** should both be green.
3. Click a layer tab to pick the layer to edit → click a key → choose a function on the right.
4. Click **Save & apply** (top right); it takes effect immediately, no restart needed.
5. Click **Set active** to make the pad use that layer.

The interface language follows your browser; the **EN / 中文** button in the top bar switches it.

## Concepts {#concepts}

| Concept | Meaning | Example |
|---|---|---|
| **Control** | One physical input on the pad | `K1` (key 1), `KNOB_CW` (knob turned right), `JOY_PRESS` (joystick pressed) |
| **Function** | A semantic command independent of hardware and applications, with a default action | `ue.play` (Play in Editor, default `Alt+P`) |
| **Layer** | A set of "control → function" mappings; exactly one layer is in effect at a time | Stock keypad, Office, UE Editor |
| **Application profile** | Matches the foreground program; can switch layers, override function actions and forward to the application | Switch to the "UE Editor" layer while `UnrealEditor.exe` is in front |

A key press is handled as: **physical control → function from the current layer → application override (if any) → run
the action / forward to the application**.

The current layer is the one the foreground application's profile names, if it names one; otherwise the **manually
active layer** (set with "Set active" or the "next / previous layer" functions).

## Tour {#tour}

### Top bar {#status-bar}

| Status | Meaning |
|---|---|
| Hub connected | The page's live connection to MacroHub |
| Pad online · USB / 2.4G / Bluetooth | MacroHub has opened the pad's vendor data channel (needed to recognise physical keys), followed by how the pad is connected |
| Battery / Charging | The pad's battery, read every 30 seconds; amber at 20 % and below, red at 10 % and below. On the cable it shows "Charging" |
| Keyboard hook | The system keyboard hook is installed (needed to intercept the pad's stock keys) |
| Interception | The current interception mode, see [Device & interception](#device) |
| Foreground | The foreground process and the application profile it matched |
| Layer | The layer in effect |
| App clients | How many applications are connected to MacroHub over the named pipe or WebSocket (such as the Unreal plugin). While a connected application is in front and its profile forwards, the application handles the pad instead of simulated shortcuts; with none connected functions run as shortcuts as usual. Hover to see each client's channel, protocol version, mode and reported context; click to jump to the test bench list. See [App client protocol](#app-protocol) |
| Not elevated | Shown when MacroHub is not running as administrator; it then cannot send shortcuts to elevated windows |

**Save & apply / Revert** (top right): every change in the page is kept in the page until you save; saving writes the
configuration and applies it.

### Tray icon {#tray}

MacroHub has no window of its own; while it runs it shows an icon in the notification area:

- The **yellow diamond** sits inside a **ring open at the bottom**, which is the battery gauge: it fills clockwise from the lower left in green, turning amber at 20 % and below and red at 10 % and below. Only the grey track means the pad is not connected (or has just connected and has not reported its battery yet).
- Hover for the version, connection, battery and current layer; **left-click** opens the setup page; the **right-click** menu shows the same facts as the top bar and opens the help, the log folder, or quits MacroHub.
- Start with `--no-tray` to hide the icon.

### Layer tabs {#layer-tabs}

- Click a tab to choose the layer you **view and edit** (this does not change the layer in effect).
- The tab marked "active" is the layer in effect.
- **Set active**: makes the layer you are viewing the manually active layer.
- **Layer properties**, **+ New layer**, **Delete layer**: see [Layers](#layers).

### Key view {#key-view}

- Every key shows its label and the function bound in this layer.
- `↳function` (grey) means this layer has no binding and inherits the base layer's.
- `Unbound · Pass through` / `Unbound · Block` show what happens when this layer leaves the key unbound.
- The knob has three zones (turn left / turn right / press) and the joystick five (up / down / left / right / press); click each to edit it.
- Pressing a physical key flashes its position white; an intercepted key flashes its border red.

### Inspector {#inspector}

With a control selected, the right-hand panel shows:

- **Hardware signatures**: the signals that identify the control, see [Learning keys](#learn).
- **Function in this layer**: pick one from the list, or "+ New function…".
- **Simulate a press**: runs the whole path with the saved configuration (the action really runs, in the focused window).
- **Function**: edit its name, category and default action. A function can be shared by several layers; changing its default action changes it everywhere it is used ("used in N places" is shown at the top right).
- **Application overrides**: tick an application to give this function a different action there.
- **Rotation sensitivity / Key trigger**: see [Input settings](#input-settings).

## Layers {#layers}

### New layer {#new-layer}

Click **+ New layer** and fill in:

- **Layer name** and **Colour** (the colour marks the tab and the active outline).
- **Initial bindings**: empty (unbound keys are treated as "inherit from the base layer"), or a copy of all bindings of an existing layer as a starting point.

### Layer properties {#layer-properties}

| Setting | Meaning |
|---|---|
| Layer name / colour | Display only |
| Unbound keys in this layer | **Inherit from the base layer**: use the first layer's binding; **Pass through**: the pad types its stock key as usual; **Block**: nothing happens (good against accidental presses in games or demos). For the base layer, "Use the global setting" applies the "Unbound keys" setting from [Device & interception](#device) |
| Switch to this layer while these applications are in front | Tick application profiles. An application can switch to one layer only; ticking it here replaces its previous choice |
| Move left / right | Changes the order; "next / previous layer" cycles in this order |
| Make base layer | Moves it first. The base layer is where other layers inherit from |
| Duplicate layer | Inserts a copy to the right |
| Internal id | Referenced by application profiles and "switch layer" actions; cannot be changed |

### Delete a layer {#delete-layer}

View the layer and click **Delete layer**. The confirmation lists what is affected: whether it is the base layer or the
active layer, which applications will stop switching to it, and which "switch to this layer" actions stop working. The
deletion takes effect when you save, and can be reverted until then. At least one layer must remain.

### Default layers {#default-layers}

| Layer | Purpose |
|---|---|
| Stock keypad | Base layer; everything passes through, as if MacroHub were not running |
| Office | Copy, paste, undo, screenshot, show desktop, media keys and so on |
| UE Editor | PIE, Simulate, Live Coding, move / rotate / scale tools, view modes; used automatically while `UnrealEditor.exe` is in front |
| Game | Hold-in-sync 1–4, Space, WASD; used automatically while packaged games such as `*-Win64-Shipping.exe` are in front |
| Interception test · block all | Every control blocked, to check interception |

## Input settings {#input-settings}

One physical movement does not have to mean one shortcut. Each control has these settings at the bottom of the inspector:

**Knob turn left / right (rotation sensitivity)**

| Setting | Meaning | Default |
|---|---|---|
| Detents per trigger | The action runs once per N detents, e.g. 3 for switching browser tabs | 1 |
| Minimum interval | At least this many milliseconds between triggers, however fast you turn; 0 = no limit | 0 |
| Pause that clears a partial count | A count that has not reached N is cleared after a pause this long | 800 ms |
| Left and right turns share these settings | Changes are written to both directions | on |

Turning the other way restarts the count.

**Keys, joystick directions, knob press (key trigger)**

| Setting | Meaning | Default |
|---|---|---|
| Auto-repeat while held | Repeats the action while held (the W909 joystick is a four-way switch, so this is "keep pushing") | off |
| Delay before the first repeat / repeat interval | Like keyboard auto-repeat | 400 / 100 ms |
| Minimum interval | Stops rapid presses from triggering repeatedly | 0 |

Auto-repeat does not apply to "hold in sync" shortcuts (they already follow press and release).

**Scope and precedence**:

| Precedence | Source | Meaning |
|---|---|---|
| 1 (highest) | A layer's own settings | Stored in that layer; can differ per layer |
| 2 | Shared settings | Stored on the control itself, **one copy for all layers**: changing the shared settings in any layer changes the same values (the last change wins) and affects every layer without its own settings |
| 3 | Defaults | Every detent, no repeat |

The inspector lists the shared values, which layers use them and which have their own settings (click a layer name to
go there). Switching between "shared" and "own settings" never changes a value: going back to shared removes this
layer's own settings; switching to own settings starts from the current shared values.

**Notes**:

- Input settings only affect local shortcuts and forwarded function events. Raw controls forwarded to an application (such as the Unreal plugin) are sent for every detent and every press; the application decides the sensitivity (a timeline needs frame accuracy, for example).
- Detents that do not trigger still intercept the stock key and still get the [knob volume guard](#volume-guard).
- In "Live events", control events show their progress (e.g. `detents=2/3`) and "not triggered"; repeats show as `repeat`.

## Functions and actions {#functions}

The **Functions** tab lists every function; you can run one to test it, create new ones, or delete them (functions still
used by a layer cannot be deleted).

- **Search** matches name, ID, category, shortcut / action text, where it is used (layer and key names) and application overrides; several words separated by spaces must all match, and matches are highlighted. Press `/` on the Functions tab to focus the search box and `Esc` to clear it.
- **Filter** by category, or by usage (bound to a key / unused / has app overrides). The number of matches is shown on the right.
- **Used in** lists every "layer · key" bound to the function; click one to jump to that layer and key.

| Action type | Meaning | Example |
|---|---|---|
| Shortcut | A key combination; commas mean "then" (pressed in order). "Record" captures it from the keyboard | `Ctrl+Shift+S`, `Ctrl+K, Ctrl+C` |
| Shortcut · hold in sync | Pressed while the control is pressed and released with it (first combination only); for movement and other held actions in games | `W` |
| Type text | Types any Unicode text; line breaks send Enter | `Hello 中文` |
| Macro | Steps: keys / text / delay (milliseconds, up to 10 s per step) | `Ctrl+A` → delay 50 → `Ctrl+C` |
| Run program | A program, file or URL; environment variables are expanded | `calc.exe`, `%USERPROFILE%\Desktop` |
| Mouse | A button (left / right / middle / X1 / X2, can be held) or wheel notches | wheel `+1` |
| Switch layer | Next / previous / a specific layer | knob press → next layer |
| Forward to app only | Only sends the function event to connected app clients; nothing runs locally | handled natively by the Unreal plugin |
| Pass through | The pad types its stock key as usual | — |
| Block | Does nothing | — |

Key names: letters, digits, `F1`–`F24`, `Ctrl` `Shift` `Alt` `Win`, `Enter` `Esc` `Tab` `Space` `Backspace` `Delete`,
arrows `Up` `Down` `Left` `Right`, `Home` `End` `PageUp` `PageDown`, keypad `Num0`–`Num9` `NumAdd` and so on, media keys
`VolumeUp` `VolumeDown` `VolumeMute` `MediaPlayPause`, symbols `-` `=` `[` `]` `;` `'` `,` `.` `/` `` ` ``. The input
box autocompletes them.

> Unreal Engine 5 on Windows does not recognise `F13`–`F24`; avoid them in shortcuts meant for UE.

## Application profiles {#apps}

Managed on the **Applications** tab:

- **Processes**: executable names, comma-separated, `*` allowed, e.g. `*-Win64-Shipping.exe`.
- **Switch to layer while in front**: can also be ticked in the layer's properties.
- **Forward to the app client**: what the application receives once it is connected to MacroHub (see [App client protocol](#app-protocol)):

| Mode | Application connected | Application offline |
|---|---|---|
| Don't forward | Local actions run | Same |
| Forward function events | Receives the function the Hub's layer maps to (e.g. `ue.play`) and handles it; nothing runs locally | Local actions |
| **Forward raw controls** | The Hub's layers are skipped; receives raw controls (e.g. `KNOB_CW`) and decides the function from its own context | Hub layers + local actions |

  The default Unreal Editor / packaged UE game profiles use **forward raw controls**: the same knob scrubs time in Sequencer and the animation editors and does something else elsewhere. That per-editor logic lives in the Unreal plugin, because the Hub cannot see focus inside UE.
- **Overrides**: set per function under "Application overrides" in the key inspector.

## Learning keys {#learn}

A control's **hardware signatures** tell MacroHub how to recognise it:

| Signature | Meaning |
|---|---|
| `vendor:48` | Bit 48 of the vendor bitmap, the key's unique physical identity (unaffected by firmware modes) |
| `key:61` | The key code the stock firmware types (hexadecimal virtual key; here keypad 1) |
| `consumer:0E9` | A consumer usage (here Volume Up) |

After changing devices, toggling NumLock, or when a control does not respond: select it → click **Learn** → press it on
the pad (turn or push the knob / joystick as appropriate). Learned signatures are saved and used immediately.

## Test bench {#test-bench}

- **Physical key interception test**: switch to a layer that intercepts (such as "Interception test · block all") and make it active, click the text box and press keys on the pad. When interception works, no characters appear.
- **Statistics**: physical presses / Hub intercepted / **leaked** (stock keys that got through) / waits and the longest wait. If leaks keep rising, raise "How long the hook waits for the physical report" below (default 8 ms). "Reset" clears the counters.
- **Action test**: runs an action directly, without a key.
- **App clients**: lists the registered applications.

## Device & interception {#device}

### Interception mode {#interception}

| Mode | For | How it works |
|---|---|---|
| **Correlate** (default) | Stock firmware, on the cable or the 2.4G receiver | Reads the pad's physical key reports directly from its vendor channel (about 1–2 ms before the system keyboard hook sees the key); hardware keys arriving shortly after such a press are attributed to the pad and intercepted. Your main keyboard is not affected, whatever the firmware mode |
| **Key codes** | Firmware changed to unique key codes (e.g. F13–F24), or a connection without the vendor channel (e.g. Bluetooth, not verified yet) | Any key code in a control's `key:` signatures counts as the pad |
| **Off** | Debugging | Never intercepts; only runs actions |

### Unbound keys {#unbound}

Whether unbound controls **pass through** or are **blocked** when the base layer uses "Use the global setting".

### Knob volume guard {#volume-guard}

The W909 knob always sends Volume ± in hardware, so Windows changes the system volume directly and the keyboard hook
cannot stop it (in every firmware mode). With the **knob volume guard** on (the default), whenever the knob is bound to
something else, blocked, or forwarded to an application as a raw control, MacroHub puts the volume straight back after
Windows changes it. The on-screen volume flyout may flash.

When the knob is bound to the Volume Up / Down shortcuts themselves, MacroHub does not send them again (which would step
twice) and the guard stays out of the way. The settings page shows the audio device status and how many times the
volume was restored.

### Device matching {#device-match}

"Match" takes substrings of the HID device path (such as `VID_B6A4&PID_4100`); pick from "HID devices on this PC" with
one click. The defaults cover all three W909 connections: cable `VID_B6A4&PID_4100`, 2.4G receiver `VID_B6A4&PID_4101`
and Bluetooth `VID&0002B6A4` / `VID&02B6A4`. Older configurations get the missing entries added on start.

## W909 hardware notes {#hardware}

| Control | Stock behaviour | Notes |
|---|---|---|
| 1–9, 0, `.`, `+`, `-`, ENTER, SPACE | Keypad key codes | Can be intercepted |
| Joystick up / down / left / right | Arrow keys | Can be intercepted |
| Joystick press | Enter | Can be intercepted; has its own physical identity, so it can be bound separately from ENTER |
| Knob press | **Switches the firmware mode**, types nothing | After switching, keys 1–6 type `A`–`F` instead of digits; MacroHub recognises keys by physical identity and is unaffected. The knob press itself is a "pure programmable key" |
| Knob turn left / right | System volume − / + (in every firmware mode) | Cannot be intercepted (Windows handles it directly). When bound to something else, the [knob volume guard](#volume-guard) restores the volume |

**Connections**: the cable (PID 4100) and the 2.4G receiver (PID 4101) are verified; both expose the same data
channels, so keys, knob, lighting and battery all work. Bluetooth is not verified yet; if it lacks the vendor channel,
use the "Key codes" mode.

## Config JSON {#config-json}

The **Config JSON** tab edits the complete configuration directly:

- **Apply to editor**: validates it and loads it into the page (you still need "Save & apply").
- **Export file / Import file**: backups and moving between machines.
- **Restore defaults**: overwrites the saved configuration; cannot be undone.

The configuration lives at `%APPDATA%\MacroHub\hub.json` by default (`--config` picks another file).

## App client protocol {#app-protocol}

For applications such as the Unreal plugin. The current protocol version is **v2**.

### Channels {#channels}

| Channel | Address | Framing | Notes |
|---|---|---|---|
| **Named pipe (recommended)** | `\\.\pipe\MacroHub` | One UTF-8 JSON message per line (ending in `\n`) | No port conflicts; only the current Windows user can connect; a disconnect is noticed immediately; carries only messages for that application. In UE, Core's `FPlatformNamedPipe` works |
| WebSocket | `ws://127.0.0.1:17900/ws` | One JSON message per text frame | Shared with the setup page, so it also receives the page's broadcast events (ignore them) |

Both channels carry identical messages. A local WebSocket round trip measures 0.11 ms (median), so the transport is not
the bottleneck; receive on a background thread and handle messages at the start of each game-thread frame.

### Handshake {#handshake}

```
Application                            MacroHub
 │── connect ──────────────────────► │
 │ ◄── {"type":"hello","role":"hub","protocol":2}
 │── {"type":"hello","role":"app","protocol":2,"app":"unreal-editor","pid":12345,"mode":"editor"} ►│
 │ ◄── {"type":"welcome","protocol":2,"clientId":"…","profile":{…},"controls":[…]}
```

`hello` fields:

| Field | Required | Meaning |
|---|---|---|
| `role` | yes | Always `"app"` |
| `protocol` | recommended | Highest protocol version the client supports; 1 when missing |
| `app` | yes | Application name, for display |
| `pid` | recommended | The client's process id. **With a pid, only that process counts as "in front"** (so two editors, or an editor and a `-game` process, are told apart) |
| `process` | no | Process name, matched when there is no pid |
| `mode` | no | Run mode such as `editor` / `game`, for display and debugging |

`welcome` returns the application profile the process matched (`profile.forward` decides what it receives) and the full
control list (`id`, `label`, `kind`, `part`, `rect`), from which an application can build its own binding UI. After
the configuration is saved, the Hub pushes a `profile` message with the same structure.

### Events (Hub → application) {#events}

Only sent while the application is **in front**; what is sent depends on the profile's "Forward to the app client":

**Raw controls** (`forward = controls`, recommended for the Unreal plugin):

```json
{ "type": "control", "seq": 1042, "t": 83412.613, "control": "KNOB_CW", "phase": "down", "kind": "knob", "part": "cw", "source": "pad" }
```

**Function events** (`forward = functions`, or functions whose action is "Forward to app only"):

```json
{ "type": "function", "seq": 7, "t": 83415.020, "function": "ue.play", "name": "Play in Editor", "control": "K1", "phase": "down", "layer": "unreal", "app": "unreal-editor" }
```

| Field | Meaning |
|---|---|
| `seq` | Increments per connection; a gap means the application read too slowly and the Hub dropped old queued messages (up to 512 are kept per connection) |
| `t` | The Hub's monotonic clock (milliseconds, fractional); use it to measure knob speed for acceleration |
| `phase` | Every action has a `down` and an `up`; each knob detent is one down / up pair |
| `source` | `pad` (physical key) or `simulate` (setup page / tests) |

### Other messages (application → Hub) {#app-messages}

| Message | Purpose |
|---|---|
| `{"type":"context","name":"Sequencer","detail":"LS_Intro"}` | Reports the current context, shown in the setup page (top bar "App clients" and the test bench) |
| `{"type":"ping","t":123}` | Heartbeat; the Hub answers `{"type":"pong","t":<hub>,"echo":123}` |
| `{"type":"simulate","control":"K1","phase":"down"}` | Simulates a control, for debugging |
| `{"type":"lighting","mode":1,"brightness":5,"speed":3,"direction":0,"color":"#ffffff"}` | Sets the pad's backlight while the application is connected; `{"type":"lighting","reset":true}` hands it back to MacroHub |

### Fallback and reconnection {#fallback}

- While an application is offline (not connected, or disconnected), the Hub runs its own layers and local actions (simulated shortcuts) for it, so the pad keeps working without the plugin.
- When the Hub restarts the connection drops; applications should reconnect automatically and send `hello` again.
- A release always goes to the application that received the matching press, even if focus has moved since.

## FAQ {#faq}

**Nothing happens when I press the pad?** Check that "Pad online" and "Keyboard hook" are green at the top; check that
`signal` events appear under "Live events". `→ unknown` means that control's signatures are wrong: see [Learning keys](#learn).

**A shortcut does nothing in one program?** If that program runs as administrator, start MacroHub elevated with
`run.ps1 -Elevated`.

**The stock digits still appear?** Make sure the current layer does not "pass through" that key; check the "Leaked"
count on the test bench and raise the wait time if needed.

**A change in the page has no effect?** Click "Save & apply".

**The volume flyout flashes when the knob switches tabs?** The knob hardware always sends volume, and MacroHub puts the
volume straight back; this is expected, see [Knob volume guard](#volume-guard).

**I changed a function's default action and another layer changed too?** Functions are shared. To change one layer
only, create a new function and bind that.

## Changelog {#changelog}

### Unreleased {#unreleased}
- The interface and the help are available in English and Chinese (**EN / 中文** in the top bar).
- 2.4G receiver supported; the top bar shows how the pad is connected (USB / 2.4G / Bluetooth) and its battery; so does the tray menu.
- Tray icon: the diamond sits in a ring open at the bottom that shows the battery; only the grey track when the pad is not connected.
- Knob held while turning: fixed the knob press being taken for a release on the first detent; press and release now follow your finger.

### 2026-09-17 · 0.1.0
- First release: release package, install / uninstall scripts (Start menu, Installed apps, optional start at sign-in, optionally elevated); see docs/installation.md in the repository.
- Only one instance runs; opening it again from the Start menu opens the running instance's page.
- Log file `%LOCALAPPDATA%\MacroHub\logs\macrohub.log`, for troubleshooting while it runs in the background.
- Security: the web page, API and WebSocket only accept requests from the local page and local non-browser programs; web pages cannot call them cross-site.
- Bottom tabs rearranged: Applications, Functions, Device & interception and Config JSON on the left; Live events and Test bench on the right. Applications opens by default, and the last tab is remembered.
- Functions tab: search (name / ID / shortcut / category / usage), category and usage filters; the "Used in" column links to each usage.
- Input settings clearly separate "shared settings (one for all layers)" from "this layer's own settings", listing the layers that use each.
- New input settings: knob "detents per trigger", minimum interval and pause reset; key / joystick "auto-repeat while held" (first delay, repeat interval). Shared or per layer.
- App client protocol v2: named pipe `\\.\pipe\MacroHub` (recommended); events carry `seq` and timestamp `t`; `hello` can declare `protocol` / `mode`; new `welcome` / `profile` (profile and control list), `context` (application reports its context) and `ping` echo.
- Application profiles forward in three modes: don't forward / forward function events / **forward raw controls**; the UE profiles forward raw controls by default so the Unreal plugin decides per editor. Old "forward all functions" configurations migrate to "forward function events".
- Knob volume guard: restores the system volume when the knob does something else; no double step when the knob is bound to the volume keys.
- Clients are matched to the foreground by pid only (several editors, or an editor and a standalone game process, are told apart).
- Top bar "App clients": hover lists connected applications, click jumps to the test bench list.
- This help page (the "Help" button at the top).
- Layer properties: unbound key handling (inherit / pass through / block), applications that switch to the layer, move left / right, make base layer, duplicate.
- "Delete layer" button, listing the consequences first.
- New layer is an in-page dialog and can copy an existing layer's bindings (fixes clicks doing nothing in embedded browsers).
- Page assets are not cached, so a refresh shows an updated interface.
- Knob press (vendor bit 42) and joystick press (vendor bit 50) recognised; interception by physical identity plus timing, independent of firmware mode; the first key press after start is warmed up so it does not leak.
- Phase one: device recognition, layered functions, application profiles, actions, key learning, test bench, WebSocket app clients.

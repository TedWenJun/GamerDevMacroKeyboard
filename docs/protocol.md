# App client protocol v2

English | [中文](protocol.zh-CN.md)

Applications (such as the Unreal Engine plugin) receive macro pad events from MacroHub through this protocol. The design
decisions behind it are in [architecture.md, section 8](architecture.md#8-responsibilities-macrohub-vs-the-unreal-plugin).

- Protocol version: **2** (`HubEngine.ProtocolVersion`)
- Reference client: `pipeClient()` in [`tests/e2e/e2e.mjs`](../tests/e2e/e2e.mjs) (Node.js, about 40 lines)

## Channels

| Channel | Address | Framing | Notes |
|---|---|---|---|
| **Named pipe (recommended)** | `\\.\pipe\MacroHub` | One UTF-8 JSON message per line (ending in `\n`) | No port conflicts; only the current Windows user can connect; a disconnect is noticed immediately; carries only messages for that application. In UE, Core's `FPlatformNamedPipe` works |
| WebSocket | `ws://127.0.0.1:17900/ws` | One JSON message per text frame | Shared with the setup page, so it also receives the page's broadcast events (ignore them) |

Both channels carry identical messages. A local WebSocket round trip measures 0.11 ms (median), so the transport is not
the bottleneck; receive on a background thread and handle messages at the start of each game-thread frame.

## Handshake

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

`welcome` returns the application profile the process matched (`profile.forward` decides what it receives), the full
control list, and `padConnected` (whether the pad is online) plus `padTransport` (`usb` / `2.4g` / `bluetooth`), from
which an application can build its own binding UI and status display. When the configuration is saved or the pad
connects or disconnects, the Hub pushes a `profile` message with the same structure.

Control fields:

| Field | Meaning |
|---|---|
| `id` | Control id, e.g. `K1`, `KNOB_CW` |
| `label` | Display name |
| `kind` | `key` / `knob` / `joystick` |
| `part` | Zone of a knob or joystick: `cw`, `ccw`, `press`, `up`, `down`, `left`, `right` |
| `rect` | `[x, y, width, height]` in key-width units, to draw the device's real shape |

## Events (Hub → application)

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

## Other messages (application → Hub)

| Message | Purpose |
|---|---|
| `{"type":"context","name":"Sequencer","detail":"LS_Intro"}` | Reports the current context, shown in the setup page (top bar "App clients" and the test bench) |
| `{"type":"ping","t":123}` | Heartbeat; the Hub answers `{"type":"pong","t":<hub>,"echo":123}` |
| `{"type":"simulate","control":"K1","phase":"down"}` | Simulates a control, for debugging |
| `{"type":"lighting","mode":1,"brightness":5,"speed":3,"direction":0,"color":"#ffffff"}` | Sets the pad's backlight: `mode` 1–9, `brightness` 1–6, `speed` 0–5, `direction` 0/1, `color` as `#rrggbb`; out-of-range values are clamped. Written to the pad immediately and kept until something else changes it |
| `{"type":"lighting","reset":true}` | Hands the backlight back: the Hub applies its own configuration again (per layer or the default when it drives the backlight) |

## Fallback and reconnection

- While an application is offline (not connected, or disconnected), the Hub runs its own layers and local actions (simulated shortcuts) for it, so the pad keeps working without the plugin.
- When the Hub restarts the connection drops; applications should reconnect automatically and send `hello` again.
- A release always goes to the application that received the matching press, even if focus has moved since.

## Compatibility rules

- New fields do not bump the protocol version; clients must ignore unknown fields and unknown `type`s.
- Removing fields or changing their meaning bumps `protocol`; the Hub declares its version in `hello` / `welcome`.
- Protocol v1 clients (WebSocket, `function` events only, no `seq` / `t`) keep working.

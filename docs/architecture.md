# W909 macro pad: signal measurements and the Windows 11 system layer

English | [中文](architecture.zh-CN.md)

> Research and design decisions (phase 1, 2026-09-17; 2.4G and backlight added 2026-09-19). Device: SXS-W909
> (firmware `YXT K100 Kbd`), USB cable and 2.4G receiver · OS: Windows 11 Pro 26200

## 1. Summary

| Question | Answer |
|---|---|
| Does the firmware need flashing? | **No.** The stock firmware plus MacroHub on the PC already gives "every physical key → any function" without affecting the main keyboard. |
| What does the stock firmware send? | Digit / symbol keys send **keypad key codes** (Num0–9, Num., Num+, Num-, Enter, Space), the joystick sends arrow keys, the knob sends consumer Volume ±; the knob press **sends no key code at all and only shows up in the vendor bitmap (bit 42)**, so it is a naturally "pure" programmable key. |
| Can the pad be told apart from the main keyboard? | Yes. The **vendor collection FF00** under MI_02 reports a live "physical key bitmap", one bit per physical key; Windows does not own that collection exclusively, so user mode can `ReadFile` it. |
| Can the pad's stock keystrokes be intercepted? | Yes. The vendor bitmap report arrives **1–2 ms before** the `WH_KEYBOARD_LL` callback (128 measurements, median 1 ms, max 2 ms), so the hook can decide and swallow the key. |
| Does the firmware have "modes"? | Yes. **The knob press switches firmware modes**: afterwards keys 1–6 send `A–F` instead of Num1–6 (other keys untested). The vendor bit numbers do not change with the mode, so MacroHub uses the bitmap as the physical identity and intercepts "any hardware key within 25 ms after a physical press", independent of the mode. |
| Can the knob's volume change be intercepted? | **No** (measured). Consumer volume usages are handled by the system without passing through the keyboard hook. The knob can still trigger any function, but the system volume changes as well; the knob volume guard restores it (section 8). |
| Does the often-suggested "Raw Input to identify the device, then block" work? | **No** (measured). `WM_INPUT` always arrives after the LL hook, and once the hook blocks a key there is no `WM_INPUT` at all. |
| Does "bind F13–F24 in UE" work? | **UE does not recognise them.** UE5's `WindowsPlatformInput.cpp` only maps up to F12; F13–F24 have no `EKeys`. UE is served through MacroHub's IPC instead (the phase 2 plugin). |

## 2. HID interfaces

VID `B6A4` / PID `4100` (the 2.4G receiver is PID `4101` with identical collections), a composite device with 3
interfaces and 7 collections:

| Collection | Usage page / usage | Report | Readable in user mode | Purpose |
|---|---|---|---|---|
| MI_00 | 0x01 / 0x02 mouse | 8 B | ✗ owned by Windows | Not seen in use |
| MI_01 | 0x01 / 0x06 keyboard | 9 B boot | ✗ owned by Windows | **Where the key codes come from** |
| MI_02 COL01 | 0x0C / 0x01 consumer | RID 3, 16-bit usage | ✓ | Knob VolumeUp `0xE9` / VolumeDown `0xEA` |
| MI_02 COL02 | 0x01 / 0x80 system | RID 4 | ✓ | Not seen in use |
| MI_02 COL03 | **0xFF00 / 0x01 vendor** | RID 5, 24 B input | ✓ | **Physical key bitmap** |
| MI_02 COL04 | 0xFF01 / 0x01 vendor | RID 6, 40 B feature | ✓ | Vendor configuration channel: backlight writes and battery query, see [device protocol](device-protocol.md) |
| MI_02 COL05 | 0x01 / 0x06 NKRO keyboard | RID 7 bitmap | ✗ | Not seen in use |

`GET_INPUT_REPORT(5)` returns `A1 01 05 01 02 00 …" K100 Kbd"` (a firmware information string).

## 3. Physical key map (vendor bit = payload byte × 8 + bit)

| Key | Vendor bit | Stock key code | | Key | Vendor bit | Stock key code |
|---|---|---|---|---|---|---|
| 1 | 48 | Num1 `0x61` | | 0 | 25 | Num0 `0x60` |
| 2 | 40 | Num2 `0x62` | | . | 17 | NumDecimal `0x6E` |
| 3 | 32 | Num3 `0x63` | | ENTER | 9 | Enter `0x0D` |
| 4 | 24 | Num4 `0x64` | | − | 18 | NumSubtract `0x6D` |
| 5 | 16 | Num5 `0x65` | | + | 26 | NumAdd `0x6B` |
| 6 | 8 | Num6 `0x66` | | SPACE | 34 | Space `0x20` |
| 7 | 49 | Num7 `0x67` | | Joystick up / down / left / right | 82 / 74 / 58 / 66 | Up / Down / Left / Right |
| 8 | 41 | Num8 `0x68` | | Knob right / left | — (consumer) | VolumeUp / VolumeDown |
| 9 | 33 | Num9 `0x69` | | Knob press | 42 | none (types nothing) |
| | | | | Joystick press | 50 | Enter `0x0D` |

The bitmap is laid out as a "column byte × row bit" matrix. The data was recorded with
[`tools/InputRecorder`](../tools/InputRecorder) and analysed with [`tools/analyze_record.py`](../tools/analyze_record.py).
> Note: keypad codes depend on NumLock; with NumLock off the stock codes become arrow keys and so on. "Learn" the key
> again in the web UI if that happens.

## 4. Timing experiments

`tools/HookOrderTest` (SendInput injection) and `tools/InputRecorder` (real hardware) agree:

```
physical press ─┬─► HID class driver ─► vendor collection FF00 ReadFile returns        t0
                │                       consumer collection ReadFile returns           t0
                └─► kbdhid ─► win32k RIT ─► WH_KEYBOARD_LL callback                    t0 + 1–2 ms
                                           └─(hook passes)─► WM_INPUT                  later
                                           └─(hook blocks)─► no WM_INPUT
```

## 5. System layer architecture (MacroHub)

```
┌──────────────── W909 (stock firmware, no flashing) ────────────────┐
│ keyboard MI_01 (keypad codes)  vendor FF00 (bitmap)  consumer (knob) │
└───────┬───────────────────────────┬──────────────────┬──────────────┘
        │ owned by Windows            │ direct ReadFile  │
        ▼                             ▼                  ▼
  WH_KEYBOARD_LL hook ◄── correlate ── PadHidReader (physical signal → control id)
  (only swallows stock keys)               │
        │                                  ▼
        │                        Router: layer → function → application profile
        │                                  │
        │            ┌─────────────────────┼─────────────────────┐
        ▼            ▼                     ▼                     ▼
  main keyboard   ActionExecutor       named pipe / WebSocket   web setup page
  unaffected      SendInput shortcuts  control / function       (127.0.0.1:17900)
                  text/macro/program   → the Unreal plugin
                  /mouse               and other apps
```

**Three-level configuration model** (`hub.json`):

1. **Device level** `device.controls`: control ids (K1…KSPACE, KNOB_CW/CCW/PRESS, JOY_*) ↔ hardware signatures (`vendor:48`, `key:61`, `consumer:0E9`). A different device only changes this level.
2. **System level** `functions` + `layers`: functions are semantic commands independent of hardware and applications (`ue.play`, `edit.copy`…) with a default action; layers map controls to functions.
3. **Application level** `apps`: matched by foreground process (`*` wildcards), can switch layers, override a function's action, or **forward** to a connected app client (handled natively while connected, local shortcuts as a fallback).

**Key design point**: actions are triggered by the *physical signal*; the hook only swallows keys. So controls that type
nothing still work, and a rare interception miss never runs a function twice (the Hub counts and reports "leaks").

**Interception modes** (`suppression`): `correlate` (default; stock firmware, cable and 2.4G) / `codes` (when key codes
are unique, e.g. firmware changed to F13–F24, or a connection without the vendor channel) / `off`.

**Knob turn frames**: for every knob step the firmware first sends an all-zero vendor bitmap and, about 1 ms later, the
volume report. While the knob is held and turned this makes the knob-press bit look released, and the real release
report is then identical to the previous one (so a bitmap diff sees nothing). `PadHidReader` recognises "a knob-press
release followed within 10 ms by a turn" as a turn frame and keeps the press down until an all-zero report arrives that
is not followed by a turn.

## 6. Verification

| Item | Method | Result |
|---|---|---|
| Core logic (chord parsing, bitmap decoding, routing, app overrides, wildcards, layer switching and unbound handling, config validation and migration, interception correlation, leak detection, knob native-effect de-duplication) | xUnit `tests/MacroHub.Core.Tests` | 60/60 pass (2026-09-19) |
| End to end: foreground detection → routing → SendInput; Unicode text; macros; hold in sync; layer switching; WS function forwarding and fallback; named pipe v2 handshake, raw control forwarding (seq/t/kind), context reports, configuration push, disconnect fallback; knob volume guard; invalid configuration rejected | `node tests/e2e/e2e.mjs` (own port / pipe + test mode + KeyTarget window) | pass |
| Device enumeration / HID reading / hook installation | On real hardware | pass |
| Physical interception rate | "Interception test · block all" layer + side-channel recorder | no leaks |

## 7. Known limits and next steps

- **2.4G receiver**: verified (PID `4101`); keys, knob, backlight and battery behave as on the cable. A sleeping pad does not answer the status query, so the Hub asks again right after the next key press wakes it.
- **Bluetooth**: not verified; the default match already includes Bluetooth HID path formats. Without a vendor bitmap channel, use `codes` mode.
- **Elevated windows**: when not elevated, SendInput cannot reach elevated windows (UIPI); run MacroHub as administrator when needed.
- **Vendor configuration channel FF01**: MacroHub only uses it for the backlight and the read-only status query and never changes the pad's key mapping.
- **Unreal plugin**: registers over the named pipe `\\.\pipe\MacroHub` (protocol v2, see [protocol.md](protocol.md)), receives raw control events and maps them per editor context to native APIs / editor commands / PIE, see section 8.

## 8. Responsibilities: MacroHub vs. the Unreal plugin

Inside UE, what a key should do depends on **editor focus**: the same knob should scrub the timeline in Sequencer and
the animation editors but do something else in an Animation Blueprint. Those tabs are mostly Slate widgets in one
process and one window, invisible to MacroHub outside the process. Therefore:

| | MacroHub (system layer) | Unreal plugin (application layer) |
|---|---|---|
| Responsible for | Hardware recognition, stock key interception, dispatch by foreground process, fallback when the plugin is offline, knob volume guard | Working out the context inside UE (editor type, PIE / editor / standalone), context → behaviour bindings, running native APIs |
| Sends to the other | Raw control events `control` (with `seq`, `t`, `kind`, `part`) | The current context `context` (shown in the setup page) |
| Configured in | Web UI: system layer and non-UE applications | UE editor settings: bindings per editor context |
| Executes via | Simulated shortcuts (only as a fallback) | Native APIs, e.g. `ISequencer::SetLocalTime`, timelines via `ITimeSliderController` |

Implementation: the application profile uses `forward = controls` (the UE default). While the plugin is online the Hub
skips its own layers, only forwards raw controls and intercepts the stock keys; while it is offline the Hub falls back
to its "UE Editor" layer shortcuts.

**Transport**: a local WebSocket round trip measures 0.11 ms median / 0.24 ms p99 (2000 samples), so the transport is
not the bottleneck; latency and stability depend on when UE handles the message on the game thread (callbacks of UE's
WebSockets module are dispatched from the game-thread tick, up to a frame late). Hence the **named pipe**
`\\.\pipe\MacroHub` (line-delimited JSON, current user only, no port, disconnects noticed immediately; UE Core ships
`FPlatformNamedPipe`), with the WebSocket kept for the setup page and scripts. On the UE side: a background thread does
blocking reads → a lock-free queue → the game thread dispatches everything at the start of each frame.

**PIE**: PIE runs in the editor process with the same pid, so the plugin tells them apart in-process;
`UnrealEditor.exe -game` is a separate process with its own pid, and once the client registers its pid the Hub matches
it exactly. The PIE viewport swallows simulated shortcuts, so PIE must go through the plugin channel.

**Hardware limit**: the knob sends consumer Volume ± in every firmware mode and the system volume change cannot be
intercepted; the Hub restores the volume within a guard window using Core Audio endpoint notifications
(`knobVolumeGuard`), at the cost of the volume flyout possibly flashing.

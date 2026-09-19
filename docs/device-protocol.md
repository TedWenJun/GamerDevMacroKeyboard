# W909 device protocol

English | [中文](device-protocol.zh-CN.md)

MacroHub talks to the W909 directly; the vendor's software does not need to be installed. This page describes the
device-side interfaces MacroHub uses: key input, backlight control and the status query.

## Device identity

| Connection | Device path contains | Notes |
|---|---|---|
| USB cable | `VID_B6A4&PID_4100` | |
| 2.4G receiver | `VID_B6A4&PID_4101` | Same collections and report formats as the cable |
| Bluetooth | `VID&0002B6A4` (Classic) / `VID&02B6A4` (LE) | Not verified yet |

## Input

| Collection | Content |
|---|---|
| Usage page `0xFF00`, report ID `5` | Vendor bitmap: one bit per physical switch, 1 while pressed. MacroHub uses the bit number as the control's physical identity |
| Consumer Control (`0x0C/0x01`), report ID `3` | The knob sends Volume −/+ (`0x00EA` / `0x00E9`) |
| Keyboard collections | Keypad codes and arrow keys. Windows owns these exclusively; MacroHub correlates them through a low-level keyboard hook |

For every knob step the firmware first sends an all-zero vendor bitmap and, about 1 ms later, the volume report. While
the knob is held and turned, that all-zero report makes the knob-press bit look released. MacroHub treats "released,
then a turn within 10 ms" as a turn frame and keeps the press down until the real release report arrives.

## Vendor feature channel

| Item | Value |
|---|---|
| Collection | `MI_02 COL04`, usage page `0xFF01` / usage `0x01` |
| Report | Feature, report ID `0x06`, 41 bytes (1 report ID byte + 40 bytes payload) |
| Access | User mode `CreateFile` + `HidD_SetFeature` / `HidD_GetFeature`; no driver or elevation needed |

### Frame layout

```
[0]  0x06          report ID
[1]  0x00
[2]  0x01
[3]  command
[4]  sequence      +1 per frame; replies echo it
[5]  0x00 / 0x01   0x00 for block writes, 0x01 for apply
[6]  address low byte
[7]  address high byte
[8]  data length
[9..] data
```

| Command | Meaning |
|---|---|
| `0x09` | Write a lighting block |
| `0x02` | Apply (2 data bytes; not a checksum, `00 00` is accepted) |
| `0x81` | Status query (read-only, see below) |

The same channel carries other commands, including the key remapping table. MacroHub only ever sends the three
commands above and never changes the key mapping.

### Lighting address map

| Address | Length | Content |
|---|---|---|
| `0x00` | 25 | Current mode: `<0x80 + N> FF 01 00…` |
| `0x19 × N` | 25 | Parameter block of mode N |

| N | Mode |
|---|---|
| 1 | Solid |
| 2 | Flowing |
| 3 | Marquee |
| 4 | Breathing |
| 5 | Cycling breath |
| 6 | Tetris |
| 7 | Neon |
| 8 | Rainbow flow |
| 9 | Off (no parameter block) |

Parameter block (25 bytes):

| Offset | Meaning |
|---|---|
| 0 | Speed `0–5` |
| 1 | Direction `0 / 1` (flowing modes) |
| 2 | Brightness `0–5`, six levels, **0 is brightest** |
| 3 | Colour slot enable mask, one bit per slot; `0x7F` = all seven slots |
| 4–24 | Seven colours, one byte each of R, G, B |

Single-colour modes only light the enabled slots, so MacroHub writes the same colour into all seven.

### Write sequence

Each change is three frames, a few milliseconds apart (frames sent back to back are dropped):

1. `0x09` to address `0x00`: select the mode
2. `0x09` to address `0x19 × N`: that mode's parameter block (skipped for Off)
3. `0x02`: apply

### Status query and battery

Send command `0x81` with three zero data bytes, then read the reply with `HidD_GetFeature` about 30 ms later. The reply
echoes the command and sequence number; its three data bytes are:

| Byte | Meaning |
|---|---|
| 0 | Link: `0` cable, `1` 2.4G |
| 1 | Bit 7 = charging, bits 0–6 = battery percent |
| 2 | `0` |

Examples: cable `00 E4 00` (charging, 100 %); 2.4G `01 53 00` (83 %). A sleeping 2.4G pad does not answer; MacroHub
asks again as soon as the next key press wakes it.

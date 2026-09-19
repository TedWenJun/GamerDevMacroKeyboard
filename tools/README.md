# Tools

English | [中文](README.zh-CN.md)

Helper programs for hardware diagnostics and testing (they only talk to your own macro pad). They are not part of
MacroHub and are not shipped in the release package. Findings and data are in
[docs/architecture.md](../docs/architecture.md).

| Tool | Purpose | Run |
|---|---|---|
| `HidProbe` | Lists the HID collections of a VID/PID with usage page, report lengths and button / value capabilities; `--feature` reads the vendor feature / input reports; `--raw [seconds]` prints every raw input report of the pad; `--light mode= color= …` writes the backlight (frame layout in the [device protocol](../docs/device-protocol.md)) | `dotnet run --project tools/HidProbe -- vid_b6a4&pid_4100` |
| `HookOrderTest` | Uses SendInput to check the order of `WH_KEYBOARD_LL` and `WM_INPUT`, and whether Raw Input still arrives after the hook blocks a key | `dotnet run --project tools/HookOrderTest` |
| `InputRecorder` | Passive recorder (never intercepts): records the pad's Raw Input / vendor bitmap / consumer reports in full; for other keyboards it **only records timing, never key content**. Writes `tools/InputRecorder/record.log` | `dotnet run --project tools/InputRecorder -c Release -- 60` (minutes) |
| `KeyTarget` | Foreground window for the end-to-end tests; prints the key messages it receives and marks the ones MacroHub injected | Started by `tests/e2e/e2e.mjs` |
| `analyze_record.py` | Derives the "vendor bit → key code / consumer usage" map and the vendor-report-to-hook latency from a recording | `py tools/analyze_record.py [record.log]` |
| `leak_check.py` | Independent interception leak check (see below) | `py tools/leak_check.py [record.log] [HH:MM:SS]` |

## Checking interception on real hardware

Low-level keyboard hooks run "last installed, first called", so:

1. start `InputRecorder` first (the earlier hook, which only sees keys MacroHub lets through);
2. then start MacroHub and make the "Interception test · block all" layer active;
3. press every control on the pad;
4. run `py tools/leak_check.py`: a hardware key (hook or Raw Input) seen by the recorder within 40 ms after a physical press is a leak.

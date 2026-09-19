# Security policy

English | [中文](SECURITY.zh-CN.md)

## Supported versions

Only the latest release receives security fixes.

## Reporting a vulnerability

Please **do not** report security problems in public issues. Use **Security → Report a vulnerability** (private
vulnerability reporting) on the GitHub repository, describing the impact, steps to reproduce and the affected versions.
We will acknowledge it as soon as possible and credit you once the fix is released (if you wish).

## Security model

MacroHub is a local program running in the current user's session. It needs these capabilities:

| Capability | Used for | Limits |
|---|---|---|
| Global low-level keyboard hook | Intercepting the pad's stock keystrokes | Only decides on keys correlated with the pad's physical reports; never records other keyboards' key content |
| Reading the pad's HID collections | Physical key identity | Only devices matched by the configuration; read-only |
| Writing the pad's vendor feature channel | Backlight and battery | Only three commands are sent: lighting write, apply and a read-only status query; the pad's key mapping is never changed |
| SendInput / running programs | Running the actions you configure | Actions only come from your saved configuration |
| Web UI / API / WebSocket | Configuration and testing | Listens on `127.0.0.1` only; accepts requests whose Host is local and that carry no Origin (local non-browser clients) or MacroHub's own Origin, preventing cross-site calls from web pages and DNS rebinding |
| Named pipe `\\.\pipe\MacroHub` | Application clients | Only the current Windows user can connect |

Known boundaries:

- Other programs running as the same user can use the local API and pipe (with the same rights they already have). A "run program" action in the configuration is therefore equivalent to a command that user can run.
- When MacroHub runs as administrator, the actions it performs are elevated too; only do this when you need it.
- Release packages are not code-signed yet.

# Installation and deployment

English | [中文](installation.zh-CN.md)

## Release packages

Each release on GitHub Releases provides:

| File | Contents |
|---|---|
| `MacroHub-<version>-win-x64.zip` | x64 self-contained single-file program (no .NET install needed) + web UI + install / uninstall scripts |
| `MacroHub-<version>-win-arm64.zip` | ARM64 build (not tested on hardware) |
| `*.zip.sha256` | SHA-256 checksum |

Verify:

```powershell
(Get-FileHash .\MacroHub-0.1.0-win-x64.zip -Algorithm SHA256).Hash.ToLower()   # compare with the .sha256 file
```

Extracted folder:

```
MacroHub-0.1.0-win-x64/
├─ MacroHub.exe        the program (single file)
├─ wwwroot/            web UI and help
├─ defaults/           default configuration hub.json (Chinese) / hub.en.json (English)
├─ install.ps1         install script
├─ uninstall.ps1       uninstall script
└─ README.md / README.zh-CN.md / LICENSE / CHANGELOG.md / CHANGELOG.zh-CN.md
```

## Installing (current user, no administrator rights)

Open PowerShell in the extracted folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -AutoStart
```

The script:

1. stops a MacroHub running from the same location (when upgrading);
2. copies the program to `%LOCALAPPDATA%\Programs\MacroHub`;
3. creates the Start menu shortcut **MacroHub** (opens the setup page, starting MacroHub first if needed);
4. with `-AutoStart`, starts it at sign-in (`HKCU\...\Run`);
5. registers it under "Settings → Apps → Installed apps", where it can be uninstalled;
6. starts MacroHub and opens <http://127.0.0.1:17900/>. While it runs, a tray icon sits in the notification area (its outer ring is the pad's battery); right-click it for status or to quit.

| Parameter | Meaning |
|---|---|
| `-AutoStart` | Start at sign-in |
| `-AutoStartElevated` | Start at sign-in **as administrator** (scheduled task "MacroHub"), to send shortcuts to elevated windows; run the script from an elevated PowerShell |
| `-InstallDir <path>` | Custom install folder |
| `-NoShortcut` | No Start menu shortcut |
| `-NoLaunch` | Don't start after installing |

**Upgrading**: extract the new version and run `install.ps1` again (same parameters); configuration and logs are kept.

**Portable mode**: don't install; run `MacroHub.exe --open` directly.

## Uninstalling

"Settings → Apps → Installed apps → MacroHub → Uninstall", or:

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\MacroHub\uninstall.ps1"
# also delete configuration and logs: add -RemoveUserData
```

Uninstalling stops the program and removes start-up entries (registry and scheduled task), the shortcut, the app
registration and the program folder.

## Data locations

| Content | Location |
|---|---|
| Program | `%LOCALAPPDATA%\Programs\MacroHub` |
| Configuration | `%APPDATA%\MacroHub\hub.json` (created on first start from the Chinese or English defaults, by the Windows display language; export / import backups on the "Config JSON" tab) |
| Log | `%LOCALAPPDATA%\MacroHub\logs\macrohub.log` (5 MB rolling, 3 old files kept) |

## Network and permissions

- Listens only on `127.0.0.1:17900` (web UI, API, WebSocket) and on the named pipe `\\.\pipe\MacroHub`, which only the current user can open; nothing is exposed to the LAN and nothing goes to the internet.
- Installs a global low-level keyboard hook (`WH_KEYBOARD_LL`) to intercept the pad's stock keystrokes; only the pad's own events are recorded, never other keyboards' key content.
- When not elevated, Windows (UIPI) does not let it send keys to elevated windows; use `-AutoStartElevated` or run it as administrator when you need that.
- The first time you run the unsigned program, Windows SmartScreen may warn about an "unknown publisher".

## Troubleshooting

| Symptom | What to do |
|---|---|
| The page does not open | Check the log; make sure nothing else uses port 17900; open MacroHub from the Start menu again |
| "Pad offline" at the top | Check the USB cable or the 2.4G receiver; on "Device & interception" refresh the HID devices and make sure the match list contains the right PID (cable `4100`, receiver `4101`) |
| No battery shown | A sleeping 2.4G pad does not answer; press any key. On the cable it shows "Charging" |
| The pad's stock digits still appear | Make sure the current layer does not "pass through"; check the "Leaked" count on the test bench |
| Shortcuts do nothing in some programs | That program runs as administrator; see "Network and permissions" |
| It exits right after starting | MacroHub is already running (one instance per user); opening it from the Start menu opens the running instance's page |

# Contributing

English | [中文](CONTRIBUTING.zh-CN.md)

Thanks for helping to improve MacroHub! Please read this page and [docs/development.md](docs/development.md) before
submitting changes.

## Reporting issues

- Use the issue templates (bug / feature request).
- For bugs, include the MacroHub version (`version` in `/api/state`), your Windows version, the pad model and how it is connected (USB / 2.4G / Bluetooth), steps to reproduce, and the relevant part of `%LOCALAPPDATA%\MacroHub\logs\macrohub.log`.
- To get a new device supported, include the output of `tools/HidProbe` (HID collections and descriptors) and the signals shown on the "Live events" tab when you press each control.
- Please do not report security problems publicly; see [SECURITY.md](SECURITY.md).

## Workflow

1. Fork and branch from `main`: `feature/<short-name>` or `fix/<short-name>`.
2. Keep changes focused; platform-independent logic belongs in `src/MacroHub.Core`, with unit tests.
3. Before submitting, run locally:
   ```powershell
   dotnet build MacroHub.slnx -c Release
   dotnet test MacroHub.slnx -c Release
   ```
   Changes to the keyboard hook, HID, action execution or the app protocol also need the end-to-end tests (they need an interactive desktop, so CI does not run them):
   ```powershell
   dotnet build src\MacroHub -c Debug; dotnet build tools\KeyTarget -c Debug; node tests\e2e\e2e.mjs
   ```
4. User-visible changes: update both in-app help files, `src/MacroHub/wwwroot/help.en.md` and `help.zh-CN.md` (including their changelog sections), and the Unreleased section of `CHANGELOG.md` / `CHANGELOG.zh-CN.md`.
5. UI text must exist in English and Chinese: the web UI uses `t('中文')` with an English entry in `wwwroot/i18n.js`; the Unreal plugin uses English `LOCTEXT` and the zh-Hans translation is updated as described in the [development guide](docs/development.md#plugin-localization); documentation changes update both `X.md` and `X.zh-CN.md`.
6. Protocol changes: update `docs/protocol.md` and `docs/protocol.zh-CN.md` and follow their compatibility rules.
7. Open a pull request using the template; CI (build + unit tests) must pass.

## Code style

- Follow the repository's `.editorconfig`: C# with 4-space indentation and file-scoped namespaces; 2 spaces in the web UI; the Unreal plugin follows the Epic C++ coding standard (tabs).
- Warnings are errors in CI (`TreatWarningsAsErrors`).
- Comments explain *why* (especially timing and limitations of the Windows input stack), not what the code already says.
- The web UI stays plain HTML / CSS / JS: no build step, no external dependencies.
- Never log or publish the key content of devices other than the macro pad.

## Commit messages

Use a short imperative subject (English or Chinese), e.g. `Add knob detent stepping` / `修复摇杆按下签名`, and explain
motivation and impact in the body.

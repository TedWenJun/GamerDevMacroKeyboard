# 参与贡献

[English](CONTRIBUTING.md) | 中文

感谢你愿意改进 MacroHub！提交前请先阅读本文和 [docs/development.zh-CN.md](docs/development.zh-CN.md)。

## 报告问题

- 使用 Issue 模板（Bug / 功能建议）。
- Bug 请附上：MacroHub 版本（界面 `/api/state` 中的 `version`）、Windows 版本、宏键盘型号与连接方式（USB / 2.4G / 蓝牙）、复现步骤，以及 `%LOCALAPPDATA%\MacroHub\logs\macrohub.log` 的相关片段。
- 支持新设备：请附上 `tools/HidProbe` 的输出（HID 集合与描述符），以及“实时事件”页中按下各控件时出现的信号。
- 安全问题请不要公开提交，见 [SECURITY.zh-CN.md](SECURITY.zh-CN.md)。

## 开发流程

1. Fork 并从 `main` 创建分支：`feature/<简述>` 或 `fix/<简述>`。
2. 保持改动聚焦；平台无关的逻辑放在 `src/MacroHub.Core` 并补充单元测试。
3. 提交前在本机运行：
   ```powershell
   dotnet build MacroHub.slnx -c Release
   dotnet test MacroHub.slnx -c Release
   ```
   涉及键盘钩子、HID、动作执行、应用协议的改动，还需运行端到端测试（需要交互式桌面，CI 不运行）：
   ```powershell
   dotnet build src\MacroHub -c Debug; dotnet build tools\KeyTarget -c Debug; node tests\e2e\e2e.mjs
   ```
4. 用户可见的改动：同时更新两份应用内帮助 `src/MacroHub/wwwroot/help.zh-CN.md` 与 `help.en.md`（含“更新记录”），以及 `CHANGELOG.md` / `CHANGELOG.zh-CN.md` 的 Unreleased。
5. 界面文字必须中英双语：Web 界面用 `t('中文')` 并在 `wwwroot/i18n.js` 补英文；UE 插件用英文 `LOCTEXT`，并按 [开发指南](docs/development.zh-CN.md#插件本地化) 更新 zh-Hans 翻译；文档改动同时更新 `X.md` 与 `X.zh-CN.md`。
6. 协议变更：更新 `docs/protocol.md` 与 `docs/protocol.zh-CN.md`，遵守其中的兼容性约定。
7. 发起 Pull Request，填写模板，CI（构建 + 单元测试）需通过。

## 代码约定

- 遵循仓库 `.editorconfig`：C# 4 空格缩进、文件范围命名空间；Web 界面 2 空格；UE 插件遵循 Epic C++ 编码规范（Tab 缩进）。
- CI 中警告视为错误（`TreatWarningsAsErrors`）。
- 注释说明“为什么”（尤其是 Windows 输入栈的时序与限制），而不是复述代码。
- Web 界面保持原生 HTML/CSS/JS、无构建步骤、无外部依赖。
- 不在日志或事件中记录非宏键盘设备的按键内容。

## 提交信息

建议使用简洁的祈使语气标题（中文或英文均可），例如 `Add knob detent stepping` / `修复摇杆按下签名`，正文说明动机与影响。

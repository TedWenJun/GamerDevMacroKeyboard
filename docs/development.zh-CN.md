# 开发指南

[English](development.md) | 中文

## 环境

| 工具 | 版本 | 用途 |
|---|---|---|
| .NET SDK | 10.0（`global.json` 允许同主版本的更新功能带） | 构建、单元测试、发布 |
| Node.js | 22+ | 端到端测试（无 npm 依赖） |
| PowerShell | Windows PowerShell 5.1 或 PowerShell 7 | 脚本 |
| Windows 11 | — | 键盘钩子、HID、SendInput 只能在交互式桌面会话中运行 |
| Unreal Engine | 5.8（源码版或安装版） | UE 插件 |

## 目录结构

```
.
├─ src/
│  ├─ MacroHub.Core/          平台无关逻辑：配置模型与校验、按键和弦、厂商位图解码、路由、拦截关联
│  │                          （SuppressionCorrelator）、输入设置（ControlStepper）、灯光、电量、连接方式
│  └─ MacroHub/               Windows 守护进程（ASP.NET Core 最小 API，WinExe）
│     ├─ Input/               HID 直接读取（PadHidReader）、LL 键盘钩子与前台跟踪（InputThread）、背光与电量（PadLighting）
│     ├─ Output/              动作执行（SendInput、运行程序）
│     ├─ Clients/             应用客户端注册表、命名管道服务器
│     ├─ Audio/               旋钮音量保护（Core Audio）
│     ├─ Hosting/             本地来源校验中间件、文件日志、托盘图标
│     ├─ Native/              Win32 / HID P/Invoke
│     ├─ HubEngine.cs         事件主流程：物理信号 → 路由 → 拦截 → 执行 / 转发
│     ├─ Program.cs           启动参数、HTTP API、WebSocket
│     ├─ defaults/            默认配置：hub.json（中文）/ hub.en.json（英文），首次启动按 Windows 显示语言复制到 %APPDATA%\MacroHub\hub.json
│     └─ wwwroot/             Web 界面（原生 JS，无构建步骤）、i18n.js（中英文）、帮助 help.zh-CN.md / help.en.md
├─ tests/
│  ├─ MacroHub.Core.Tests/    xUnit 单元测试
│  └─ e2e/e2e.mjs             端到端测试
├─ unreal/MacroKeyboard/      Unreal Engine 插件（Runtime + Editor 模块，本地化目标 MacroKeyboard）
├─ tools/                     硬件诊断与测试辅助工具（见 tools/README.zh-CN.md）
├─ scripts/                   run / publish / install / uninstall
├─ docs/                      架构、协议、设备协议、安装、UE 插件、开发文档（中英文）
└─ .github/                   CI、发布流程、Issue / PR 模板
```

## 构建与运行

```powershell
dotnet build MacroHub.slnx -c Release
.\scripts\run.ps1                 # 构建 Release 并启动（配置 %APPDATA%\MacroHub\hub.json），打开界面
.\scripts\run.ps1 -Console        # 在当前控制台运行并显示日志，Ctrl+C 退出
.\scripts\run.ps1 -Config .\dev.json   # 使用独立的开发配置，避免改动日常配置
```

### 启动参数

| 参数 | 默认 | 说明 |
|---|---|---|
| `--config <path>` | `%APPDATA%\MacroHub\hub.json` | 配置文件，不存在时从 `defaults/` 生成（按 Windows 显示语言选中文或英文） |
| `--port <n>` | `17900` | Web 界面 / API / WebSocket 端口（仅监听 127.0.0.1） |
| `--pipe <name>` | `MacroHub` | 应用命名管道 `\\.\pipe\<name>`；同名只允许运行一个实例 |
| `--log-dir <path>` | `%LOCALAPPDATA%\MacroHub\logs` | 滚动日志（5 MB × 4） |
| `--open` | — | 启动后打开界面；若已有实例在运行则只打开界面 |
| `--no-tray` | — | 不显示托盘图标（无界面 / 服务场景） |
| `--test-mode` | — | 启用 `/api/diag/foreground`，仅供端到端测试；隐含 `--no-tray` |

退出码：`0` 正常，`3` 已有实例在运行。

## 测试

```powershell
dotnet test MacroHub.slnx -c Release     # 单元测试（CI 中运行）
node tests\e2e\e2e.mjs                   # 端到端测试（需先 Debug 构建 src/MacroHub 与 tools/KeyTarget）
```

端到端测试启动一个独立的 Hub（端口 17901、管道 `MacroHub-e2e`、测试模式、测试配置，不会动你的配置和宏键盘），
以及 `KeyTarget` 测试窗口，通过 HTTP API 模拟控件并校验：路由、快捷键注入、文本、宏、按住同步、层切换、
WebSocket 功能转发、命名管道 v2（握手、原始控件、上下文、配置推送、断线回退）、旋钮音量保护、输入设置。

- 路由使用固定前台（测试模式），逻辑检查与你是否在操作电脑无关。
- 按键注入类检查要求 `KeyTarget` 真正持有焦点；焦点被占用时标记为 **SKIP**，空闲时重跑即可验证。
- 只统计带 MacroHub 注入标记的按键，你在真实键盘上的输入不会造成误报。
- 该测试需要交互式桌面，因此不在 CI 中运行，提交涉及输入/执行路径的改动前请在本机运行。

```powershell
dotnet build src\MacroHub -c Debug; dotnet build tools\KeyTarget -c Debug; node tests\e2e\e2e.mjs
```

实物拦截验证：先启动 `tools/InputRecorder`（作为较早安装的旁路钩子），再启动 Hub 并激活“拦截测试·全屏蔽”层，
按宏键盘后用 `py tools\leak_check.py` 统计漏键，详见 [tools/README.zh-CN.md](../tools/README.zh-CN.md)。

## 配置文件（hub.json）

| 层级 | 键 | 说明 |
|---|---|---|
| 设备 | `device.match[]` | HID 设备路径子串；默认覆盖 W909 有线 / 2.4G / 蓝牙 |
| 设备 | `device.controls[]` | 控件 ID ↔ 硬件签名：`vendor:<位号>`（物理身份）、`key:<VK 十六进制>`（出厂键码）、`consumer:<usage>` |
| 设备 | `device.controls[].input` | 输入设置：`stepDetents`（旋钮每 N 格触发）、`minIntervalMs`、`resetMs`、`repeat` / `repeatDelayMs` / `repeatIntervalMs`（按住连发）；不影响“转发原始控件” |
| 系统 | `functions[]` | 语义功能 + 默认动作 |
| 系统 | `layers[]` | 控件 → 功能；未映射的控件按 `fallback`（`inherit` / `passthrough` / `block`）处理；`input` 为本层的控件输入设置覆盖；`lighting` 为本层背光 |
| 应用 | `apps[]` | `processes`（支持 `*`）、`layer`（前台时自动切层）、`overrides`（功能 → 动作）、`forward`：`off` / `functions` / `controls`（旧字段 `forwardAll: true` 自动迁移为 `functions`） |
| 全局 | `suppression` | `correlate`（默认）/ `codes` / `off` |
| 全局 | `unbound` | 未绑定控件：`passthrough` / `block` |
| 全局 | `knobVolumeGuard` | 旋钮用于非音量功能时恢复被硬件改动的系统音量（默认 `true`） |
| 全局 | `lighting` | `enabled`（由 MacroHub 接管背光）、`default`（模式、亮度 1–6、速度、方向、颜色）、`followLayer` |

动作类型：`keys`（`"Ctrl+Shift+S"`、序列 `"Ctrl+K, Ctrl+C"`、`hold:true` 按住同步）、`text`、`macro`（keys/text/delayMs 步骤）、`run`、`mouse`（button/hold 或 wheel/hWheel）、`layer`（next/prev/set）、`forward`、`passthrough`、`none`。

默认层：原厂数字键盘（直通）· 通用办公 · UE 编辑器（`UnrealEditor.exe` 前台自动启用）· 游戏（`*-Win64-Shipping.exe` 等前台自动启用，按住同步）· 拦截测试·全屏蔽。
`defaults/hub.json` 与 `hub.en.json` 只允许显示名称不同，单元测试 `EnglishDefaultsDifferOnlyInDisplayNames` 会检查。

配置保存时整体校验（`HubConfig.Validate`），旧字段在加载时自动迁移（`HubConfig.Normalize`）。

## HTTP API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/state` | 设备、连接方式（`transport`）、电量（`battery`）、钩子、前台、层、客户端、拦截统计 |
| GET / PUT | `/api/config` | 读取 / 保存并热应用（校验失败返回 400 + `errors`） |
| POST | `/api/config/validate` · `/api/config/reset` | 校验 / 恢复默认 |
| POST | `/api/simulate` | `{control, phase, tap}` 模拟控件（走完整路由与执行） |
| POST | `/api/execute` | `{action}` 直接执行动作 |
| POST | `/api/layer` | `{op:"next"|"prev"|"set", layer}` |
| POST / DELETE | `/api/learn/{control}` · `/api/learn` | 学习签名 / 取消 |
| GET | `/api/lighting` | 背光配置、当前生效值、上次写入设备的值 |
| POST | `/api/lighting/preview` · `/api/lighting/apply` | 预览（写入设备但不保存）/ 按配置重新写入 |
| POST | `/api/tuning` | `{correlateWaitMs}` 钩子等待物理报告的时间 |
| DELETE | `/api/stats` | 清零拦截统计 |
| GET | `/api/keys` · `/api/devices` | 可用键名 / 本机 HID 设备 |
| GET / POST | `/api/diag/volume` · `/api/diag/volume/arm` | 旋钮音量保护状态 / 手动触发保护窗口（测试用） |
| POST | `/api/diag/foreground` | 固定路由用的前台应用 `{pid, process}`，空对象取消；仅 `--test-mode` 启用 |

所有请求须来自本机界面或本机非浏览器客户端（见 [SECURITY.zh-CN.md](../SECURITY.zh-CN.md)）。

## 修改 Web 界面

- 界面是 `src/MacroHub/wwwroot` 下的原生 HTML/CSS/JS，构建时复制到输出目录；开发时可直接复制到 `bin/.../wwwroot` 后刷新浏览器（静态文件禁用缓存）。
- **界面文字必须中英双语**：代码里写中文并用 `t('中文 {0}', 值)` 包起来，在 `wwwroot/i18n.js` 的 `EN` 表中补英文；`index.html` 里的静态文字在加载时按同一张表翻译（含混排标记的段落用 `data-i18n-html` 整段替换）。
- **用户可见的改动必须同步更新 `help.zh-CN.md` 与 `help.en.md`**（含“更新记录”），并在 `CHANGELOG.md` / `CHANGELOG.zh-CN.md` 的 Unreleased 中记录。被链接的标题使用 `{#id}` 固定锚点，两种语言保持一致。
- 托盘文字在 `Hosting/TrayIcon.cs` 里用 `L("中文", "English")`，跟随 Windows 显示语言。

## Unreal 插件

插件源码在 `unreal/MacroKeyboard`，使用说明见 [ue-plugin.zh-CN.md](ue-plugin.zh-CN.md)。

```powershell
# 1. 挂到测试工程（目录联接，不需要管理员权限；改一次源码两边同步）
New-Item -ItemType Junction -Path "<工程目录>\Plugins\MacroKeyboard" -Target "<仓库>\unreal\MacroKeyboard"

# 2. 编译（蓝图工程 + C++ 插件：编译带 -Project 的编辑器目标）
<引擎>\Engine\Build\BatchFiles\Build.bat UnrealEditor Win64 Development -Project="<工程>.uproject" -WaitMutex

# 3. 启动编辑器
<引擎>\Engine\Binaries\Win64\UnrealEditor.exe "<工程>.uproject"
```

调试要点：

- 日志类别 `LogMacroKeyboard`；按键日志默认关闭，在编辑器偏好设置 → 插件 → MacroKeyboard (Editor) → Diagnostics 打开 `Log Control Events`，或 `log LogMacroKeyboard Verbose` 临时查看。
- 插件设置：项目设置 → 插件 → MacroKeyboard（管道名、AppId、是否启用、原始事件日志）。
- MacroHub 侧确认：Web 界面顶部“应用客户端”应出现 `unreal-editor`，并显示插件上报的上下文。
- UE 应用档案需为“转发原始控件”（默认已是）；插件未连接时 Hub 回退为快捷键层。
- 没有实体宏键盘时，可用 `POST /api/simulate {"control":"KNOB_CW"}` 模拟（需要 UE 在前台，或用 `--test-mode` 固定前台）。
- `MacroKeyboard.OpenPanel KNOB_CW` 打开面板并选中控件；`-culture=zh-Hans` / `-culture=en` 启动参数可临时切换编辑器语言检查翻译。

### 插件本地化

插件文字的源语言是英文（`LOCTEXT` / `NSLOCTEXT`），简体中文翻译是本地化目标 `MacroKeyboard`（`Config/Localization/MacroKeyboard.ini`），随编辑器语言切换。除了源码里的文字，还会收集设置类的属性名称、提示与分类。

1. 新增或修改文字后，重新编译插件，再运行一次收集：
   ```powershell
   cd <工程目录>
   <引擎>\Engine\Binaries\Win64\UnrealEditor-Cmd.exe "<工程>.uproject" -run=GatherText `
     -config="Plugins/MacroKeyboard/Config/Localization/MacroKeyboard.ini" -unattended -nullrhi
   ```
2. 在 `Content/Localization/MacroKeyboard/zh-Hans/MacroKeyboard.po` 中为新条目填写 `msgstr`（`msgctxt` 为“命名空间,键”）。
3. 再运行一次第 1 步的命令：导入翻译并生成 `zh-Hans/MacroKeyboard.locres`。
4. 提交 `Content/Localization/MacroKeyboard` 下的全部文件（manifest、archive、po、locres）。

## 发布流程

1. 更新 `Directory.Build.props` 的 `VersionPrefix` 与 `CHANGELOG.md` / `CHANGELOG.zh-CN.md`。
2. 本机运行单元测试与端到端测试。
3. 打标签并推送：`git tag v0.2.0 && git push origin v0.2.0`。
4. GitHub Actions `release.yml` 运行 `scripts/publish.ps1`（x64 与 ARM64），创建 GitHub Release 并上传 zip 与 `.sha256`。

本地打包：`.\scripts\publish.ps1 [-Version 0.2.0] [-Runtime win-arm64] [-SkipTests]`，输出到 `artifacts/`。

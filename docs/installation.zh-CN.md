# 安装与部署

[English](installation.md) | 中文

## 发布包

每个版本在 GitHub Releases 提供：

| 文件 | 说明 |
|---|---|
| `MacroHub-<版本>-win-x64.zip` | x64 自包含单文件程序（无需安装 .NET）+ Web 界面 + 安装/卸载脚本 |
| `MacroHub-<版本>-win-arm64.zip` | ARM64 版本（未实测） |
| `*.zip.sha256` | SHA-256 校验值 |

校验：

```powershell
(Get-FileHash .\MacroHub-0.1.0-win-x64.zip -Algorithm SHA256).Hash.ToLower()   # 与 .sha256 文件内容比对
```

解压后目录：

```
MacroHub-0.1.0-win-x64/
├─ MacroHub.exe        主程序（单文件）
├─ wwwroot/            Web 界面与帮助文档
├─ defaults/          默认配置 hub.json（中文）/ hub.en.json（英文）
├─ install.ps1         安装脚本
├─ uninstall.ps1       卸载脚本
└─ README.md / README.zh-CN.md / LICENSE / CHANGELOG.md / CHANGELOG.zh-CN.md
```

## 安装（当前用户，无需管理员）

在解压目录打开 PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -AutoStart
```

安装脚本会：

1. 停止正在运行的同位置 MacroHub（升级时）；
2. 复制程序到 `%LOCALAPPDATA%\Programs\MacroHub`；
3. 创建开始菜单快捷方式 **MacroHub**（打开配置界面，未运行时先启动）；
4. `-AutoStart` 时登录自动启动（`HKCU\...\Run`）；
5. 在“设置 → 应用 → 已安装的应用”中登记，可从那里卸载；
6. 启动 MacroHub 并打开 <http://127.0.0.1:17900/>。运行时任务栏通知区域显示托盘图标（外圈为键盘电量），右键可查看状态或退出。

| 参数 | 说明 |
|---|---|
| `-AutoStart` | 登录时自动启动 |
| `-AutoStartElevated` | 登录时**以管理员权限**自动启动（计划任务 “MacroHub”），用于向管理员权限窗口发送快捷键；需在管理员 PowerShell 中运行安装脚本 |
| `-InstallDir <路径>` | 自定义安装目录 |
| `-NoShortcut` | 不创建开始菜单快捷方式 |
| `-NoLaunch` | 安装后不启动 |

**升级**：解压新版本后再次运行 `install.ps1`（参数同上），配置与日志保留。

**便携模式**：不安装，直接运行 `MacroHub.exe --open`。

## 卸载

“设置 → 应用 → 已安装的应用 → MacroHub → 卸载”，或：

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\MacroHub\uninstall.ps1"
# 同时删除配置和日志：追加 -RemoveUserData
```

卸载会停止程序、移除自启动（注册表与计划任务）、快捷方式、应用登记和程序目录。

## 数据位置

| 内容 | 位置 |
|---|---|
| 程序 | `%LOCALAPPDATA%\Programs\MacroHub` |
| 配置 | `%APPDATA%\MacroHub\hub.json`（首次启动按 Windows 显示语言由中文或英文默认配置生成；可在界面“配置 JSON”页导出/导入备份） |
| 日志 | `%LOCALAPPDATA%\MacroHub\logs\macrohub.log`（5 MB 滚动，保留 3 份旧日志） |

## 网络与权限

- 只监听 `127.0.0.1:17900`（Web 界面、API、WebSocket）和当前用户可访问的命名管道 `\\.\pipe\MacroHub`，不对局域网开放，不访问互联网。
- 安装全局低级键盘钩子（`WH_KEYBOARD_LL`）以拦截宏键盘的原始按键；只记录宏键盘自身的事件，不记录其他键盘的按键内容。
- 以普通权限运行时，Windows（UIPI）不允许向管理员权限窗口发送按键；需要时使用 `-AutoStartElevated` 或以管理员身份运行。
- 首次运行未签名程序时，Windows SmartScreen 可能提示“未知发布者”。

## 排错

| 现象 | 处理 |
|---|---|
| 界面打不开 | 查看日志；确认没有其他程序占用 17900 端口；从开始菜单再次打开 MacroHub |
| 顶部“宏键盘离线” | 检查 USB 连接或 2.4G 接收器；在“设备与拦截”页刷新本机 HID 设备并确认匹配列表包含对应 PID（有线 `4100`、接收器 `4101`） |
| 不显示电量 | 2.4G 模式下键盘休眠时不应答，按一下任意键即可；有线连接时显示“充电中” |
| 宏键盘原本的数字仍会输入 | 确认当前层没有设为“原样输出”；在测试台查看“泄漏”计数 |
| 某些程序里快捷键无效 | 该程序以管理员权限运行，见“网络与权限” |
| 启动后立即退出 | 已有 MacroHub 在运行（每个用户只允许一个实例），从开始菜单打开会直接打开现有实例的界面 |

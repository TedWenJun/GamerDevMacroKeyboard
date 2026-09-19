# 更新记录

[English](CHANGELOG.md) | 中文

本项目的重要变更记录。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。
面向用户的逐项说明见应用内帮助的“更新记录”（[`help.zh-CN.md`](src/MacroHub/wwwroot/help.zh-CN.md)）。

## [Unreleased]

### 新增
- Unreal Engine 插件（阶段二，`unreal/MacroKeyboard`，UE 5.8）：
  - 命名管道客户端（后台线程、自动重连）、协议 v2 握手、事件按帧在游戏线程分发（`UMacroKeyboardSubsystem`，C++ 与蓝图事件）。
  - 编辑器上下文识别（关卡编辑器 / Sequencer / 动画编辑器 / 动画蓝图 / PIE / Simulate）并回报给 MacroHub。
  - 按上下文的绑定：编辑器内快捷键（经 Slate 路由）、按命令名查找快捷键、控制台命令、时间轴拖动与播放/暂停（Sequencer 与动画预览）。
  - 可视化绑定面板（Window ▸ Tools ▸ MacroKeyboard，也嵌在编辑器偏好设置页顶部）：按 MacroHub 下发的布局绘制整块键盘，点击控件就地编辑，按下实体键实时高亮，上下文可跟随编辑器；卡片式布局。
  - 底部状态栏入口，圆点显示 MacroHub 与键盘的连接状态。
  - 旋钮绑定按上下文设置：每几格触发一次、快转加速、按住旋钮旋转倍数；绑定可设“显示名称”。
  - 按上下文的键盘背光（可交还给 MacroHub）。
  - 日志默认关闭：编辑器偏好设置 `Log Control Events`，项目设置 `Log Raw Hub Events`。
  - 控制台命令 `MacroKeyboard.OpenPanel [控件]` / `MacroKeyboard.Status`。
  - 使用说明见 [`docs/ue-plugin.zh-CN.md`](docs/ue-plugin.zh-CN.md)。
- 键盘背光：模式、亮度（6 档）、颜色、速度、方向；可由 MacroHub 接管、按层设置，或由应用通过协议消息 `lighting` 设置。
- W909 2.4G 接收器（`VID_B6A4&PID_4101`）；设备匹配默认覆盖有线 / 2.4G / 蓝牙，旧配置启动时自动补齐。
- 连接方式与电池电量：`/api/state` 新增 `transport`、`battery`，Web 顶部状态与托盘显示；电量来自只读状态查询（Feature 0x81，每 30 秒，键盘唤醒后立即补查）。
- 托盘图标：菱形 + 底部开口的电量环；悬停查看状态，左键打开配置页，右键菜单显示状态并可退出。`--no-tray` 关闭。
- 中英双语：Web 界面与帮助（顶部 EN / 中文 切换）、托盘（跟随 Windows 显示语言）、默认配置（`hub.en.json`）、UE 插件（跟随编辑器语言，zh-Hans 本地化目标）与全部文档。
- 设备协议说明 [`docs/device-protocol.zh-CN.md`](docs/device-protocol.zh-CN.md)。

### 修复
- 按住旋钮旋转时，W909 固件在每格转动前发送全零厂商报告，导致旋钮按下被误判为松开、真正的松开又被丢弃。MacroHub 现在把“松开后 10 ms 内紧跟旋转”的报告视为转动帧，旋钮按下的按下 / 松开与手指一致。
- “本机 HID 设备”列表的“添加”按钮改为追加匹配项，不再覆盖整个匹配列表。

### 变更
- 协议 v2 的 `welcome` / `profile` 附带每个控件的 `rect`（布局单位）与 `padConnected`，客户端可以按与 Web 界面一致的方式绘制设备。

## [0.1.0] - 2026-09-17

阶段一：Windows 11 系统层首个版本。

### 新增
- W909 宏键盘支持（出厂固件，无需刷写）：直接读取厂商 HID 位图作为物理身份，与低级键盘钩子时序关联，只拦截宏键盘的原始按键；支持固件模式切换、旋钮按下与摇杆按下。
- 分层功能模型：控件 → 层 → 功能 → 动作（快捷键、文本、宏、运行程序、鼠标、切换层、转发、原样输出、屏蔽）；层的未绑定处理（继承 / 原样输出 / 屏蔽）。
- 应用档案：按前台进程（支持通配）自动切层、覆盖功能动作、转发给应用（不转发 / 功能事件 / 原始控件）。
- 输入设置：旋钮每 N 格触发、最小触发间隔、停顿清零；按住连发；共用设置与单层覆盖。
- 旋钮音量保护：旋钮用于其他功能时自动恢复系统音量；绑定音量键时不重复发送。
- 应用接入协议 v2：命名管道 `\\.\pipe\MacroHub` 与 WebSocket；`welcome` / `profile` / `control` / `function` / `context` / `ping`；事件序号与时间戳。
- Web 配置界面：按键可视化编辑、学习按键、层管理、功能库检索、应用档案、实时事件、拦截测试台、配置 JSON 导入导出、内置帮助文档。
- 发布与部署：自包含单文件发布包、按用户安装 / 卸载脚本、开机自启（普通或管理员权限）、滚动文件日志、单实例。

### 安全
- 本地 Web 接口与 WebSocket 仅接受本机界面与本机非浏览器客户端（校验 Host 与 Origin），防止网页跨站调用与 DNS 重绑定。
- 命名管道仅允许当前 Windows 用户连接。

[Unreleased]: ../../compare/v0.1.0...HEAD
[0.1.0]: ../../releases/tag/v0.1.0

# MacroHub

[English](README.md) | 中文

**宏键盘系统层 · Windows 11 + Unreal Engine 插件** —— 把宏键盘的每个物理按键、旋钮、摇杆变成可配置的“功能”，按前台应用自动切换，并通过本地协议交给应用（如 Unreal Engine 编辑器）按自身上下文处理。无需刷写键盘固件。

---

## 功能

- **识别宏键盘、不影响主键盘**：直接读取宏键盘的厂商 HID 通道作为“物理身份”，与键盘钩子时序关联，只拦截宏键盘发出的原始按键（实测无漏键，详见 [架构与实测](docs/architecture.zh-CN.md)）。
- **分层与功能**：控件 → 层 → 功能 → 动作。动作支持快捷键、文本、宏序列、运行程序、鼠标、切换层；未绑定按键可继承 / 原样输出 / 屏蔽。
- **按应用切换**：按前台进程自动切层、覆盖功能动作；游戏可用“按住同步”。
- **输入设置**：旋钮“每转几格触发一次”、最小触发间隔；按键 / 摇杆按住连发；可所有层共用或单层覆盖。
- **键盘背光**：模式、亮度、颜色、速度、方向，可按层或按 UE 编辑器上下文切换。
- **连接方式与电量**：支持 USB 有线与 2.4G 接收器，状态栏和托盘图标显示连接方式与电池电量。
- **应用接入协议 v2**：命名管道 `\\.\pipe\MacroHub`（推荐）或 WebSocket；应用接收原始控件事件（带序号、时间戳）并回报自身上下文，离线时自动回退为本地快捷键。
- **Unreal Engine 插件**：按编辑器上下文（关卡编辑器 / Sequencer / 动画编辑器 / 动画蓝图 / PIE）执行编辑器快捷键、命令、控制台命令、时间轴逐帧拖动；可视化绑定面板、状态栏入口、按上下文的背光。
- **Web 配置界面**：`http://127.0.0.1:17900/`，按键可视化编辑、学习按键、功能库检索、实时事件、拦截测试台、内置帮助文档。
- **中英双语**：Web 界面、帮助、托盘、UE 插件与文档均提供中文和英文。

## 支持的硬件

| 设备 | 状态 |
|---|---|
| SXS-W909（固件标识 “YXT K100 Kbd”），USB 有线（PID `4100`） | ✅ 已实测，内置默认配置 |
| 同款 2.4G 接收器（PID `4101`） | ✅ 已实测：按键、旋钮、灯光、电量 |
| 同款蓝牙 | ⚠️ 未验证 |
| 其他宏键盘 | 可在“设备与拦截”里匹配设备、用“学习”录入按键签名；有厂商位图通道时效果最好 |

## 安装

**系统要求**：Windows 11 x64（开发与实测环境）。Windows 10 与 ARM64 理论可用但尚未验证。发布包为自包含程序，无需安装 .NET。

1. 从 [Releases](../../releases) 下载 `MacroHub-<版本>-win-x64.zip` 并解压（可用 `.sha256` 校验）。
2. 在解压目录运行：
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1 -AutoStart
   ```
   安装到 `%LOCALAPPDATA%\Programs\MacroHub`（当前用户，无需管理员），创建开始菜单快捷方式，登录时自动启动，并打开配置界面。
3. 也可以不安装，直接运行解压目录里的 `MacroHub.exe --open`（便携模式）。

更多选项（管理员权限自启、卸载、数据位置、排错）见 [安装与部署](docs/installation.zh-CN.md)。UE 插件的安装见 [UE 插件使用说明](docs/ue-plugin.zh-CN.md)。

## 使用

打开 <http://127.0.0.1:17900/> —— 界面右上角 **帮助** 是完整的使用说明（源文件 [`help.zh-CN.md`](src/MacroHub/wwwroot/help.zh-CN.md) / [`help.en.md`](src/MacroHub/wwwroot/help.en.md)）。

默认配置提供 5 个层：原厂数字键盘（直通）、通用办公、UE 编辑器（`UnrealEditor.exe` 前台自动启用）、游戏、拦截测试。

## 架构

```
 宏键盘（出厂固件，有线或 2.4G）
   ├─ 键盘集合（小键盘码，系统独占） ──► WH_KEYBOARD_LL 钩子 ◄──时序关联──┐
   ├─ 厂商集合 FF00（物理按键位图）  ─────────────► HID 读取（物理身份）──┤
   ├─ Consumer 集合（旋钮音量）      ─────────────►                     │
   └─ 厂商 Feature 通道 FF01 ◄─── 背光写入 / 电量查询                    ▼
                                            路由：层 → 功能 → 应用档案 → 输入设置
                                                   │             │              │
                                          本地动作 SendInput   命名管道 / WS     Web 界面 / 托盘
                                                              （UE 插件等应用）
```

设计取舍与实测数据见 [docs/architecture.zh-CN.md](docs/architecture.zh-CN.md)；应用接入协议见 [docs/protocol.zh-CN.md](docs/protocol.zh-CN.md)；设备协议见 [docs/device-protocol.zh-CN.md](docs/device-protocol.zh-CN.md)。

## 从源码构建

需要 [.NET SDK 10](https://dotnet.microsoft.com/download)，端到端测试另需 Node.js 22+；UE 插件需要 Unreal Engine 5.8。

```powershell
dotnet build MacroHub.slnx -c Release
dotnet test MacroHub.slnx -c Release
.\scripts\run.ps1            # 构建并启动，打开配置界面
.\scripts\publish.ps1        # 生成发布包 artifacts\MacroHub-<版本>-win-x64.zip
```

目录结构、测试（含端到端）、诊断工具、UE 插件的本地化与发布流程见 [docs/development.zh-CN.md](docs/development.zh-CN.md)。欢迎贡献，请先阅读 [CONTRIBUTING.zh-CN.md](CONTRIBUTING.zh-CN.md)。

## 路线图

- [x] 阶段一：Windows 系统层（设备识别与拦截、分层功能、应用档案、输入设置、Web 配置与测试、应用接入协议 v2、安装部署）
- [x] 阶段二：Unreal Engine 插件（管道客户端、编辑器上下文识别、编辑器动作、Sequencer 与动画时间轴、可视化绑定面板、状态栏、背光）
- [x] 2.4G 接收器、电量显示、托盘图标、中英双语
- [ ] 蓝牙模式验证；PIE / 打包游戏的 Enhanced Input 注入；更多设备预设

## 安全

MacroHub 会安装全局键盘钩子并在本机 `127.0.0.1` 提供不需要登录的 Web 界面，已限制只接受本机界面与非浏览器本地客户端的请求。安全模型与漏洞报告方式见 [SECURITY.zh-CN.md](SECURITY.zh-CN.md)。

## 许可证

[MIT](LICENSE)

## 声明

本项目与键盘制造商无关。文中出现的产品名称、型号仅用于说明兼容性，归各自所有者所有。

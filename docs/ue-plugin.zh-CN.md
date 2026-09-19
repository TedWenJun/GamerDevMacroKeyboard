# UE 插件使用说明（MacroKeyboard）

[English](ue-plugin.md) | 中文

让宏键盘在 Unreal Editor 里按**当前正在操作的编辑器**执行不同功能：关卡编辑器里是 PIE / 工具切换，Sequencer 和动画编辑器里旋钮变成时间轴逐帧拖动。

- 适用版本：**UE 5.8**（源码版或安装版皆可，本项目在源码版验证）
- 前提：先装好并运行 [MacroHub](installation.zh-CN.md)；插件通过命名管道 `\\.\pipe\MacroHub` 连接
- 插件源码：[`unreal/MacroKeyboard`](../unreal/MacroKeyboard)，开发、编译与本地化见 [development.zh-CN.md](development.zh-CN.md#unreal-插件)
- 界面语言：跟随编辑器语言（编辑器偏好设置 → 区域和语言），提供英文与简体中文

## 安装

1. 把 `unreal/MacroKeyboard` 复制（或建立目录联接）到工程的 `Plugins/MacroKeyboard`。
2. 用带 `-Project` 的编辑器目标编译一次（蓝图工程也适用）：
   ```powershell
   <引擎目录>\Engine\Build\BatchFiles\Build.bat UnrealEditor Win64 Development -Project="<工程>.uproject" -WaitMutex
   ```
3. 启动编辑器。Output Log 里会出现 `LogMacroKeyboard: connected to MacroHub …`，底部状态栏出现宏键盘按钮。
4. 在 MacroHub 界面顶部的“应用客户端”应看到 `unreal-editor`，并显示插件上报的上下文。

如果日志提示 `MacroHub is set to forward="functions"`：在 MacroHub 的 **应用档案 → Unreal Editor → 转发给应用客户端** 选择 **转发原始控件**，插件才能自行决定每个控件的功能。

## 工作方式

```
宏键盘 → MacroHub（识别、拦截原始按键、按前台应用转发原始控件）
        → 插件（判断当前是哪个编辑器 → 查绑定 → 执行）
```

- 插件在线时，MacroHub 不再为 UE 模拟快捷键，交给插件决定。
- 插件未启动（或编辑器没开）时，MacroHub 回退到它自己的“UE 编辑器”层快捷键，宏键盘依然可用。
- 事件只在 UE 处于前台时才会送达。
- 插件依赖 MacroHub 运行：它负责识别宏键盘、拦截原始按键、读取电量与写入背光。

## 状态栏

编辑器底部状态栏右侧的宏键盘按钮：点击打开绑定面板；右下角圆点表示连接状态——绿色 = 键盘在线，橙色 = MacroHub 已连接但没检测到键盘，灰色 = 未连接 MacroHub。悬停查看详情。

## 上下文

| 上下文 id | 何时生效 |
|---|---|
| `pie` / `simulate` | 正在 PIE / Simulate |
| `sequencer` | 当前标签页是 Sequencer |
| `animation` | 当前标签页是动画 / 骨骼网格编辑器 |
| `animBlueprint` | 当前标签页是动画蓝图 |
| `levelEditor` | 关卡编辑器（视口或其标签页） |
| `editor:<标签页 id>` | 其他编辑器；日志里会打印该 id，可直接用它写绑定 |

上下文变化会实时上报给 MacroHub，在其界面上可以看到（便于确认判断是否正确）。

## 绑定设置

### 可视化面板（推荐）

打开方式：状态栏的宏键盘按钮、**Window ▸ Tools ▸ MacroKeyboard**、控制台 `MacroKeyboard.OpenPanel [控件]`（默认也绑定到旋钮按下），或 **编辑器偏好设置 → 插件 → MacroKeyboard (Editor)**（面板嵌在设置页顶部，可一键在独立窗口中打开）。

面板按 MacroHub 下发的设备布局绘制整块宏键盘：键帽、圆形旋钮（左旋 / 右旋 / 按下三个扇区）、圆形摇杆（四向 + 按下），右侧是键盘灯光。

- 点击任意键帽、旋钮扇区或摇杆方向即可编辑它在当前上下文中的功能；键帽上直接显示该控件的绑定摘要，`↳` 前缀表示继承自“所有上下文”。
- 按下实体按键时，对应位置会实时高亮，可用来确认 MacroHub 的识别是否正确。
- 顶部“跟随编辑器”勾选后，编辑的上下文跟随当前焦点自动切换（面板自身的标签页不计入，仍显示切换前的编辑器）；取消勾选可手动选择上下文。
- 动作类型、快捷键（点击后直接按组合键录制）、命令名、控制台命令、帧数、松开触发等都在下方就地编辑，改完立即生效并写入编辑器偏好设置。
- **显示名称**：覆盖键帽上的绑定摘要（例如把 `Alt+H` 显示成“隐藏选中”）；留空恢复默认。按绑定保存，不同上下文可以各起各的名字。
- **旋钮**（时间轴拖动）：
  - `每几格触发一次`：W909 每个手感档位上报 2 格，填 2 即转一档移动一次（默认）。
  - `快转加速`：快速转动时每次多走几帧（格间隔 < 90 ms ×2，< 40 ms ×5）；要求严格一档一帧时关闭（默认关）。
  - `按住旋转倍数`：按住旋钮再转时每次移动乘以该倍数，松开立即恢复；大于 1 时该上下文里旋钮按下自己的绑定改为松开时触发，按住期间转过则不触发。
  - 都按绑定保存，不同上下文各自独立。
- **恢复默认绑定…**（右下角）：用插件默认绑定替换所有上下文的全部绑定，执行前会确认。

`MacroKeyboard.Status` 会在日志里打印当前连接状态、识别到的上下文、控件数与绑定数，排错时很有用。

### 键盘灯光

面板右侧的“键盘灯光”：勾选 **接管** 后由编辑器控制背光（模式、亮度、颜色、速度、方向），取消后交还给 MacroHub。默认所有上下文共用一套灯光；勾选 **本上下文独立** 可让当前上下文使用自己的灯光，切到它时自动应用。

### 偏好设置列表

**编辑器偏好设置 → 插件 → MacroKeyboard (Editor)** 在面板下方保留原始列表：

| 字段 | 说明 |
|---|---|
| Context | 上下文 id；留空表示“所有上下文”，具体上下文的绑定优先 |
| Control | 控件 id：`K1`…`K9`、`K0`、`KDOT`、`KENTER`、`KMINUS`、`KPLUS`、`KSPACE`、`KNOB_CW/CCW/PRESS`、`JOY_UP/DOWN/LEFT/RIGHT/PRESS` |
| Action | 见下表 |
| Amount | 时间轴拖动的帧数（旋钮方向决定正负） |
| Detents Per Trigger / Speed Acceleration / Hold Multiplier | 旋钮设置，见上 |
| Display Name | 键帽上显示的名称 |
| On Release | 在松开时触发（默认按下时触发） |

| Action | 作用 |
|---|---|
| `Chord` | 在编辑器内发送快捷键（走 Slate 路由，不经过操作系统，不会打扰其他程序） |
| `UICommand` | 按命令名查找用户当前绑定的快捷键再发送，例如 `Sequencer` / `TogglePlay`；用户改过快捷键也能跟着走 |
| `ConsoleCommand` | 执行控制台 / 编辑器命令，例如 `stat fps` |
| `TimelineScrub` | 拖动 Sequencer 播放头或动画预览时间 |
| `TimelinePlayPause` | Sequencer 播放/暂停；动画编辑器切换预览播放 |
| `None` | 屏蔽（用来让某个上下文不响应某个控件） |

### 默认绑定

| 上下文 | 绑定 |
|---|---|
| 关卡编辑器 | 1=PIE、2=Simulate、3=停止、4=Live Coding 编译、5=全部保存、7/8/9=移动/旋转/缩放、0=聚焦、空格=内容侧滑菜单 |
| Sequencer | 旋钮=逐帧拖动时间轴、空格=播放/暂停、摇杆左右=上一个/下一个关键帧、摇杆按下=选择范围到播放头 |
| 动画编辑器 | 旋钮=逐帧拖动预览、空格=播放/暂停 |
| 所有上下文 | 旋钮=撤销/重做（未被上面覆盖时）、摇杆=方向键、ENTER=回车 |
| PIE | 1=`stat fps`、2=`stat unit`、3=`show collision` |

修改后立即生效，无需重启编辑器。

## 蓝图接入

`UMacroKeyboardSubsystem`（Engine Subsystem）暴露：

- 事件 `OnControlEventBP(Event)`：控件 id、按下/松开、类型（key/knob/joystick）、方向（cw/ccw…）、序号与时间戳。
- `IsConnected` / `IsPadConnected` / `GetConnectionState` / `DescribeConnection`：连接状态。
- `ReportContext(Name, Detail)`：自定义上报上下文（例如你自己的编辑器工具）。
- `SetLighting(Spec)` / `ResetLighting()`：设置键盘背光 / 交还给 MacroHub。

## 排错

| 现象 | 处理 |
|---|---|
| 日志没有 `connected to MacroHub` | 确认 MacroHub 正在运行；项目设置 → 插件 → MacroKeyboard 里的管道名与 MacroHub 的 `--pipe` 一致 |
| 连上了但按键没反应 | 确认 UE 在前台；确认应用档案是“转发原始控件”；打开日志看是否有 `no binding in context '…'` |
| 日志显示 `(not handled)` | 该快捷键在当前焦点下没有对应命令，或编辑器窗口没有键盘焦点；点一下编辑器窗口再试 |
| `command 'X.Y' not found` | 命令名或绑定上下文写错；在 编辑器偏好设置 → 键盘快捷键 里可以查到命令所属上下文 |
| 灯光不变 | 确认面板上勾选了“接管”，且 MacroHub 界面的“接管”没有同时打开（两边都接管时以最后写入的为准） |
| 想看每个事件 | 日志默认关闭。编辑器偏好设置 → 插件 → MacroKeyboard (Editor) → Diagnostics → **Log Control Events**：记录编辑器对每个按键执行了什么（`KNOB_CW [sequencer] -> …`）；项目设置 → 插件 → MacroKeyboard → Diagnostics → **Log Raw Hub Events**：记录从 MacroHub 收到的原始事件。关闭时同样的内容仍可用 `log LogMacroKeyboard Verbose` 临时查看 |

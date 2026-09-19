# 应用接入协议 v2

[English](protocol.md) | 中文

应用（如 Unreal Engine 插件）通过本协议从 MacroHub 接收宏键盘事件。与协议相关的设计取舍见 [architecture.zh-CN.md 第 8 节](architecture.zh-CN.md#8-macrohub-与-ue-插件的职责划分)。

- 协议版本：**2**（`HubEngine.ProtocolVersion`）
- 参考客户端：[`tests/e2e/e2e.mjs`](../tests/e2e/e2e.mjs) 中的 `pipeClient()`（Node.js，约 40 行）

## 通道

| 通道 | 地址 | 分帧 | 说明 |
|---|---|---|---|
| **命名管道（推荐）** | `\\.\pipe\MacroHub` | 每行一条 UTF-8 JSON（`\n` 结尾） | 无端口冲突；仅当前 Windows 用户可连接；断开立即可知；只收发给该应用的消息。UE 端可用 Core 自带的 `FPlatformNamedPipe` |
| WebSocket | `ws://127.0.0.1:17900/ws` | 每个文本帧一条 JSON | 与配置界面共用，会额外收到界面广播事件（可忽略） |

两个通道的消息完全相同。实测本机 WebSocket 往返中位 0.11 ms，传输本身不是瓶颈；应用端应在后台线程收消息、在游戏线程每帧开头统一处理。

## 握手

```
应用                                  MacroHub
 │── 连接 ─────────────────────────► │
 │ ◄── {"type":"hello","role":"hub","protocol":2}
 │── {"type":"hello","role":"app","protocol":2,"app":"unreal-editor","pid":12345,"mode":"editor"} ►│
 │ ◄── {"type":"welcome","protocol":2,"clientId":"…","profile":{…},"controls":[…]}
```

`hello` 字段：

| 字段 | 必需 | 说明 |
|---|---|---|
| `role` | 是 | 固定 `"app"` |
| `protocol` | 建议 | 客户端支持的协议版本，缺省视为 1 |
| `app` | 是 | 应用标识，显示用 |
| `pid` | 建议 | 本进程 pid。**有 pid 时只按 pid 匹配前台**（同时开两个编辑器、或编辑器与 `-game` 独立进程都能区分） |
| `process` | 否 | 进程名；无 pid 时按进程名匹配 |
| `mode` | 否 | 运行模式，如 `editor` / `game`，显示与调试用 |

`welcome` 返回该进程匹配到的应用档案（`profile.forward` 决定会收到什么）、全部控件列表，以及 `padConnected`（键盘是否在线）与 `padTransport`（`usb` / `2.4g` / `bluetooth`），应用可据此生成自己的绑定界面和状态显示。配置保存或键盘连接状态变化后 Hub 会推送同结构的 `profile` 消息。

控件字段：

| 字段 | 说明 |
|---|---|
| `id` | 控件 id，例如 `K1`、`KNOB_CW` |
| `label` | 显示名 |
| `kind` | `key` / `knob` / `joystick` |
| `part` | 旋钮与摇杆的分区：`cw`、`ccw`、`press`、`up`、`down`、`left`、`right` |
| `rect` | `[x, y, 宽, 高]`，以按键宽度为单位的布局矩形，用于按设备实际外形绘制界面 |

## 事件（Hub → 应用）

只有当应用**在前台**时才会收到，内容由应用档案的“转发给应用客户端”决定：

**原始控件**（`forward = controls`，UE 插件推荐）：

```json
{ "type": "control", "seq": 1042, "t": 83412.613, "control": "KNOB_CW", "phase": "down", "kind": "knob", "part": "cw", "source": "pad" }
```

**功能事件**（`forward = functions`，或功能动作为“仅转发给应用”）：

```json
{ "type": "function", "seq": 7, "t": 83415.020, "function": "ue.play", "name": "PIE 运行", "control": "K1", "phase": "down", "layer": "unreal", "app": "unreal-editor" }
```

| 字段 | 说明 |
|---|---|
| `seq` | 每个连接独立递增的序号；出现跳号说明应用读取过慢、Hub 丢弃了积压的旧消息（每连接最多缓存 512 条） |
| `t` | Hub 单调时钟（毫秒，含小数），可用来计算旋钮转速做加速滑动 |
| `phase` | 每次操作都有 `down` 和 `up`；旋钮每转一格是一对 down/up |
| `source` | `pad`（实体按键）、`simulate`（界面/测试模拟） |

## 其他消息（应用 → Hub）

| 消息 | 作用 |
|---|---|
| `{"type":"context","name":"Sequencer","detail":"LS_Intro"}` | 回报当前上下文，显示在配置界面（顶部“应用客户端”与测试台） |
| `{"type":"ping","t":123}` | 心跳，Hub 回复 `{"type":"pong","t":<hub>,"echo":123}` |
| `{"type":"simulate","control":"K1","phase":"down"}` | 模拟控件，调试用 |
| `{"type":"lighting","mode":1,"brightness":5,"speed":3,"direction":0,"color":"#ffffff"}` | 设置键盘背光：`mode` 1–9、`brightness` 1–6、`speed` 0–5、`direction` 0/1、`color` 为 `#rrggbb`；超出范围的值会被截断。立即写入设备，直到其他来源改变它 |
| `{"type":"lighting","reset":true}` | 交还背光：Hub 重新应用自己的配置（接管时按层或默认灯光） |

## 回退与重连

- 应用离线（未连接或已断开）时，Hub 对该应用按自己的层执行本地动作（模拟快捷键），保证插件没启动也能用。
- Hub 重启后连接会断开，应用应自动重连并重新发送 `hello`。
- 松开事件总是发给收到对应按下事件的那个应用，即使期间焦点已切走。

## 兼容性约定

- 新增字段不升级协议版本；客户端必须忽略未知字段和未知 `type`。
- 删除或改变已有字段语义时升级 `protocol`，Hub 在 `hello` / `welcome` 中声明自身版本。
- 协议 v1（WebSocket、仅 `function` 事件、无 `seq`/`t`）的客户端仍可工作。

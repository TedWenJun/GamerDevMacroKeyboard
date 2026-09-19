# 工具

[English](README.md) | 中文

硬件诊断与测试用的辅助程序（只访问你自己的宏键盘）。它们不是 MacroHub 的一部分，也不随发布包分发。
结论与数据见 [docs/architecture.zh-CN.md](../docs/architecture.zh-CN.md)。

| 工具 | 用途 | 运行 |
|---|---|---|
| `HidProbe` | 枚举指定 VID/PID 的 HID 集合，打印 Usage Page、报告长度、按钮/数值能力；`--feature` 读取厂商 Feature/Input 报告；`--raw [秒]` 打印宏键盘的每一份原始输入报告；`--light mode= color= …` 写入背光（帧格式见 [设备协议](../docs/device-protocol.zh-CN.md)） | `dotnet run --project tools/HidProbe -- vid_b6a4&pid_4100` |
| `HookOrderTest` | 用 SendInput 验证 `WH_KEYBOARD_LL` 与 `WM_INPUT` 的先后顺序，以及钩子拦截后 Raw Input 是否还会产生 | `dotnet run --project tools/HookOrderTest` |
| `InputRecorder` | 被动记录器（从不拦截）：完整记录宏键盘的 Raw Input / 厂商位图 / Consumer 报告；对其他键盘**只统计时序、不记录按键内容**。输出 `tools/InputRecorder/record.log` | `dotnet run --project tools/InputRecorder -c Release -- 60`（分钟） |
| `KeyTarget` | 端到端测试用的前台窗口，输出收到的按键消息，并标记 MacroHub 注入的按键 | 由 `tests/e2e/e2e.mjs` 启动 |
| `analyze_record.py` | 从记录中统计“厂商位 → 键码 / Consumer”对应关系与厂商报告到钩子的延迟 | `py tools/analyze_record.py [record.log]` |
| `leak_check.py` | 独立的拦截漏键检查（见下） | `py tools/leak_check.py [record.log] [HH:MM:SS]` |

## 实物拦截验证

低级键盘钩子按“后安装先调用”的顺序执行，因此：

1. 先启动 `InputRecorder`（较早安装的钩子，只能看到 MacroHub 放行的按键）；
2. 再启动 MacroHub，并在界面把“拦截测试·全屏蔽”设为激活层；
3. 按宏键盘各控件；
4. `py tools/leak_check.py` 统计：每个物理按下之后 40 ms 内若在记录器侧出现硬件按键（钩子或 Raw Input），即为漏键。


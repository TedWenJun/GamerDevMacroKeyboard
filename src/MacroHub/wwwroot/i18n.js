'use strict';
// UI language for the MacroHub pages: Chinese or English.
//
// Strings are written in Chinese in app.js / index.html and double as lookup keys; EN below holds their English.
// Scripts call t('中文 {0}', value); the static page is translated once at load (text nodes and title / placeholder /
// aria-label attributes whose trimmed text is a key; elements marked data-i18n-html are replaced from EN_HTML).
// The choice follows the browser language until the user picks one with the 中 / EN button; switching reloads.
const I18N = (() => {
  const STORAGE_KEY = 'macrohub.lang';
  let saved = null;
  try { saved = localStorage.getItem(STORAGE_KEY); } catch { /* private mode: follow the browser */ }
  const lang = saved === 'zh' || saved === 'en' ? saved
    : (navigator.language || '').toLowerCase().startsWith('zh') ? 'zh' : 'en';

  const EN = {
    // ── common ──
    '确定': 'OK', '取消': 'Cancel', '删除': 'Delete', '创建': 'Create', '添加': 'Add', '执行': 'Run', '学习': 'Learn',
    '名称': 'Name', '分类': 'Category', '颜色': 'Colour', '类型': 'Type', '参数': 'Arguments', '操作': 'Operation',
    '层': 'Layer', '激活': 'active', '无': 'none', '暂无': 'None yet', '未知': 'unknown', '其他': 'Other', '自定义': 'Custom',
    '、': ', ', '，': ', ', '；': '; ', ' 副本': ' copy', '{0}（{1}）': '{0} ({1})',

    // ── top bar ──
    'MacroHub · 宏键盘配置': 'MacroHub · Macro pad setup', '宏键盘系统层': 'macro pad system layer',
    '切换界面语言': 'Switch the interface language', '在新标签页打开帮助文档': 'Open the help in a new tab', '帮助': 'Help',
    '● 有未保存修改': '● Unsaved changes', '撤销修改': 'Revert', '保存并应用': 'Save & apply',
    '已连接 Hub': 'Hub connected', 'Hub 未连接': 'Hub offline', '宏键盘在线': 'Pad online', '宏键盘离线': 'Pad offline',
    '蓝牙': 'Bluetooth', '未知连接': 'unknown link',
    '充电中 {0}%': 'Charging {0}%', '电量 {0}%': 'Battery {0}%', '宏键盘电池（每 30 秒读取一次）': 'Pad battery (read every 30 s)',
    '键盘钩子': 'Keyboard hook', '钩子未安装': 'Hook not installed', '拦截: {0}': 'Interception: {0}',
    '关联': 'Correlate', '按键码': 'Key codes', '关闭': 'Off', '前台: {0}': 'Foreground: {0}', '当前层: {0}': 'Layer: {0}',
    '已连接的应用（在前台且其档案开启转发时，由应用处理）：': 'Connected applications (they handle the pad while in front, if their profile forwards):',
    '暂无应用接入（如阶段二的 UE 插件，通过命名管道或 WebSocket）。未接入时 MacroHub 用模拟快捷键执行功能。点击查看说明':
      'No application connected (e.g. the Unreal plugin, over the named pipe or WebSocket). Without one, MacroHub runs functions as simulated shortcuts. Click for details',
    '应用客户端 {0}': 'App clients {0}', '非管理员': 'Not elevated',
    '向管理员权限窗口发送按键需要以管理员身份运行 MacroHub': 'Sending keys to elevated windows requires running MacroHub as administrator',
    '命名管道': 'named pipe', ' · 上下文：{0}': ' · context: {0}', ' · 已发送 {0} 条': ' · {0} sent',

    // ── layers ──
    '把当前查看的层设为手动激活层': 'Make the layer you are viewing the active layer', '设为激活层': 'Set active',
    '层属性': 'Layer properties', '+ 新建层': '+ New layer', '删除当前查看的层': 'Delete the layer you are viewing', '删除层': 'Delete layer',
    '层的说明': 'About layers', '宏键盘布局': 'Macro pad layout',
    '点击按键进行编辑 · 实体按键按下时高亮 · 黄色描边为当前激活层': 'Click a key to edit it · keys light up when pressed · the yellow outline marks the active layer',
    '例如：动画调试': 'e.g. Animation debug', '层名称': 'Layer name',
    '按全局设置': 'Use the global setting', '继承基础层': 'Inherit from the base layer',
    '使用“设备与拦截”里的未绑定设置（当前：{0}）': 'Use the unbound setting from “Device & interception” (currently: {0})',
    '沿用「{0}」的绑定': 'Use the bindings of “{0}”', '宏键盘原本的按键照常输入': 'The pad types its stock keys as usual',
    '什么都不发生，适合游戏/演示时防误触': 'Nothing happens; good against accidental presses in games or demos',
    '（当前自动切到「{0}」，勾选将改为本层）': ' (currently switches to “{0}”; ticking moves it to this layer)',
    '← 左移': '← Move left', '右移 →': 'Move right →', '设为基础层': 'Make base layer',
    '移到第一位；其他层未绑定且设为“继承”的按键会沿用它': 'Moves it first; unbound keys set to “inherit” in other layers use it',
    '复制此层': 'Duplicate layer', '层属性 · {0}': 'Layer properties · {0}', '本层未绑定的按键': 'Unbound keys in this layer',
    '这些应用在前台时自动切到本层': 'Switch to this layer while these applications are in front',
    '还没有应用档案，可在“应用档案”页添加。': 'No application profiles yet; add them on the “Applications” tab.',
    '顺序（第 {0} / {1} 层，基础层；旋钮等“下一层”按此顺序切换）': 'Order (layer {0} of {1}, base layer; “next layer” cycles in this order)',
    '顺序（第 {0} / {1} 层；旋钮等“下一层”按此顺序切换）': 'Order (layer {0} of {1}; “next layer” cycles in this order)',
    '内部 id：{0}': 'Internal id: {0}',
    '「{0}」已设为基础层，点“保存并应用”生效': '“{0}” is now the base layer; click “Save & apply” to use it',
    '已复制为「{0}」，点“保存并应用”生效': 'Duplicated as “{0}”; click “Save & apply” to use it',
    '至少需要保留一个层': 'At least one layer is required',
    '删除后点“保存并应用”才生效，保存前可“撤销修改”。': 'Takes effect after “Save & apply”; “Revert” undoes it until then.',
    '· 它是基础层，其他层未绑定的按键会改为继承「{0}」': '· It is the base layer; unbound keys in other layers will inherit “{0}” instead',
    '· 它是当前激活层，保存后会切到第一个层': '· It is the active layer; saving switches to the first layer',
    '· 应用 {0} 将不再自动切层': '· {0} will no longer switch layers automatically',
    '· {0} 个“切换到该层”的动作将失效': '· {0} “switch to this layer” action(s) will stop working',
    '删除层「{0}」？': 'Delete layer “{0}”?', '已删除层「{0}」，点“保存并应用”生效': 'Deleted layer “{0}”; click “Save & apply” to use it',
    '未绑定·{0}': 'Unbound · {0}', '自定义层': 'Custom layer', '空白（未绑定的按键继承基础层）': 'Empty (unbound keys inherit the base layer)',
    '复制「{0}」的绑定': 'Copy the bindings of “{0}”', '新建层': 'New layer', '初始绑定': 'Initial bindings',
    '创建后点按键逐个选择功能。可在“应用档案”里让某个程序在前台时自动切到该层，或把“下一层”功能绑到旋钮按下。':
      'After creating it, click keys to pick their functions. An application profile can switch to it while a program is in front, or bind “next layer” to the knob press.',
    '已创建层「{0}」，点“保存并应用”生效': 'Created layer “{0}”; click “Save & apply” to use it',

    // ── lighting ──
    '灯光': 'Lighting', '接管': 'Take over', '打开后 MacroHub 才会写入键盘背光': 'MacroHub only writes the backlight while this is on',
    '模式': 'Mode', '亮度': 'Brightness', '色彩': 'Colour', '速度': 'Speed', '方向': 'Direction', '顺时针': 'Clockwise', '逆时针': 'Counter-clockwise',
    '本层独立': 'Per layer', '勾选后这个层有自己的灯光，切到该层时自动应用': 'Gives this layer its own lighting, applied whenever it becomes active',
    '单色常亮': 'Solid', '行云流水': 'Flowing', '跑马': 'Marquee', '单色呼吸': 'Breathing', '循环呼吸': 'Cycling breath',
    '俄罗斯方块': 'Tetris', '霓虹': 'Neon', '流光溢彩': 'Rainbow flow', '关灯': 'Off',
    '未接管：键盘保持它自己的灯效': 'Not taken over: the pad keeps its own lighting',
    '「{0}」层的灯光，切到该层时自动应用': 'Lighting of layer “{0}”, applied when it becomes active',
    '所有层通用；勾选「本层独立」可为当前层单独设置': 'Shared by all layers; tick “Per layer” to give this layer its own',

    // ── actions ──
    '快捷键': 'Shortcut', '输入文本': 'Type text', '宏序列': 'Macro', '运行程序': 'Run program', '鼠标': 'Mouse',
    '切换层': 'Switch layer', '仅转发给应用': 'Forward to app only', '原样输出': 'Pass through', '屏蔽': 'Block',
    '按住 ': 'Hold ', '文本 "{0}"': 'Text "{0}"', '宏 ({0} 步)': 'Macro ({0} steps)', '运行 {0}': 'Run {0}',
    '鼠标 {0}': 'Mouse {0}', '滚轮 {0}': 'Wheel {0}', '层 {0}': 'Layer {0}',
    '例如 Ctrl+Shift+S 或 Ctrl+K, Ctrl+C': 'e.g. Ctrl+Shift+S or Ctrl+K, Ctrl+C', '录制': 'Record', '按下组合键…': 'Press the keys…',
    '按键': 'Keys', '按住同步（按下即按下、松开即松开，游戏用）': 'Hold in sync (press on press, release on release; for games)',
    '文本': 'Text', '程序 / 文件 / URL': 'Program / file / URL', '滚轮': 'Wheel', '垂直格数': 'Vertical notches',
    '按住': 'Hold', '水平格数': 'Horizontal notches', '下一层': 'Next layer', '上一层': 'Previous layer', '指定层': 'Specific layer',
    '延时ms': 'Delay ms', '+ 步骤': '+ Step',
    '只把功能事件发给已连接的应用客户端，本地不执行任何按键。': 'Only sends the function event to connected app clients; nothing is typed locally.',
    '动作类型': 'Action type',

    // ── input shaping ──
    '每 {0} 格触发': 'every {0} detents', '间隔 ≥ {0}ms': 'interval ≥ {0} ms', '按住连发 {0}/{1}ms': 'auto-repeat {0}/{1} ms',
    '共用设置（全局一份）': 'Shared settings (one for all layers)', '「{0}」层单独设置': 'Own settings for layer “{0}”',
    '默认（每格触发）': 'Default (every detent)', '默认（不连发）': 'Default (no repeat)', '共用设置：': 'Shared settings: ',
    '（{0} 个层使用：{1}）': ' ({0} layer(s): {1})', '单独设置的层：': 'Layers with their own settings: ',
    '修改共用设置会影响上面所有使用它的层；单独设置的层不受影响。': 'Changing the shared settings affects every layer above that uses them; layers with their own settings are not affected.',
    '只影响「{0}」层，优先于共用设置。': 'Only affects layer “{0}” and takes precedence over the shared settings.',
    '每转几格触发一次': 'Detents per trigger', '格': 'detents',
    '转满格数才执行一次动作；反向旋转或停顿会重新计数': 'The action runs once per this many detents; turning back or pausing restarts the count',
    '最小触发间隔': 'Minimum interval', '0 为不限制；转得再快也不会比这更频繁': '0 = no limit; never triggers more often than this, however fast you turn',
    '停顿多久清零未满的格数': 'Pause that clears a partial count', '左旋 / 右旋使用相同设置': 'Left and right turns share these settings',
    '按住连发': 'Auto-repeat while held', ' — 按住不放时重复执行动作（“按住同步”类快捷键不适用）': ' — repeats the action while held (not for “hold in sync” shortcuts)',
    '首次重复前延迟': 'Delay before the first repeat', '重复间隔': 'Repeat interval',
    '0 为不限制；防止连续快速按下重复触发': '0 = no limit; stops rapid presses from triggering repeatedly',
    '共用设置': 'Shared settings', '默认值': 'Defaults', '旋转灵敏度': 'Rotation sensitivity', '按键触发': 'Key trigger',
    '本层生效：{0}': 'In effect here: {0}', '取消本层单独设置（改用共用设置）': 'Remove this layer’s own settings (use the shared ones)',
    '共用设置恢复默认': 'Reset the shared settings',
    '只影响本地快捷键和功能转发；转发原始控件给应用（如 UE 插件）时每格/每次都会发送，由应用自己决定灵敏度。':
      'Only affects local shortcuts and forwarded functions. Raw controls forwarded to an application (such as the Unreal plugin) are sent for every detent / press, and the application decides the sensitivity.',

    // ── inspector ──
    '选择左侧的一个按键': 'Pick a key on the left', '无签名（尚未学习）': 'No signature (not learned yet)',
    '请在宏键盘上按一下 ': 'Press ', '（旋钮/摇杆请做对应动作）…': ' on the macro pad (turn or push the knob / joystick)…',
    '（未绑定：{0}）': '(unbound: {0})', '（继承基础层）': '(inherit base layer)', '+ 新建功能…': '+ New function…',
    '控件 {0}': 'Control {0}', '硬件签名': 'Hardware signatures', '在「': 'Function in layer “', '」层绑定的功能': '”',
    '当前继承自基础层「{0}」': 'Currently inherited from the base layer “{0}”',
    '本层未绑定时：{0}（可在“层属性”中修改）': 'When unbound here: {0} (change it in “Layer properties”)',
    '模拟按下（使用已保存配置）': 'Simulate a press (uses the saved configuration)', '功能：{0}': 'Function: {0}',
    'id {0} · 被 {1} 处使用': 'id {0} · used in {1} place(s)', '默认动作（系统层）': 'Default action (system layer)',
    '应用覆盖': 'Application overrides', '前台为该应用时，使用下面的动作代替默认动作。': 'While that application is in front, the action below replaces the default.',
    '厂商位图 bit {0}（第 {1} 字节 bit {2}）': 'Vendor bitmap bit {0} (byte {1}, bit {2})', '键盘虚拟键 0x{0}': 'Virtual key 0x{0}',
    '自定义功能 {0}': 'Custom function {0}', '未绑定': 'unbound', '（注意：未保存的修改不生效）': ' (note: unsaved changes are not used)',
    '已新建「{0}」，在按键检查器里绑定并编辑它': 'Created “{0}”; bind and edit it in the key inspector',

    // ── bottom tabs ──
    '应用档案': 'Applications', '功能库': 'Functions', '设备与拦截': 'Device & interception', '配置 JSON': 'Config JSON',
    '实时事件': 'Live events', '测试台': 'Test bench',
    '+ 新建应用': '+ New application',
    '前台进程匹配后：可自动切换到指定层，并覆盖功能的动作。开启“转发”后，已连接的应用客户端（如 UE 插件）收到功能事件并原生处理；未连接时回退为本地动作。':
      'When the foreground process matches, MacroHub can switch to a layer and override function actions. With forwarding on, a connected app client (such as the Unreal plugin) receives the events and handles them natively; when it is not connected, the local actions run instead.',
    '删除应用档案「{0}」？': 'Delete application profile “{0}”?', '删除后点“保存并应用”才生效。': 'Takes effect after “Save & apply”.',
    '进程（逗号分隔，支持 *）': 'Processes (comma-separated, * allowed)', '前台时自动切换到层': 'Switch to layer while in front',
    '（不切换）': '(don’t switch)', '转发给应用客户端': 'Forward to the app client', '覆盖 {0} 个功能：': 'Overrides {0} function(s): ',
    '新应用': 'New application', '不转发': 'Don’t forward', '功能按本地动作执行（模拟快捷键等）。': 'Functions run as local actions (simulated shortcuts etc.).',
    '转发功能事件': 'Forward function events',
    '应用在线时收到 Hub 层映射后的功能（如 ue.play）并自行处理，本地不再执行；离线时回退本地动作。':
      'While the application is connected it receives the function the Hub’s layer maps to (e.g. ue.play) and handles it itself; nothing runs locally. When it is offline, the local action runs.',
    '转发原始控件（应用自行决定功能）': 'Forward raw controls (the application decides)',
    '应用在线时跳过 Hub 的层，收到原始控件事件（如 KNOB_CW），由应用按自身上下文决定做什么（UE 插件：不同编辑器不同功能）；离线时回退 Hub 的层与本地动作。':
      'While the application is connected the Hub’s layers are skipped: it receives raw control events (e.g. KNOB_CW) and decides what to do from its own context (the Unreal plugin: per editor). When it is offline, the Hub’s layers and local actions apply.',
    '功能库（系统层接口）': 'Functions (system layer interface)', '+ 新建功能': '+ New function',
    '功能是与硬件、与应用都无关的语义命令。层把按键映射到功能，应用档案可以覆盖功能的具体动作。':
      'A function is a semantic command, independent of hardware and of applications. Layers map keys to functions; application profiles can override what a function does.',
    '搜索名称、ID、快捷键、分类、使用位置…（多个词用空格分隔，按 / 聚焦）': 'Search name, ID, shortcut, category, usage… (separate words with spaces; press / to focus)',
    '使用情况': 'Usage', '全部': 'All', '已绑定到按键': 'Bound to a key', '未使用': 'Unused', '有应用覆盖': 'Has app overrides',
    '全部分类（{0}）': 'All categories ({0})', '默认动作': 'Default action', '使用位置': 'Used in',
    '跳到「{0}」层的 {1}': 'Go to {1} in layer “{0}”', '覆盖：': 'Overrides: ', '已执行 {0}': 'Ran {0}', '仍被层使用': 'Still used by a layer',
    '没有匹配的功能 ': 'No matching functions ', '清除筛选': 'Clear filters', '共 {0} 个': '{0} in total', '显示 {0} / 共 {1} 个': 'Showing {0} of {1}',

    // ── device & interception ──
    '拦截模式': 'Interception mode',
    '（默认）— 用直接读取的厂商/Consumer 报告识别宏键盘按键，主键盘不受影响': ' (default) — identifies pad keys from the vendor / consumer reports read directly; your main keyboard is not affected',
    '— 按键签名里的键码一律视为宏键盘（固件改为 F13–F24 等独有键码时使用）': ' — any key code in a signature counts as the pad (for firmware that sends unique codes such as F13–F24)',
    '— 从不拦截，只触发动作': ' — never intercepts, only runs actions',
    '未绑定按键': 'Unbound keys', '旋钮音量保护': 'Knob volume guard',
    '旋钮用于其他功能时，自动恢复被旋钮改动的系统音量': 'When the knob does something else, restore the system volume it changed',
    'W909 旋钮在硬件层固定发送“音量±”，Windows 会直接调节音量且无法拦截。开启后，旋钮被绑定到非音量功能、被屏蔽或转发给应用时，MacroHub 会立即把音量恢复原值（屏幕上的音量提示可能闪一下）。':
      'The W909 knob always sends Volume ± in hardware; Windows changes the volume directly and it cannot be intercepted. With this on, whenever the knob is bound to something else, blocked or forwarded to an application, MacroHub puts the volume straight back (the on-screen volume flyout may flash).',
    '设备': 'Device', '匹配（HID 路径包含的子串，逗号分隔）': 'Match (substrings of the HID path, comma-separated)',
    '本机 HID 设备': 'HID devices on this PC', '刷新列表': 'Refresh',
    '已打开 page 0x{0} usage 0x{1} len {2}': 'Open: page 0x{0} usage 0x{1} len {2}', '未打开任何可读集合': 'No readable collection open',
    '音频端点可用 · 当前音量 {0}% · 已恢复 {1} 次': 'Audio endpoint available · volume {0}% · restored {1} time(s)', ' · 保护中': ' · guarding',
    '未找到默认音频输出设备，音量保护不可用': 'No default audio output device; the volume guard is unavailable',

    // ── config JSON ──
    '从编辑状态刷新': 'Refresh from editor', '应用到编辑器': 'Apply to editor', '导出文件': 'Export file', '导入文件': 'Import file',
    '恢复默认配置': 'Restore defaults', '已应用到编辑器（尚未保存）': 'Applied to the editor (not saved yet)',
    '已载入文件，点“应用到编辑器”校验并应用': 'File loaded; click “Apply to editor” to validate and apply it',
    '恢复默认配置？': 'Restore the default configuration?',
    '会覆盖当前已保存的配置（包括学习到的按键签名），且无法撤销。': 'This overwrites the saved configuration (including learned key signatures) and cannot be undone.',
    '恢复默认': 'Restore', '已恢复默认配置': 'Defaults restored', '已保存并应用': 'Saved and applied', '保存失败：': 'Save failed:',
    '初始化失败：': 'Start-up failed: ',

    // ── live events ──
    '显示原始信号': 'Show raw signals', '清空': 'Clear', '原始控件 → 应用×{0}': 'raw control → app ×{0}', ' 拦截': ' blocked',
    '层={0} 功能={1} 动作={2}': 'layer={0} function={1} action={2}', ' 格数={0}': ' detents={0}', ' 未触发': ' not triggered',
    ' 转发×{0}': ' forwarded ×{0}', ' 已执行': ' executed', '{0} 连发 {1}': '{0} repeat {1}', '层={0}': 'layer={0}',
    '手动层={0} 生效层={1}': 'manual layer={0} effective layer={1}', '已连接': 'connected', '已断开': 'disconnected',
    '{0} 的原始按键漏出（物理报告晚于钩子），可调大等待时间': 'The stock key of {0} leaked (its physical report came after the hook); raise the wait time',
    '{0} 学习完成：{1}': '{0} learned: {1}', '学习失败：': 'Learning failed: ',

    // ── test bench ──
    '实体按键拦截测试': 'Physical key interception test',
    '1. 切到会拦截按键的层（如“通用办公”）并点“设为激活层”。': '1. Switch to a layer that intercepts keys (e.g. “Office”) and click “Set active”.',
    '2. 点击下面输入框，按宏键盘按键。': '2. Click the box below and press keys on the macro pad.',
    '3. 拦截成功时输入框': '3. When interception works, the box does ', '不会': 'not', '出现原厂数字，只会触发功能。': ' show the stock digits; only functions run.',
    '焦点放这里再按宏键盘…': 'Focus here, then press the macro pad…',
    '触发': 'Triggered', '已拦截': 'Intercepted', '输入框收到按键': 'Keys typed into the box', '重置': 'Reset',
    '从厂商/Consumer 报告读到的物理按下次数': 'Physical presses read from the vendor / consumer reports', '物理按下': 'Physical presses',
    '钩子拦截掉的原始按键': 'Stock keys the hook intercepted', 'Hub 拦截': 'Hub intercepted',
    '物理报告晚于钩子等待时间，原始按键漏出': 'The physical report came later than the hook waits, so the stock key leaked', '泄漏': 'Leaked',
    '需要等待物理报告的次数 / 最长等待': 'Times the hook had to wait for the physical report / longest wait', '等待': 'Waited',
    '钩子等待物理报告的时间：': 'How long the hook waits for the physical report: ',
    '动作测试': 'Action test', '直接执行一个动作（发往当前焦点窗口，即本页面）。': 'Run an action directly (it goes to the focused window, i.e. this page).',
    '应用客户端（协议 v2）': 'App clients (protocol v2)',

    // ── help page chrome ──
    'MacroHub · 帮助': 'MacroHub · Help', '帮助文档': 'Help', '查看 Markdown 源文件': 'View Markdown source',
    '← 返回配置界面': '← Back to setup', '加载中…': 'Loading…', '加载帮助失败：': 'Could not load the help: ',
  };

  // Whole elements whose content mixes text and markup; replaced as HTML in English.
  const EN_HTML = {
    clientProtocol: 'Recommended: the named pipe <code id="pipeName">\\\\.\\pipe\\MacroHub</code> (one JSON message per line); '
      + '<code>ws://127.0.0.1:17900/ws</code> also works. Send <code>{"type":"hello","role":"app","protocol":2,"app":"…","pid":…,"mode":"editor"}</code> '
      + 'to register; while in front you receive <code>control</code> or <code>function</code> events according to your application profile. '
      + '<a href="help.html#app-protocol" target="_blank">Protocol details</a>',
  };

  function format(s, args) {
    return args.length ? s.replace(/\{(\d+)\}/g, (m, i) => (args[i] ?? m)) : s;
  }

  /** Translate a Chinese key (with {0}… placeholders) into the current language. */
  function t(key, ...args) {
    const text = lang === 'en' ? (EN[key] ?? key) : key;
    return format(text, args);
  }

  /** Translate the static page once. Text keeps its surrounding whitespace; unknown text stays as it is. */
  function translateDom(root = document) {
    if (lang !== 'en') return;
    const walker = document.createTreeWalker(root.body || root, NodeFilter.SHOW_TEXT);
    const nodes = [];
    for (let n = walker.nextNode(); n; n = walker.nextNode()) nodes.push(n);
    for (const n of nodes) {
      if (n.parentElement?.closest('script, style, [data-i18n-html]')) continue;
      const raw = n.nodeValue, key = raw.trim();
      if (key && EN[key] !== undefined) {
        const lead = raw.match(/^\s*/)[0], trail = raw.match(/\s*$/)[0];
        n.nodeValue = lead + EN[key] + trail;
      }
    }
    for (const el of (root.body || root).querySelectorAll('[title], [placeholder], [aria-label]')) {
      for (const attr of ['title', 'placeholder', 'aria-label']) {
        const v = el.getAttribute(attr);
        if (v && EN[v] !== undefined) el.setAttribute(attr, EN[v]);
      }
    }
    for (const el of (root.body || root).querySelectorAll('[data-i18n-html]')) {
      const html = EN_HTML[el.dataset.i18nHtml];
      if (html !== undefined) el.innerHTML = html;
    }
    if (EN[document.title] !== undefined) document.title = EN[document.title];
  }

  // Names that come from the configuration (layers, functions, categories, control labels, apps). The config keeps
  // whatever language it was created in; a name still equal to a default in either language is shown in the current
  // one, anything the user typed is shown as typed. Pairs come from /api/names ([Chinese, English] per default name).
  const names = new Map();
  function setDefaultNames(pairs) {
    names.clear();
    for (const [zh, en] of pairs || []) {
      names.set(zh, lang === 'en' ? en : zh);
      names.set(en, lang === 'en' ? en : zh);
    }
  }
  /** Show a configuration name in the current language when it is an untouched default. */
  function tn(name) {
    return typeof name === 'string' ? names.get(name) ?? name : name;
  }

  function setLang(next) {
    try { localStorage.setItem(STORAGE_KEY, next); } catch { /* not persisted; still switch for this load */ }
    location.reload();
  }

  document.documentElement.lang = lang === 'zh' ? 'zh-CN' : 'en';
  return { lang, t, tn, setDefaultNames, translateDom, setLang, EN };
})();
const t = I18N.t, tn = I18N.tn;

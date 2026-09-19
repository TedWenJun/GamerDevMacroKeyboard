'use strict';
// MacroHub web UI — vanilla JS, no build step.

const S = {
  saved: null,      // config as saved on the hub
  cfg: null,        // working copy being edited
  hub: {},          // /api/state
  viewLayer: null,
  selected: null,
  keyNames: [],
  ws: null,
  wsOk: false,
  stats: { controls: 0, suppressed: 0, typed: 0 },
  testAction: { type: 'keys', keys: 'Ctrl+Shift+Esc' },
};

// ───────────── helpers ─────────────
const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];
function h(tag, attrs = {}, ...children) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v == null || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
    else if (k === 'value') el.value = v;
    else if (k === 'checked') el.checked = !!v;
    else el.setAttribute(k, v === true ? '' : v);
  }
  for (const c of children.flat()) if (c != null && c !== false) el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  return el;
}
const SVGNS = 'http://www.w3.org/2000/svg';
function s(tag, attrs = {}, ...children) {
  const el = document.createElementNS(SVGNS, tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v == null) continue;
    if (k.startsWith('on')) el.addEventListener(k.slice(2), v); else el.setAttribute(k, v);
  }
  for (const c of children.flat()) if (c != null) el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  return el;
}
const clone = o => JSON.parse(JSON.stringify(o));
function toast(msg, err = false) {
  const el = $('#toast');
  el.textContent = msg; el.className = 'toast' + (err ? ' err' : ''); el.hidden = false;
  clearTimeout(toast.timer); toast.timer = setTimeout(() => el.hidden = true, err ? 6000 : 2500);
}
// In-page modal: the native dialog functions are unavailable in some embedded browsers.
function modal({ title, body = [], okText = t('确定'), cancelText = t('取消'), danger = false, onOk }) {
  return new Promise(resolve => {
    const close = result => { overlay.remove(); document.removeEventListener('keydown', onKey); resolve(result); };
    const ok = h('button', { class: danger ? 'danger' : 'primary', type: 'submit' }, okText);
    const form = h('form', {
      class: 'modal', onsubmit: ev => {
        ev.preventDefault();
        if (onOk && onOk() === false) return; // validation failed, keep open
        close(true);
      },
    },
      h('h3', {}, title),
      h('div', { class: 'modal-body' }, body),
      h('div', { class: 'row modal-actions' }, h('button', { class: 'ghost', type: 'button', onclick: () => close(false) }, cancelText), ok));
    const overlay = h('div', { class: 'modal-overlay', onmousedown: ev => { if (ev.target === overlay) close(false); } }, form);
    const onKey = ev => { if (ev.key === 'Escape') close(false); };
    document.addEventListener('keydown', onKey);
    document.body.append(overlay);
    const first = form.querySelector('input:not([type=color]), select');
    (first || ok).focus();
    if (first && first.select) first.select();
  });
}
const confirmModal = (title, lines, okText = t('确定'), danger = true) =>
  modal({ title, body: [].concat(lines).map(l => h('p', { class: 'modal-line' }, l)), okText, danger });

function layerForm(init) {
  const name = h('input', { value: init.name, placeholder: t('例如：动画调试'), maxlength: 20 });
  const color = h('input', { type: 'color', value: init.color, class: 'color-input' });
  const swatches = h('div', { class: 'swatches' }, ...['#f5c400', '#4f8cff', '#34c77b', '#ff5d5d', '#b07cff', '#ff9f43', '#2ec4d6', '#9aa0a6'].map(c =>
    h('button', { type: 'button', class: 'swatch-btn', style: `background:${c}`, title: c, onclick: () => { color.value = c; } })));
  return { name, color, fields: [h('label', { class: 'field' }, t('层名称'), name), h('div', { class: 'field' }, t('颜色'), h('div', { class: 'row', style: 'margin:3px 0 0' }, color, swatches))] };
}

async function api(method, path, body) {
  const res = await fetch('/api' + path, { method, headers: body ? { 'Content-Type': 'application/json' } : {}, body: body ? JSON.stringify(body) : undefined });
  const text = await res.text();
  let data = null; try { data = text ? JSON.parse(text) : null; } catch { data = text; }
  if (!res.ok) throw Object.assign(new Error((data && data.errors && data.errors.join('\n')) || res.statusText), { data });
  return data;
}

const FORWARD_MODES = {
  off: { label: t('不转发'), hint: t('功能按本地动作执行（模拟快捷键等）。') },
  functions: { label: t('转发功能事件'), hint: t('应用在线时收到 Hub 层映射后的功能（如 ue.play）并自行处理，本地不再执行；离线时回退本地动作。') },
  controls: { label: t('转发原始控件（应用自行决定功能）'), hint: t('应用在线时跳过 Hub 的层，收到原始控件事件（如 KNOB_CW），由应用按自身上下文决定做什么（UE 插件：不同编辑器不同功能）；离线时回退 Hub 的层与本地动作。') },
};
function describeClient(c) {
  const transport = { pipe: t('命名管道'), websocket: 'WebSocket' }[c.transport] || c.transport;
  return `${c.app} · ${c.process || '?'} (pid ${c.pid}) · ${transport} · v${c.protocol}${c.mode ? ' · ' + c.mode : ''}`
    + (c.context ? t(' · 上下文：{0}', c.context + (c.contextDetail ? ' / ' + c.contextDetail : '')) : '') + t(' · 已发送 {0} 条', c.lastSeq);
}

const ACTION_TYPES = {
  keys: t('快捷键'), text: t('输入文本'), macro: t('宏序列'), run: t('运行程序'), mouse: t('鼠标'),
  layer: t('切换层'), forward: t('仅转发给应用'), passthrough: t('原样输出'), none: t('屏蔽'),
};
function describeAction(a) {
  if (!a) return '—';
  switch (a.type) {
    case 'keys': return (a.hold ? t('按住 ') : '') + a.keys;
    case 'text': return t('文本 "{0}"', a.text);
    case 'macro': return t('宏 ({0} 步)', (a.steps || []).length);
    case 'run': return t('运行 {0}', a.path);
    case 'mouse': return a.button ? t('鼠标 {0}', a.button) : t('滚轮 {0}', a.wheel || 0);
    case 'layer': return t('层 {0}', a.op + (a.layer ? ' ' + a.layer : ''));
    default: return ACTION_TYPES[a.type] || a.type;
  }
}

// ───────────── model helpers ─────────────
const fnById = id => S.cfg.functions.find(f => f.id === id);
const layerById = id => S.cfg.layers.find(l => l.id === id);
const controlById = id => S.cfg.device.controls.find(c => c.id === id);
const FALLBACK_LABEL = { passthrough: t('原样输出'), block: t('屏蔽') };
// How the pad is attached (PadTransport on the Hub): cable, 2.4G receiver or Bluetooth.
const TRANSPORT_LABEL = { usb: 'USB', '2.4g': '2.4G', bluetooth: t('蓝牙'), unknown: t('未知连接') };
// Mirrors Router.Route: own binding → layer fallback (passthrough/block) → inherit base layer → global "unbound".
function resolveFn(layer, controlId) {
  if (layer.map[controlId]) return { fn: layer.map[controlId], inherited: false };
  const fb = layer.fallback || 'inherit';
  if (fb !== 'inherit') return { fn: null, inherited: false, fallback: fb };
  const base = S.cfg.layers[0];
  if (base && base !== layer && base.map[controlId]) return { fn: base.map[controlId], inherited: true };
  return { fn: null, inherited: false, fallback: S.cfg.unbound };
}
function fnUsage(fnId) {
  let n = 0;
  for (const l of S.cfg.layers) for (const v of Object.values(l.map)) if (v === fnId) n++;
  return n;
}
function markDirty() {
  const dirty = JSON.stringify(S.cfg) !== JSON.stringify(S.saved);
  $('#dirty').hidden = !dirty;
  $('#btnSave').disabled = !dirty;
  $('#btnRevert').disabled = !dirty;
}
function changed(what = 'all') {
  markDirty();
  if (what === 'all' || what === 'device') { renderDevice(); renderLighting(); }
  if (what === 'all' || what === 'layers') renderLayerTabs();
  if (what === 'all') { renderInspector(); renderApps(); renderFunctions(); renderSettings(); }
}

// ───────────── lighting ─────────────
// Five controls only: mode, brightness, colour, speed, direction. The pad stores a palette per effect, but the
// Hub writes one colour into every slot, so a single swatch covers every mode.
const LIGHT_MODES = [t('单色常亮'), t('行云流水'), t('跑马'), t('单色呼吸'), t('循环呼吸'), t('俄罗斯方块'), t('霓虹'), t('流光溢彩'), t('关灯')];
const LIGHT_DEFAULT = { mode: 1, brightness: 5, speed: 3, direction: 0, color: '#ffffff' };

function lightingConfig() {
  if (!S.cfg.lighting) S.cfg.lighting = { enabled: false, followLayer: true, default: { ...LIGHT_DEFAULT } };
  if (!S.cfg.lighting.default) S.cfg.lighting.default = { ...LIGHT_DEFAULT };
  return S.cfg.lighting;
}
/** The spec the panel edits: the viewed layer's own lighting when it has one, otherwise the hub default. */
function editedLighting() {
  const layer = layerById(S.viewLayer);
  return layer?.lighting ?? lightingConfig().default;
}
function renderLighting() {
  if (!S.cfg) return;
  const cfg = lightingConfig();
  const spec = editedLighting();
  const layer = layerById(S.viewLayer);

  const modeSel = $('#lightMode');
  if (modeSel.options.length !== LIGHT_MODES.length) {
    modeSel.replaceChildren(...LIGHT_MODES.map((name, i) => h('option', { value: String(i + 1) }, `${i + 1} · ${name}`)));
  }
  modeSel.value = String(spec.mode ?? 1);
  $('#lightEnabled').checked = !!cfg.enabled;
  $('#lightBright').value = spec.brightness ?? 5;
  $('#lightBrightVal').textContent = spec.brightness ?? 5;
  $('#lightSpeed').value = spec.speed ?? 3;
  $('#lightSpeedVal').textContent = spec.speed ?? 3;
  $('#lightColor').value = spec.color || '#ffffff';
  $('#lightColorHex').value = spec.color || '#ffffff';
  $$('#lightDir button').forEach(b => b.classList.toggle('active', +b.dataset.dir === (spec.direction ?? 0)));
  $('#lightPerLayer').checked = !!layer?.lighting;
  $('#lightPanel').dataset.off = cfg.enabled ? '0' : '1';
  $('#lightHint').textContent = !cfg.enabled ? t('未接管：键盘保持它自己的灯效')
    : layer?.lighting ? t('「{0}」层的灯光，切到该层时自动应用', layer.name)
      : t('所有层通用；勾选「本层独立」可为当前层单独设置');
}
/** Edit one field of the spec the panel is bound to and preview it on the pad right away. */
function setLighting(patch) {
  const layer = layerById(S.viewLayer);
  const target = layer?.lighting ?? lightingConfig().default;
  Object.assign(target, patch);
  markDirty();
  renderLighting();
  previewLighting();
}
let lightPreviewTimer = null;
function previewLighting() {
  if (!lightingConfig().enabled) return;
  clearTimeout(lightPreviewTimer);
  // dragging a slider fires continuously; the pad only needs the value the user settles on
  lightPreviewTimer = setTimeout(() => api('POST', '/lighting/preview', editedLighting()).catch(() => { }), 120);
}

// ───────────── top bar ─────────────
function renderChips() {
  const st = S.hub || {};
  const layer = st.layer && S.cfg ? layerById(st.layer) : null;
  const fg = st.foreground || {};
  const chip = (cls, text, title) => h('span', { class: 'chip ' + cls, title }, h('span', { class: 'dot' }), text);
  $('#chips').replaceChildren(...[
    chip(S.wsOk ? 'ok' : 'err', S.wsOk ? t('已连接 Hub') : t('Hub 未连接')),
    chip(st.connected ? 'ok' : 'err',
      st.connected ? t('宏键盘在线') + (TRANSPORT_LABEL[st.transport] ? ' · ' + TRANSPORT_LABEL[st.transport] : '') : t('宏键盘离线'),
      (st.collections || []).map(c => c.path).join('\n')),
    // Battery from the pad's status poll; on the cable it reads as charging.
    ...(st.connected && st.battery ? [chip(
      st.battery.charging || st.battery.percent > 20 ? 'ok' : st.battery.percent > 10 ? 'warn' : 'err',
      st.battery.charging ? t('充电中 {0}%', st.battery.percent) : t('电量 {0}%', st.battery.percent),
      t('宏键盘电池（每 30 秒读取一次）'))] : []),
    chip(st.hook ? 'ok' : 'err', st.hook ? t('键盘钩子') : t('钩子未安装')),
    chip('', t('拦截: {0}', { correlate: t('关联'), codes: t('按键码'), off: t('关闭') }[st.suppression] || st.suppression || '?')),
    chip('', t('前台: {0}', (fg.process || '?') + (fg.app ? ` → ${fg.app}` : '')), fg.title),
    chip('', t('当前层: {0}', layer ? layer.name : st.layer || '?')),
    (() => {
      const clients = st.clients || [];
      const tip = clients.length
        ? t('已连接的应用（在前台且其档案开启转发时，由应用处理）：') + '\n' + clients.map(describeClient).join('\n')
        : t('暂无应用接入（如阶段二的 UE 插件，通过命名管道或 WebSocket）。未接入时 MacroHub 用模拟快捷键执行功能。点击查看说明');
      const el = chip(clients.length ? 'ok' : '', t('应用客户端 {0}', clients.length), tip);
      el.style.cursor = 'pointer';
      el.addEventListener('click', () => { $('#bottomTabs [data-tab=test]').click(); $('#clientList').scrollIntoView({ block: 'center' }); });
      return el;
    })(),
    st.elevated === false ? chip('warn', t('非管理员'), t('向管理员权限窗口发送按键需要以管理员身份运行 MacroHub')) : null,
  ].filter(Boolean));
}

// ───────────── layers ─────────────
async function openLayerProps() {
  const l = layerById(S.viewLayer);
  if (!l) return;
  const idx = S.cfg.layers.indexOf(l);
  const isBase = idx === 0;
  const f = layerForm(l);
  const cur = l.fallback || 'inherit';
  const radio = (value, label, hint) => h('label', { class: 'radio' },
    h('input', { type: 'radio', name: 'layerFallback', value, checked: cur === value }),
    h('span', {}, label, hint ? h('span', { class: 'muted small' }, ' — ' + hint) : null));
  const fallbackBox = h('div', {},
    radio('inherit', isBase ? t('按全局设置') : t('继承基础层'),
      isBase ? t('使用“设备与拦截”里的未绑定设置（当前：{0}）', FALLBACK_LABEL[S.cfg.unbound] || t('原样输出')) : t('沿用「{0}」的绑定', S.cfg.layers[0].name)),
    radio('passthrough', t('原样输出'), t('宏键盘原本的按键照常输入')),
    radio('block', t('屏蔽'), t('什么都不发生，适合游戏/演示时防误触')));

  const appChecks = S.cfg.apps.map(app => {
    const other = app.layer && app.layer !== l.id ? layerById(app.layer) : null;
    return h('label', { class: 'check' },
      h('input', { type: 'checkbox', value: app.id, checked: app.layer === l.id }),
      h('span', {}, app.name, h('span', { class: 'muted small' }, ` ${app.processes.join(', ')}` + (other ? t('（当前自动切到「{0}」，勾选将改为本层）', other.name) : ''))));
  });

  const act = (label, fn, opts = {}) => h('button', { type: 'button', class: 'ghost small', disabled: opts.disabled, title: opts.title, onclick: () => { document.querySelector('.modal-overlay')?.remove(); fn(); } }, label);
  const tools = h('div', { class: 'row' },
    act(t('← 左移'), () => moveLayer(l, -1), { disabled: idx === 0 }),
    act(t('右移 →'), () => moveLayer(l, 1), { disabled: idx === S.cfg.layers.length - 1 }),
    act(t('设为基础层'), () => moveLayer(l, -idx), { disabled: isBase, title: t('移到第一位；其他层未绑定且设为“继承”的按键会沿用它') }),
    act(t('复制此层'), () => duplicateLayer(l)));

  const ok = await modal({
    title: t('层属性 · {0}', l.name),
    body: [
      ...f.fields,
      h('div', { class: 'field' }, t('本层未绑定的按键'), fallbackBox),
      h('div', { class: 'field' }, t('这些应用在前台时自动切到本层'), appChecks.length ? h('div', {}, appChecks) : h('p', { class: 'muted small' }, t('还没有应用档案，可在“应用档案”页添加。'))),
      h('div', { class: 'field' }, t(isBase ? t('顺序（第 {0} / {1} 层，基础层；旋钮等“下一层”按此顺序切换）') : t('顺序（第 {0} / {1} 层；旋钮等“下一层”按此顺序切换）'), idx + 1, S.cfg.layers.length), tools),
      h('p', { class: 'muted small' }, t('内部 id：{0}', l.id)),
    ],
    onOk: () => { if (!f.name.value.trim()) { f.name.focus(); return false; } },
  });
  if (!ok) return;
  l.name = f.name.value.trim();
  l.color = f.color.value;
  const fb = fallbackBox.querySelector('input:checked')?.value || 'inherit';
  if (fb === 'inherit') delete l.fallback; else l.fallback = fb;
  for (const box of appChecks.map(c => c.querySelector('input'))) {
    const app = S.cfg.apps.find(a => a.id === box.value);
    if (box.checked) app.layer = l.id;
    else if (app.layer === l.id) delete app.layer;
  }
  changed();
}
function moveLayer(l, delta) {
  const from = S.cfg.layers.indexOf(l);
  const to = Math.max(0, Math.min(S.cfg.layers.length - 1, from + delta));
  if (from === to) return;
  S.cfg.layers.splice(from, 1);
  S.cfg.layers.splice(to, 0, l);
  changed();
  if (to === 0) toast(t('「{0}」已设为基础层，点“保存并应用”生效', l.name));
}
function duplicateLayer(l) {
  let i = 1; while (layerById('layer' + i)) i++;
  const copy = clone(l);
  copy.id = 'layer' + i;
  copy.name = (l.name + t(' 副本')).slice(0, 20);
  S.cfg.layers.splice(S.cfg.layers.indexOf(l) + 1, 0, copy);
  S.viewLayer = copy.id;
  changed();
  toast(t('已复制为「{0}」，点“保存并应用”生效', copy.name));
}
async function deleteViewedLayer() {
  const l = layerById(S.viewLayer);
  if (!l) return;
  if (S.cfg.layers.length === 1) return toast(t('至少需要保留一个层'), true);
  const idx = S.cfg.layers.indexOf(l);
  const apps = S.cfg.apps.filter(a => a.layer === l.id);
  const layerFns = S.cfg.functions.filter(f => f.action?.type === 'layer' && f.action.layer === l.id);
  const overrides = S.cfg.apps.flatMap(a => Object.values(a.overrides)).filter(a => a.type === 'layer' && a.layer === l.id);
  const notes = [t('删除后点“保存并应用”才生效，保存前可“撤销修改”。')];
  if (idx === 0) notes.push(t('· 它是基础层，其他层未绑定的按键会改为继承「{0}」', S.cfg.layers[1].name));
  if (l.id === S.hub.manualLayer) notes.push(t('· 它是当前激活层，保存后会切到第一个层'));
  if (apps.length) notes.push(t('· 应用 {0} 将不再自动切层', apps.map(a => a.name).join(t('、'))));
  if (layerFns.length + overrides.length) notes.push(t('· {0} 个“切换到该层”的动作将失效', layerFns.length + overrides.length));
  if (!await confirmModal(t('删除层「{0}」？', l.name), notes, t('删除'))) return;

  S.cfg.layers.splice(idx, 1);
  for (const a of apps) delete a.layer;
  for (const act of [...layerFns.map(f => f.action), ...overrides]) delete act.layer;
  S.viewLayer = S.cfg.layers[Math.max(0, idx - 1)].id;
  changed();
  toast(t('已删除层「{0}」，点“保存并应用”生效', l.name));
}
function renderLayerTabs() {
  const live = S.hub.layer;
  $('#btnDeleteLayer').disabled = S.cfg.layers.length <= 1;
  $('#layerTabs').replaceChildren(...S.cfg.layers.map(l =>
    h('button', {
      class: 'tab' + (l.id === S.viewLayer ? ' active' : '') + (l.id === live ? ' live' : ''),
      onclick: () => { S.viewLayer = l.id; renderLayerTabs(); renderDevice(); renderInspector(); },
    }, h('span', { class: 'swatch', style: `background:${l.color}` }), l.name)));
}

// ───────────── device SVG ─────────────
const ACCENT_KEYS = new Set(['K1', 'KENTER', 'KMINUS']);
const GRAY_KEYS = new Set(['K5', 'K6', 'K9', 'K0', 'KDOT']);
function shortFnName(controlId) {
  const layer = layerById(S.viewLayer);
  const { fn, inherited, fallback } = resolveFn(layer, controlId);
  if (!fn) return { text: t('未绑定·{0}', FALLBACK_LABEL[fallback] || t('原样输出')), cls: 'fn unbound' };
  const f = fnById(fn);
  let name = f ? f.name : fn;
  if (name.length > 7) name = name.slice(0, 7) + '…';
  return { text: (inherited ? '↳' : '') + name, cls: 'fn' + (inherited ? ' unbound' : '') };
}
function renderDevice() {
  const svg = $('#device');
  const controls = S.cfg.device.controls;
  const groups = new Map();
  for (const c of controls) {
    if (c.kind === 'knob' || c.kind === 'joystick') {
      const key = c.kind + c.rect.join(',');
      if (!groups.has(key)) groups.set(key, { kind: c.kind, rect: c.rect, parts: {} });
      groups.get(key).parts[c.part || 'press'] = c;
    }
  }
  const nodes = [
    s('rect', { class: 'dev-body', x: -0.25, y: -0.25, width: 7.9, height: 4.3, rx: 0.3 }),
    s('rect', { class: 'dev-well', x: 1.3, y: 0.02, width: 6.2, height: 1.16, rx: 0.12 }),
    s('rect', { class: 'dev-well', x: 0.2, y: 1.32, width: 7.3, height: 1.16, rx: 0.12 }),
    s('rect', { class: 'dev-well', x: 0.2, y: 2.62, width: 5.35, height: 1.16, rx: 0.12 }),
  ];
  for (const c of controls) {
    if (c.kind === 'knob' || c.kind === 'joystick') continue;
    const [x, y, w, hgt] = c.rect;
    const fn = shortFnName(c.id);
    const cls = 'k' + (ACCENT_KEYS.has(c.id) ? ' accent' : GRAY_KEYS.has(c.id) ? ' gray' : '') + (S.selected === c.id ? ' selected' : '');
    nodes.push(s('g', { class: cls, 'data-id': c.id, onclick: () => select(c.id) },
      s('title', {}, `${c.id} · ${c.label}`),
      s('rect', { class: 'cap', x: x + 0.04, y: y + 0.04, width: w - 0.08, height: hgt - 0.08, rx: 0.1 }),
      s('rect', { class: 'top', x: x + 0.12, y: y + 0.08, width: w - 0.24, height: hgt - 0.26, rx: 0.08 }),
      s('text', { x: x + w / 2, y: y + hgt * 0.36 }, c.label),
      s('text', { x: x + w / 2, y: y + hgt * 0.64, class: fn.cls }, fn.text)));
  }
  for (const g of groups.values()) {
    const [x, y, w] = g.rect;
    const r = w / 2, cx = x + r, cy = y + r;
    const parts = [];
    const partEl = (partName, d, title) => {
      const c = g.parts[partName];
      if (!c) return null;
      const fn = shortFnName(c.id);
      const shaping = describeInput(c, effectiveInput(layerById(S.viewLayer), c).values);
      return s('path', { class: 'part' + (S.selected === c.id ? ' selected' : ''), d, 'data-id': c.id, onclick: ev => { ev.stopPropagation(); select(c.id); } },
        s('title', {}, `${c.label} · ${fn.text}${shaping ? ' · ' + shaping : ''}`));
    };
    const ring = r * 0.92, inner = r * 0.38;
    if (g.kind === 'knob') {
      parts.push(s('circle', { class: 'cap', cx, cy, r: r * 0.95, fill: '#0b0c0d' }));
      parts.push(s('g', { class: 'k accent' }, s('circle', { class: 'top', cx, cy, r: r * 0.78 })));
      parts.push(partEl('ccw', `M${cx},${cy - ring} A${ring},${ring} 0 0 0 ${cx},${cy + ring} L${cx},${cy + inner} A${inner},${inner} 0 0 1 ${cx},${cy - inner} Z`));
      parts.push(partEl('cw', `M${cx},${cy - ring} A${ring},${ring} 0 0 1 ${cx},${cy + ring} L${cx},${cy + inner} A${inner},${inner} 0 0 0 ${cx},${cy - inner} Z`));
      parts.push(partEl('press', `M${cx - inner},${cy} a${inner},${inner} 0 1 0 ${inner * 2},0 a${inner},${inner} 0 1 0 ${-inner * 2},0`));
      parts.push(s('text', { class: 'arrow', x: cx - r * 0.6, y: cy }, '⟲'), s('text', { class: 'arrow', x: cx + r * 0.6, y: cy }, '⟳'), s('text', { class: 'arrow', x: cx, y: cy }, '●'));
    } else {
      parts.push(s('circle', { class: 'cap', cx, cy, r: r * 0.95, fill: '#0b0c0d' }));
      parts.push(s('g', { class: 'k accent' }, s('circle', { class: 'top', cx, cy, r: r * 0.62 })));
      const wedge = (a0, a1) => {
        const p = (a, rad) => `${cx + rad * Math.cos(a)},${cy + rad * Math.sin(a)}`;
        return `M${p(a0, inner)} L${p(a0, ring)} A${ring},${ring} 0 0 1 ${p(a1, ring)} L${p(a1, inner)} A${inner},${inner} 0 0 0 ${p(a0, inner)} Z`;
      };
      const q = Math.PI / 4;
      parts.push(partEl('up', wedge(-3 * q, -q)), partEl('right', wedge(-q, q)), partEl('down', wedge(q, 3 * q)), partEl('left', wedge(3 * q, 5 * q)));
      parts.push(partEl('press', `M${cx - inner},${cy} a${inner},${inner} 0 1 0 ${inner * 2},0 a${inner},${inner} 0 1 0 ${-inner * 2},0`));
      const off = r * 0.66;
      parts.push(s('text', { class: 'arrow', x: cx, y: cy - off }, '▲'), s('text', { class: 'arrow', x: cx, y: cy + off }, '▼'),
        s('text', { class: 'arrow', x: cx - off, y: cy }, '◀'), s('text', { class: 'arrow', x: cx + off, y: cy }, '▶'), s('text', { class: 'arrow', x: cx, y: cy }, '●'));
    }
    nodes.push(s('g', { 'data-group': g.kind }, parts.filter(Boolean)));
  }
  svg.replaceChildren(...nodes);
}
function flash(controlId, blocked) {
  const els = $$(`[data-id="${controlId}"]`, $('#device'));
  for (const el of els) {
    el.classList.add('pressed');
    if (blocked) el.classList.add('blocked');
    setTimeout(() => el.classList.remove('pressed', 'blocked'), 220);
  }
}
function select(id) {
  S.selected = id;
  renderDevice();
  renderInspector();
}

// ───────────── action editor ─────────────
function chordInput(value, onChange) {
  const input = h('input', { class: 'chord-input', value: value || '', placeholder: t('例如 Ctrl+Shift+S 或 Ctrl+K, Ctrl+C'), list: 'keyNames' });
  input.addEventListener('input', () => onChange(input.value));
  let recording = false;
  const btn = h('button', { class: 'ghost small', type: 'button' }, t('录制'));
  const stop = () => { recording = false; input.classList.remove('recording'); btn.textContent = t('录制'); };
  btn.addEventListener('click', () => {
    if (recording) return stop();
    recording = true; input.classList.add('recording'); btn.textContent = t('按下组合键…'); input.focus();
  });
  input.addEventListener('keydown', ev => {
    if (!recording) return;
    ev.preventDefault();
    const name = keyNameFromEvent(ev);
    if (!name) return; // modifier only — wait for the main key
    const mods = [];
    if (ev.ctrlKey) mods.push('Ctrl');
    if (ev.shiftKey) mods.push('Shift');
    if (ev.altKey) mods.push('Alt');
    if (ev.metaKey) mods.push('Win');
    input.value = [...mods, name].join('+');
    onChange(input.value);
    stop();
  });
  input.addEventListener('blur', stop);
  return h('div', { class: 'row', style: 'flex-wrap:nowrap;margin:0' }, input, btn);
}
function keyNameFromEvent(ev) {
  const code = ev.code;
  if (/^(Control|Shift|Alt|Meta)(Left|Right)$/.test(code)) return null;
  if (/^Key[A-Z]$/.test(code)) return code.slice(3);
  if (/^Digit\d$/.test(code)) return code.slice(5);
  if (/^F\d{1,2}$/.test(code)) return code;
  if (/^Numpad\d$/.test(code)) return 'Num' + code.slice(6);
  const map = {
    ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right', Space: 'Space', Enter: 'Enter', NumpadEnter: 'Enter',
    Escape: 'Esc', Tab: 'Tab', Backspace: 'Backspace', Delete: 'Delete', Insert: 'Insert', Home: 'Home', End: 'End',
    PageUp: 'PageUp', PageDown: 'PageDown', Minus: '-', Equal: '=', BracketLeft: '[', BracketRight: ']', Backslash: '\\',
    Semicolon: ';', Quote: "'", Comma: ',', Period: '.', Slash: '/', Backquote: '`', NumpadAdd: 'NumAdd',
    NumpadSubtract: 'NumSubtract', NumpadMultiply: 'NumMultiply', NumpadDivide: 'NumDivide', NumpadDecimal: 'NumDecimal',
    PrintScreen: 'PrintScreen', Pause: 'Pause', CapsLock: 'CapsLock', ContextMenu: 'Apps',
  };
  return map[code] || null;
}
function actionEditor(action, onChange, { allowForward = true } = {}) {
  const box = h('div', { class: 'action-editor' });
  const draw = () => {
    const a = action;
    const set = (k, v) => { a[k] = v; onChange(a); };
    const typeSel = h('select', {
      onchange: e => {
        const type = e.target.value;
        for (const k of Object.keys(a)) delete a[k];
        a.type = type;
        if (type === 'keys') a.keys = '';
        if (type === 'text') a.text = '';
        if (type === 'macro') a.steps = [{ keys: '' }];
        if (type === 'run') a.path = '';
        if (type === 'mouse') { a.button = 'Left'; }
        if (type === 'layer') a.op = 'next';
        onChange(a); draw();
      },
    }, ...Object.entries(ACTION_TYPES).filter(([t]) => allowForward || t !== 'forward').map(([t, n]) => h('option', { value: t, selected: t === a.type }, n)));
    const fields = [];
    switch (a.type) {
      case 'keys':
        fields.push(h('label', { class: 'field' }, t('按键'), chordInput(a.keys, v => set('keys', v))));
        fields.push(h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: a.hold, onchange: e => set('hold', e.target.checked || undefined) }), t('按住同步（按下即按下、松开即松开，游戏用）')));
        break;
      case 'text':
        fields.push(h('label', { class: 'field' }, t('文本'), h('textarea', { rows: 2, value: a.text, oninput: e => set('text', e.target.value) })));
        break;
      case 'run':
        fields.push(h('label', { class: 'field' }, t('程序 / 文件 / URL'), h('input', { value: a.path, oninput: e => set('path', e.target.value) })));
        fields.push(h('label', { class: 'field' }, t('参数'), h('input', { value: a.args || '', oninput: e => set('args', e.target.value || undefined) })));
        break;
      case 'mouse': {
        const mode = a.button ? 'button' : 'wheel';
        fields.push(h('div', { class: 'grid3' },
          h('label', { class: 'field' }, t('类型'), h('select', { onchange: e => { if (e.target.value === 'button') { a.button = 'Left'; delete a.wheel; } else { delete a.button; delete a.hold; a.wheel = 1; } onChange(a); draw(); } },
            h('option', { value: 'button', selected: mode === 'button' }, t('按键')), h('option', { value: 'wheel', selected: mode === 'wheel' }, t('滚轮')))),
          mode === 'button'
            ? h('label', { class: 'field' }, t('按键'), h('select', { onchange: e => set('button', e.target.value) }, ...['Left', 'Right', 'Middle', 'X1', 'X2'].map(b => h('option', { selected: a.button === b }, b))))
            : h('label', { class: 'field' }, t('垂直格数'), h('input', { type: 'number', value: a.wheel || 0, oninput: e => set('wheel', +e.target.value) })),
          mode === 'button'
            ? h('label', { class: 'check', style: 'margin-top:22px' }, h('input', { type: 'checkbox', checked: a.hold, onchange: e => set('hold', e.target.checked || undefined) }), t('按住'))
            : h('label', { class: 'field' }, t('水平格数'), h('input', { type: 'number', value: a.hWheel || 0, oninput: e => set('hWheel', +e.target.value || undefined) }))));
        break;
      }
      case 'layer':
        fields.push(h('div', { class: 'grid3' },
          h('label', { class: 'field' }, t('操作'), h('select', { onchange: e => { set('op', e.target.value); draw(); } },
            ...[['next', t('下一层')], ['prev', t('上一层')], ['set', t('指定层')]].map(([v, n]) => h('option', { value: v, selected: a.op === v }, n)))),
          a.op === 'set' ? h('label', { class: 'field' }, t('层'), h('select', { onchange: e => set('layer', e.target.value) },
            h('option', { value: '' }, '—'), ...S.cfg.layers.map(l => h('option', { value: l.id, selected: a.layer === l.id }, l.name)))) : null));
        break;
      case 'macro': {
        const list = h('div');
        (a.steps || []).forEach((st, i) => {
          const kind = st.text != null ? 'text' : st.delayMs != null && st.keys == null ? 'delay' : 'keys';
          const valueInput = kind === 'keys' ? chordInput(st.keys, v => { st.keys = v; onChange(a); })
            : kind === 'text' ? h('input', { value: st.text, oninput: e => { st.text = e.target.value; onChange(a); } })
              : h('input', { type: 'number', value: st.delayMs, oninput: e => { st.delayMs = +e.target.value; onChange(a); } });
          list.append(h('div', { class: 'macro-step' },
            h('select', { onchange: e => { a.steps[i] = e.target.value === 'keys' ? { keys: '' } : e.target.value === 'text' ? { text: '' } : { delayMs: 100 }; onChange(a); draw(); } },
              h('option', { value: 'keys', selected: kind === 'keys' }, t('按键')), h('option', { value: 'text', selected: kind === 'text' }, t('文本')), h('option', { value: 'delay', selected: kind === 'delay' }, t('延时ms'))),
            valueInput,
            h('button', { class: 'ghost small', type: 'button', onclick: () => { a.steps.splice(i, 1); onChange(a); draw(); } }, '✕')));
        });
        fields.push(list, h('button', { class: 'ghost small', type: 'button', onclick: () => { a.steps.push({ keys: '' }); onChange(a); draw(); } }, t('+ 步骤')));
        break;
      }
      case 'forward':
        fields.push(h('p', { class: 'muted small' }, t('只把功能事件发给已连接的应用客户端，本地不执行任何按键。')));
        break;
    }
    box.replaceChildren(h('label', { class: 'field' }, t('动作类型'), typeSel), ...fields);
  };
  draw();
  return box;
}


// ───────────── input shaping (detents per trigger, throttle, hold-to-repeat) ─────────────
const INPUT_DEFAULTS = { stepDetents: 1, minIntervalMs: 0, resetMs: 800, repeat: false, repeatDelayMs: 400, repeatIntervalMs: 100 };
const isRotary = c => c.kind === 'knob' && (c.part === 'cw' || c.part === 'ccw');
// knob CW <-> CCW share one physical knob
const siblingRotary = c => isRotary(c)
  ? S.cfg.device.controls.find(x => x !== c && isRotary(x) && x.rect.join() === c.rect.join()) : null;
function cleanInput(obj) {
  const out = {};
  for (const [k, v] of Object.entries(obj || {})) if (k in INPUT_DEFAULTS && v !== INPUT_DEFAULTS[k]) out[k] = v;
  return Object.keys(out).length ? out : null;
}
// Mirrors Router.ResolveInput: layer override → control setting → defaults.
function effectiveInput(layer, c) {
  const ov = layer.input && layer.input[c.id];
  return { scope: ov ? 'layer' : c.input ? 'control' : 'default', values: { ...INPUT_DEFAULTS, ...(ov || c.input || {}) } };
}
function writeInput(layer, c, scope, values) {
  const cleaned = cleanInput(values);
  for (const target of [c, siblingRotary(c)].filter(Boolean)) {
    if (target !== c && !S.inputSyncKnob) continue;
    if (scope === 'layer') {
      layer.input ||= {};
      layer.input[target.id] = cleaned || {};           // an (empty) override still pins this layer to defaults
    } else {
      if (layer.input) { delete layer.input[target.id]; if (!Object.keys(layer.input).length) delete layer.input; }
      if (cleaned) target.input = cleaned; else delete target.input;
    }
  }
}
function describeInput(c, v) {
  const parts = [];
  if (isRotary(c)) {
    if (v.stepDetents > 1) parts.push(t('每 {0} 格触发', v.stepDetents));
    if (v.minIntervalMs > 0) parts.push(t('间隔 ≥ {0}ms', v.minIntervalMs));
  } else if (v.repeat) parts.push(t('按住连发 {0}/{1}ms', v.repeatDelayMs, v.repeatIntervalMs));
  return parts.join(t('，'));
}
function inputSection(layer, c) {
  S.inputSyncKnob ??= true;
  const eff = effectiveInput(layer, c);
  let scope = eff.scope === 'layer' ? 'layer' : 'control';
  const v = { ...eff.values };
  const commit = () => { writeInput(layer, c, scope, v); markDirty(); renderDevice(); };
  const num = (key, label, min, max, step, unit, hint) => h('label', { class: 'field' }, label,
    h('div', { class: 'row', style: 'margin:3px 0 0;flex-wrap:nowrap' },
      h('input', { type: 'number', min, max, step, value: v[key], style: 'width:110px',
        onchange: e => { const n = Math.max(min, Math.min(max, Math.round(+e.target.value || 0))); e.target.value = n; v[key] = n; commit(); renderInspector(); } }),
      h('span', { class: 'muted small' }, unit)),
    hint ? h('span', { class: 'muted small' }, hint) : null);

  // Switching scope never edits values: "shared" drops this layer's override, "layer" starts one from the shared value.
  const setScope = val => {
    for (const target of [c, siblingRotary(c)].filter(Boolean)) {
      if (target !== c && !S.inputSyncKnob) continue;
      if (val === 'layer') {
        layer.input ||= {};
        layer.input[target.id] = cleanInput(target.input) || {};
      } else if (layer.input) {
        delete layer.input[target.id];
        if (!Object.keys(layer.input).length) delete layer.input;
      }
    }
    markDirty(); renderDevice(); renderInspector();
  };
  const scopeRow = h('div', { class: 'row', style: 'gap:14px' },
    ...[['control', t('共用设置（全局一份）')], ['layer', t('「{0}」层单独设置', layer.name)]].map(([val, label]) =>
      h('label', { class: 'radio', style: 'margin:0' },
        h('input', { type: 'radio', name: 'inputScope', value: val, checked: scope === val, onchange: () => setScope(val) }),
        h('span', {}, label))));

  // Where the values come from: the single shared copy and every layer that overrides it.
  const defaultText = isRotary(c) ? t('默认（每格触发）') : t('默认（不连发）');
  const sharedText = describeInput(c, { ...INPUT_DEFAULTS, ...(c.input || {}) }) || defaultText;
  const overrides = S.cfg.layers.filter(l => l.input && l.input[c.id]);
  const inherits = S.cfg.layers.filter(l => !(l.input && l.input[c.id]));
  const overview = h('div', { class: 'input-overview' },
    h('div', {}, h('span', { class: 'muted' }, t('共用设置：')), sharedText,
      h('span', { class: 'muted small' }, t('（{0} 个层使用：{1}）', inherits.length, inherits.map(l => l.name).join(t('、')) || t('无')))),
    overrides.length ? h('div', {}, h('span', { class: 'muted' }, t('单独设置的层：')),
      ...overrides.map((l, i) => h('span', {}, i ? '、' : '',
        h('a', { href: '#', onclick: e => { e.preventDefault(); S.viewLayer = l.id; renderLayerTabs(); renderDevice(); renderInspector(); } }, l.name),
        `（${describeInput(c, { ...INPUT_DEFAULTS, ...l.input[c.id] }) || defaultText}）`))) : null,
    h('div', { class: 'muted small' }, scope === 'control'
      ? t('修改共用设置会影响上面所有使用它的层；单独设置的层不受影响。')
      : t('只影响「{0}」层，优先于共用设置。', layer.name)));

  const fields = [];
  if (isRotary(c)) {
    fields.push(
      num('stepDetents', t('每转几格触发一次'), 1, 50, 1, t('格'), t('转满格数才执行一次动作；反向旋转或停顿会重新计数')),
      num('minIntervalMs', t('最小触发间隔'), 0, 5000, 10, 'ms', t('0 为不限制；转得再快也不会比这更频繁')),
      num('resetMs', t('停顿多久清零未满的格数'), 50, 10000, 50, 'ms'),
      h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: S.inputSyncKnob,
        onchange: e => { S.inputSyncKnob = e.target.checked; if (e.target.checked) commit(); } }),
        h('span', {}, t('左旋 / 右旋使用相同设置'))));
  } else {
    fields.push(
      h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: v.repeat,
        onchange: e => { v.repeat = e.target.checked; commit(); renderInspector(); } }),
        h('span', {}, t('按住连发'), h('span', { class: 'muted small' }, t(' — 按住不放时重复执行动作（“按住同步”类快捷键不适用）')))));
    if (v.repeat) fields.push(h('div', { class: 'grid2', style: 'gap:8px' },
      num('repeatDelayMs', t('首次重复前延迟'), 50, 5000, 10, 'ms'),
      num('repeatIntervalMs', t('重复间隔'), 16, 5000, 10, 'ms')));
    fields.push(num('minIntervalMs', t('最小触发间隔'), 0, 5000, 10, 'ms', t('0 为不限制；防止连续快速按下重复触发')));
  }

  const source = { layer: t('「{0}」层单独设置', layer.name), control: t('共用设置'), default: t('默认值') }[eff.scope];
  return h('div', { class: 'section' },
    h('div', { class: 'row between' }, h('b', {}, isRotary(c) ? t('旋转灵敏度') : t('按键触发')),
      h('span', { class: 'muted small' }, t('本层生效：{0}', source))),
    scopeRow,
    overview,
    ...fields,
    h('div', { class: 'row' },
      scope === 'layer'
        ? h('button', { class: 'ghost small', onclick: () => setScope('control') }, t('取消本层单独设置（改用共用设置）'))
        : h('button', { class: 'ghost small', onclick: () => { Object.assign(v, INPUT_DEFAULTS); commit(); renderInspector(); } }, t('共用设置恢复默认'))),
    h('p', { class: 'muted small' }, t('只影响本地快捷键和功能转发；转发原始控件给应用（如 UE 插件）时每格/每次都会发送，由应用自己决定灵敏度。')));
}

// ───────────── inspector ─────────────
function renderInspector() {
  const root = $('#inspector');
  const c = S.selected && controlById(S.selected);
  if (!c) { root.replaceChildren(h('div', { class: 'empty' }, t('选择左侧的一个按键'))); return; }
  const layer = layerById(S.viewLayer);
  const { fn: fnId, inherited } = resolveFn(layer, c.id);
  const fn = fnId ? fnById(fnId) : null;

  const sigs = h('div', { class: 'sigs' }, ...(c.signatures.length ? c.signatures.map(sig => h('span', { class: 'sig ' + sig.split(':')[0], title: sigTitle(sig) }, sig)) : [h('span', { class: 'muted small' }, t('无签名（尚未学习）'))]));
  const learning = S.hub.learning === c.id;
  const learnBox = learning
    ? h('div', { class: 'learn-box' }, t('请在宏键盘上按一下 '), h('b', {}, c.label), t('（旋钮/摇杆请做对应动作）…'), h('button', { class: 'ghost small', style: 'margin-left:8px', onclick: () => api('DELETE', '/learn') }, t('取消')))
    : null;

  // binding select
  const cats = {};
  for (const f of S.cfg.functions) (cats[f.category || t('其他')] ||= []).push(f);
  const bindSel = h('select', {
    onchange: e => {
      const v = e.target.value;
      if (v === '__new') { const f = newFunction(); layer.map[c.id] = f.id; }
      else if (v === '') delete layer.map[c.id];
      else layer.map[c.id] = v;
      changed();
    },
  },
    h('option', { value: '' }, layer.fallback && layer.fallback !== 'inherit'
      ? t('（未绑定：{0}）', FALLBACK_LABEL[layer.fallback])
      : S.cfg.layers[0] === layer ? t('（未绑定：{0}）', FALLBACK_LABEL[S.cfg.unbound] || t('原样输出')) : t('（继承基础层）')),
    ...Object.entries(cats).map(([cat, fns]) => h('optgroup', { label: cat }, ...fns.map(f => h('option', { value: f.id, selected: layer.map[c.id] === f.id }, `${f.name}  ·  ${describeAction(f.action)}`)))),
    h('option', { value: '__new' }, t('+ 新建功能…')));

  const parts = [
    h('p', { class: 'insp-title' }, c.label),
    h('div', { class: 'insp-sub' }, t('控件 {0}', c.id) + (c.part ? ` · ${c.kind}/${c.part}` : '')),
    h('div', { class: 'row between' }, h('b', {}, t('硬件签名')), h('button', { class: 'ghost small', disabled: !S.hub.connected && S.hub.suppression !== 'codes', onclick: () => api('POST', '/learn/' + encodeURIComponent(c.id)).catch(e => toast(e.message, true)) }, t('学习'))),
    sigs, learnBox,
    h('div', { class: 'section' },
      h('div', { class: 'row between' }, h('b', {}, t('在「'), h('span', { style: `color:${layer.color}` }, layer.name), t('」层绑定的功能'))),
      bindSel,
      inherited ? h('p', { class: 'muted small' }, t('当前继承自基础层「{0}」', S.cfg.layers[0].name))
        : !fnId ? h('p', { class: 'muted small' }, t('本层未绑定时：{0}（可在“层属性”中修改）', FALLBACK_LABEL[resolveFn(layer, c.id).fallback] || t('原样输出'))) : null,
      h('div', { class: 'row' },
        h('button', { class: 'ghost small', onclick: () => simulate(c.id) }, t('模拟按下（使用已保存配置）')))),
    inputSection(layer, c),
  ];

  if (fn) {
    const usage = fnUsage(fn.id);
    parts.push(h('div', { class: 'section' },
      h('div', { class: 'row between' }, h('b', {}, t('功能：{0}', fn.name)), h('span', { class: 'muted small' }, t('id {0} · 被 {1} 处使用', fn.id, usage))),
      h('div', { class: 'grid2', style: 'gap:8px' },
        h('label', { class: 'field' }, t('名称'), h('input', { value: fn.name, oninput: e => { fn.name = e.target.value; markDirty(); renderDevice(); } })),
        h('label', { class: 'field' }, t('分类'), h('input', { value: fn.category, oninput: e => { fn.category = e.target.value; markDirty(); } }))),
      h('b', { class: 'small' }, t('默认动作（系统层）')),
      actionEditor(fn.action, () => { markDirty(); })));

    const appsBox = h('div', { class: 'section' }, h('b', {}, t('应用覆盖')),
      h('p', { class: 'muted small' }, t('前台为该应用时，使用下面的动作代替默认动作。')));
    for (const app of S.cfg.apps) {
      const ov = app.overrides[fn.id];
      appsBox.append(h('div', { class: 'card' },
        h('div', { class: 'card-head' },
          h('label', { class: 'check', style: 'margin:0' }, h('input', {
            type: 'checkbox', checked: !!ov, onchange: e => {
              if (e.target.checked) app.overrides[fn.id] = clone(fn.action); else delete app.overrides[fn.id];
              changed();
            },
          }), h('b', {}, app.name)),
          h('span', { class: 'muted small' }, app.processes.join(', '))),
        ov ? actionEditor(ov, () => markDirty()) : null));
    }
    parts.push(appsBox);
  }
  root.replaceChildren(...parts.filter(Boolean));
}
function sigTitle(sig) {
  const [src, code] = sig.split(':');
  if (src === 'vendor') return t('厂商位图 bit {0}（第 {1} 字节 bit {2}）', code, Math.floor(code / 8), code % 8);
  if (src === 'key') return t('键盘虚拟键 0x{0}', code);
  if (src === 'consumer') return `Consumer usage 0x${code}`;
  return sig;
}
function newFunction() {
  let i = 1;
  while (fnById('custom.' + i)) i++;
  const f = { id: 'custom.' + i, name: t('自定义功能 {0}', i), category: t('自定义'), action: { type: 'keys', keys: 'Ctrl+C' } };
  S.cfg.functions.push(f);
  return f;
}
async function simulate(controlId) {
  try {
    const r = await api('POST', '/simulate', { control: controlId, phase: 'down', tap: true });
    toast(`${controlId} → ${r.function || t('未绑定')} · ${describeAction(r.action)}${S.hub && JSON.stringify(S.cfg) !== JSON.stringify(S.saved) ? t('（注意：未保存的修改不生效）') : ''}`);
  } catch (e) { toast(e.message, true); }
}

// ───────────── apps / functions / settings / json ─────────────
function renderApps() {
  const list = $('#appList');
  list.replaceChildren(...S.cfg.apps.map((app, idx) => h('div', { class: 'card' },
    h('div', { class: 'card-head' }, h('b', {}, app.name || app.id),
      h('button', { class: 'danger small', onclick: async () => { if (await confirmModal(t('删除应用档案「{0}」？', app.name), t('删除后点“保存并应用”才生效。'), t('删除'))) { S.cfg.apps.splice(S.cfg.apps.indexOf(app), 1); changed(); } } }, t('删除'))),
    h('div', { class: 'grid3' },
      h('label', { class: 'field' }, t('名称'), h('input', { value: app.name, oninput: e => { app.name = e.target.value; markDirty(); } })),
      h('label', { class: 'field' }, t('进程（逗号分隔，支持 *）'), h('input', { value: app.processes.join(', '), oninput: e => { app.processes = e.target.value.split(',').map(x => x.trim()).filter(Boolean); markDirty(); } })),
      h('label', { class: 'field' }, t('前台时自动切换到层'), h('select', { onchange: e => { app.layer = e.target.value || undefined; changed(); } },
        h('option', { value: '' }, t('（不切换）')), ...S.cfg.layers.map(l => h('option', { value: l.id, selected: app.layer === l.id }, l.name))))),
    h('label', { class: 'field' }, t('转发给应用客户端'),
      h('select', { onchange: e => { app.forward = e.target.value; delete app.forwardAll; changed(); } },
        ...Object.entries(FORWARD_MODES).map(([v, m]) => h('option', { value: v, selected: (app.forward || 'off') === v }, m.label)))),
    h('p', { class: 'muted small', style: 'margin-top:-4px' }, FORWARD_MODES[app.forward || 'off'].hint),
    h('div', { class: 'muted small' }, t('覆盖 {0} 个功能：', Object.keys(app.overrides).length) + Object.entries(app.overrides).map(([k, v]) => `${fnById(k)?.name || k} → ${describeAction(v)}`).join(t('；'))))));
}
// Every place a function is bound: layer + control, plus app overrides.
function fnUsages(fnId) {
  const list = [];
  for (const l of S.cfg.layers)
    for (const [ctl, fn] of Object.entries(l.map))
      if (fn === fnId) list.push({ layer: l, control: controlById(ctl) || { id: ctl, label: ctl } });
  return list;
}
function highlight(text, terms) {
  text = String(text ?? '');
  if (!terms.length) return text;
  const re = new RegExp('(' + terms.map(t => t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|') + ')', 'ig');
  return text.split(re).map((part, i) => i % 2 ? h('mark', {}, part) : part);
}
function renderFunctions() {
  const f0 = S.fnFilter ||= { q: '', category: '', usage: '' };
  // category options (kept in sync with the functions list)
  const catCounts = {};
  for (const f of S.cfg.functions) catCounts[f.category || t('其他')] = (catCounts[f.category || t('其他')] || 0) + 1;
  const catSel = $('#fnCategory');
  catSel.replaceChildren(h('option', { value: '' }, t('全部分类（{0}）', S.cfg.functions.length)),
    ...Object.entries(catCounts).map(([c, n]) => h('option', { value: c, selected: f0.category === c }, `${c}（${n}）`)));
  if (f0.category && !catCounts[f0.category]) f0.category = '';
  catSel.value = f0.category;
  $('#fnUsage').value = f0.usage;
  if ($('#fnSearch').value !== f0.q) $('#fnSearch').value = f0.q;

  const terms = f0.q.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const overridden = id => S.cfg.apps.filter(a => a.overrides && a.overrides[id]);
  const matches = f => {
    const usages = fnUsages(f.id);
    if (f0.category && (f.category || t('其他')) !== f0.category) return false;
    if (f0.usage === 'used' && !usages.length) return false;
    if (f0.usage === 'unused' && usages.length) return false;
    if (f0.usage === 'override' && !overridden(f.id).length) return false;
    if (!terms.length) return true;
    const hay = [f.id, f.name, f.category, describeAction(f.action), f.action?.keys, f.action?.text, f.action?.path,
      ...usages.map(u => `${u.layer.name} ${u.control.label} ${u.control.id}`),
      ...overridden(f.id).map(a => `${a.name} ${describeAction(a.overrides[f.id])}`)].join(' ').toLowerCase();
    return terms.every(t => hay.includes(t));
  };

  const rows = [h('tr', {}, h('th', {}, 'ID'), h('th', {}, t('名称')), h('th', {}, t('默认动作')), h('th', {}, t('使用位置')), h('th', {}))];
  const cats = {};
  let shown = 0;
  for (const f of S.cfg.functions) if (matches(f)) (cats[f.category || t('其他')] ||= []).push(f);
  for (const [cat, fns] of Object.entries(cats)) {
    rows.push(h('tr', { class: 'cat' }, h('td', { colspan: 5 }, highlight(cat, terms), h('span', { class: 'muted small' }, `  ${fns.length}`))));
    for (const f of fns) {
      shown++;
      const usages = fnUsages(f.id);
      const used = usages.length;
      const apps = overridden(f.id);
      const usageCell = h('td', { class: 'usages' },
        used ? usages.map(u => h('a', {
          href: '#', class: 'usage-chip', title: t('跳到「{0}」层的 {1}', u.layer.name, u.control.label),
          onclick: e => { e.preventDefault(); S.viewLayer = u.layer.id; renderLayerTabs(); select(u.control.id); window.scrollTo({ top: 0, behavior: 'smooth' }); },
        }, h('span', { class: 'swatch', style: `background:${u.layer.color}` }), highlight(`${u.layer.name}·${u.control.label}`, terms)))
          : h('span', { class: 'muted small' }, t('未使用')),
        apps.length ? h('div', { class: 'muted small' }, t('覆盖：'), highlight(apps.map(a => a.name).join('、'), terms)) : null);
      rows.push(h('tr', { 'data-fn': f.id },
        h('td', { class: 'mono' }, highlight(f.id, terms)), h('td', {}, highlight(f.name, terms)), h('td', { class: 'mono' }, highlight(describeAction(f.action), terms)), usageCell,
        h('td', { class: 'actions' },
          h('button', { class: 'ghost small', onclick: () => api('POST', '/execute', { action: f.action }).then(() => toast(t('已执行 {0}', describeAction(f.action)))).catch(e => toast(e.message, true)) }, t('执行')),
          ' ',
          h('button', {
            class: 'danger small', disabled: used > 0, title: used ? t('仍被层使用') : '', onclick: () => {
              S.cfg.functions = S.cfg.functions.filter(x => x !== f);
              for (const a of S.cfg.apps) delete a.overrides[f.id];
              changed();
            },
          }, t('删除')))));
    }
  }
  if (!shown) rows.push(h('tr', {}, h('td', { colspan: 5, class: 'muted', style: 'text-align:center;padding:24px' },
    t('没有匹配的功能 '), h('button', { class: 'ghost small', onclick: () => { S.fnFilter = { q: '', category: '', usage: '' }; renderFunctions(); } }, t('清除筛选')))));
  $('#fnTable').replaceChildren(...rows);
  $('#fnCount').textContent = shown === S.cfg.functions.length ? t('共 {0} 个', shown) : t('显示 {0} / 共 {1} 个', shown, S.cfg.functions.length);
}
function renderSettings() {
  for (const r of $$('input[name=supp]')) r.checked = r.value === S.cfg.suppression;
  for (const r of $$('input[name=unbound]')) r.checked = r.value === S.cfg.unbound;
  $('#devName').value = S.cfg.device.name;
  $('#devMatch').value = S.cfg.device.match.join(', ');
  $('#devCollections').textContent = (S.hub.collections || []).map(c => t('已打开 page 0x{0} usage 0x{1} len {2}', c.usagePage.toString(16), c.usage.toString(16), c.inputLength)).join('\n') || t('未打开任何可读集合');
  $('#waitMs').value = S.hub.correlateWaitMs ?? 8;
  $('#waitMsOut').textContent = S.hub.correlateWaitMs ?? 8;
  const hs = S.hub.stats || {};
  $('#hubPhysical').textContent = hs.physicalDowns ?? 0;
  $('#hubSuppressed').textContent = hs.suppressed ?? 0;
  $('#hubLeaked').textContent = hs.leaked ?? 0;
  $('#hubWaited').textContent = hs.waitedForReport ?? 0;
  $('#hubMaxWait').textContent = hs.maxWaitMs ?? 0;
  $('#clientList').replaceChildren(...(S.hub.clients || []).map(c => h('li', {}, describeClient(c))));
  const vg = S.hub.volumeGuard || {};
  $('#knobVolumeGuard').checked = S.cfg.knobVolumeGuard !== false;
  $('#volumeGuardStatus').textContent = vg.available
    ? t('音频端点可用 · 当前音量 {0}% · 已恢复 {1} 次', Math.round((vg.level || 0) * 100), vg.restores || 0) + (vg.guarding ? t(' · 保护中') : '')
    : t('未找到默认音频输出设备，音量保护不可用');
  $('#pipeName').textContent = S.hub.pipe || '—';
  if (!(S.hub.clients || []).length) $('#clientList').append(h('li', { class: 'muted' }, t('暂无')));
}
function renderJson() { $('#jsonText').value = JSON.stringify(S.cfg, null, 2); $('#jsonErrors').textContent = ''; }
function renderActionTester() {
  $('#actionTester').replaceChildren(actionEditor(S.testAction, () => { }, { allowForward: false }));
}

// ───────────── events ─────────────
function logEvent(ev) {
  if (ev.type === 'signal' && !$('#evShowSignals').checked) return;
  const log = $('#eventLog');
  let text;
  switch (ev.type) {
    case 'control': text = ev.raw
      ? `${ev.control} ${ev.phase} [${ev.source}] ` + t('原始控件 → 应用×{0}', ev.forwarded) + (ev.block ? t(' 拦截') : '') + ` @${ev.foreground}`
      : `${ev.control} ${ev.phase} [${ev.source}] ` + t('层={0} 功能={1} 动作={2}', ev.layer, ev.function ?? '—', ev.action) + (ev.step ? t(' 格数={0}', ev.step) : '') + (ev.gated && ev.phase === 'down' ? t(' 未触发') : '') + (ev.block ? t(' 拦截') : '') + (ev.forwarded ? t(' 转发×{0}', ev.forwarded) : '') + (ev.executed ? t(' 已执行') : '') + ` @${ev.foreground}`; break;
    case 'repeat': text = t('{0} 连发 {1}', ev.control, ev.action); break;
    case 'signal': text = `${ev.signature} ${ev.phase} → ${ev.control ?? t('未知')}`; break;
    case 'suppressed': text = `${ev.control} ${ev.key} ${ev.up ? 'up' : 'down'}`; break;
    case 'foreground': text = `${ev.process} (${ev.pid}) ${ev.app ? '→ ' + ev.app : ''} ` + t('层={0}', ev.layer); break;
    case 'layer': text = t('手动层={0} 生效层={1}', ev.manualLayer, ev.layer); break;
    case 'learn': text = `${ev.state} ${ev.control ?? ''} ${(ev.signatures || []).join(' ')} ${(ev.errors || []).join(' ')}`; break;
    case 'device': text = ev.connected ? t('已连接') + (TRANSPORT_LABEL[ev.transport] ? ' (' + TRANSPORT_LABEL[ev.transport] + ')' : '') : t('已断开'); break;
    case 'clients': text = (ev.clients || []).map(c => c.app).join(', ') || t('无'); break;
    case 'error': text = ev.message; break;
    default: text = JSON.stringify(ev);
  }
  const now = new Date();
  log.prepend(h('li', {}, h('span', { class: 't' }, now.toLocaleTimeString('zh-CN', { hour12: false }) + '.' + String(now.getMilliseconds()).padStart(3, '0')),
    h('span', { class: 'ty ty-' + ev.type }, ev.type), h('span', {}, text)));
  while (log.children.length > 300) log.lastChild.remove();
}
async function refreshState() {
  try { S.hub = await api('GET', '/state'); } catch { }
  renderChips(); renderLayerTabs(); renderSettings();
}
function connectWs() {
  const ws = new WebSocket(`ws://${location.host}/ws`);
  S.ws = ws;
  ws.onopen = () => { S.wsOk = true; refreshState(); };
  ws.onclose = () => { S.wsOk = false; renderChips(); setTimeout(connectWs, 1500); };
  ws.onmessage = async msg => {
    const ev = JSON.parse(msg.data);
    if (ev.type !== 'hello' && ev.type !== 'pong') logEvent(ev);
    switch (ev.type) {
      case 'hello': S.hub = ev.state; renderChips(); renderLayerTabs(); break;
      case 'control':
        if (ev.phase === 'down') { flash(ev.control, ev.block); S.stats.controls++; updateStats(); }
        break;
      case 'signal': if (ev.control && ev.phase === 'down') flash(ev.control, false); break;
      case 'suppressed': if (!ev.up) { S.stats.suppressed++; updateStats(); } scheduleStateRefresh(); break;
      case 'leak': toast(t('{0} 的原始按键漏出（物理报告晚于钩子），可调大等待时间', ev.control), true); scheduleStateRefresh(); break;
      case 'foreground': case 'layer': case 'device': case 'clients': case 'battery': refreshState(); break;
      case 'learn':
        await refreshState();
        if (ev.state === 'done') {
          const server = await api('GET', '/config');
          // merge learned signatures into both the saved and working copy so other edits survive
          for (const target of [S.saved, S.cfg]) {
            for (const c of target.device.controls) {
              const sc = server.device.controls.find(x => x.id === c.id);
              if (sc) c.signatures = sc.signatures;
            }
          }
          markDirty(); renderInspector();
          toast(t('{0} 学习完成：{1}', ev.control, ev.signatures.join(' ')));
        } else if (ev.state === 'error') toast(t('学习失败：') + ev.errors.join('\n'), true);
        else renderInspector();
        break;
      case 'error': toast(ev.message, true); break;
      case 'config':
        break;
    }
  };
}
function scheduleStateRefresh() {
  clearTimeout(scheduleStateRefresh.t);
  scheduleStateRefresh.t = setTimeout(refreshState, 300);
}
function updateStats() {
  $('#statControls').textContent = S.stats.controls;
  $('#statSuppressed').textContent = S.stats.suppressed;
  $('#statTyped').textContent = S.stats.typed;
}

// ───────────── wiring ─────────────
function wire() {
  $$('#bottomTabs button').forEach(b => b.addEventListener('click', () => {
    $$('#bottomTabs button').forEach(x => x.classList.toggle('active', x === b));
    $$('.panel').forEach(p => p.hidden = p.dataset.panel !== b.dataset.tab);
    if (b.dataset.tab === 'json') renderJson();
    try { localStorage.setItem('macrohub.tab', b.dataset.tab); } catch { }
  }));
  let lastTab = null;
  try { lastTab = localStorage.getItem('macrohub.tab'); } catch { }
  if (lastTab && lastTab !== 'json') $(`#bottomTabs [data-tab="${lastTab}"]`)?.click();
  $('#btnSave').addEventListener('click', async () => {
    try {
      await api('PUT', '/config', S.cfg);
      S.saved = clone(S.cfg);
      markDirty(); toast(t('已保存并应用')); refreshState();
    } catch (e) { toast(t('保存失败：') + '\n' + e.message, true); }
  });
  $('#btnRevert').addEventListener('click', () => { S.cfg = clone(S.saved); changed(); });
  $('#btnActivateLayer').addEventListener('click', () => api('POST', '/layer', { op: 'set', layer: S.viewLayer }).then(refreshState).catch(e => toast(e.message, true)));
  $('#btnAddLayer').addEventListener('click', async () => {
    const f = layerForm({ name: t('自定义层'), color: '#b07cff' });
    const source = h('select', {},
      h('option', { value: '' }, t('空白（未绑定的按键继承基础层）')),
      ...S.cfg.layers.map(l => h('option', { value: l.id }, t('复制「{0}」的绑定', l.name))));
    const ok = await modal({
      title: t('新建层'),
      body: [...f.fields, h('label', { class: 'field' }, t('初始绑定'), source),
        h('p', { class: 'muted small' }, t('创建后点按键逐个选择功能。可在“应用档案”里让某个程序在前台时自动切到该层，或把“下一层”功能绑到旋钮按下。'))],
      okText: t('创建'),
      onOk: () => { if (!f.name.value.trim()) { f.name.focus(); return false; } },
    });
    if (!ok) return;
    let i = 1; while (layerById('layer' + i)) i++;
    const src = layerById(source.value);
    S.cfg.layers.push({ id: 'layer' + i, name: f.name.value.trim(), color: f.color.value, map: src ? clone(src.map) : {} });
    S.viewLayer = 'layer' + i;
    changed();
    toast(t('已创建层「{0}」，点“保存并应用”生效', f.name.value.trim()));
  });
  $('#btnLayerProps').addEventListener('click', openLayerProps);
  $('#btnDeleteLayer').addEventListener('click', deleteViewedLayer);
  $('#btnAddApp').addEventListener('click', () => {
    let i = 1; while (S.cfg.apps.find(a => a.id === 'app' + i)) i++;
    S.cfg.apps.push({ id: 'app' + i, name: t('新应用'), processes: ['example.exe'], forward: 'off', overrides: {} });
    changed();
  });
  $('#btnAddFunction').addEventListener('click', () => {
    const f = newFunction();
    S.fnFilter = { q: '', category: '', usage: '' }; // make sure the new one is visible
    changed();
    const row = document.querySelector(`#fnTable tr[data-fn="${f.id}"]`);
    row?.scrollIntoView({ block: 'center' });
    row?.classList.add('flash');
    toast(t('已新建「{0}」，在按键检查器里绑定并编辑它', f.name));
  });
  $('#fnSearch').addEventListener('input', e => { S.fnFilter.q = e.target.value; renderFunctions(); });
  $('#fnSearch').addEventListener('keydown', e => { if (e.key === 'Escape') { S.fnFilter.q = ''; renderFunctions(); } });
  $('#fnCategory').addEventListener('change', e => { S.fnFilter.category = e.target.value; renderFunctions(); });
  $('#fnUsage').addEventListener('change', e => { S.fnFilter.usage = e.target.value; renderFunctions(); });
  document.addEventListener('keydown', e => {
    if (e.key !== '/' || $('[data-panel=functions]').hidden) return;
    if (['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName) || document.querySelector('.modal-overlay')) return;
    e.preventDefault(); $('#fnSearch').focus();
  });
  $$('input[name=supp]').forEach(r => r.addEventListener('change', () => { S.cfg.suppression = r.value; markDirty(); }));
  $$('input[name=unbound]').forEach(r => r.addEventListener('change', () => { S.cfg.unbound = r.value; markDirty(); }));
  $('#knobVolumeGuard').addEventListener('change', e => { S.cfg.knobVolumeGuard = e.target.checked; markDirty(); });
  $('#lightEnabled').addEventListener('change', e => {
    lightingConfig().enabled = e.target.checked;
    markDirty(); renderLighting();
    if (e.target.checked) previewLighting();
  });
  $('#lightMode').addEventListener('change', e => setLighting({ mode: +e.target.value }));
  $('#lightBright').addEventListener('input', e => setLighting({ brightness: +e.target.value }));
  $('#lightSpeed').addEventListener('input', e => setLighting({ speed: +e.target.value }));
  $('#lightColor').addEventListener('input', e => setLighting({ color: e.target.value }));
  $('#lightColorHex').addEventListener('change', e => {
    const v = e.target.value.trim();
    if (/^#?[0-9a-f]{6}$/i.test(v)) setLighting({ color: v.startsWith('#') ? v.toLowerCase() : '#' + v.toLowerCase() });
    else renderLighting();
  });
  $$('#lightDir button').forEach(b => b.addEventListener('click', () => setLighting({ direction: +b.dataset.dir })));
  $('#lightPerLayer').addEventListener('change', e => {
    const layer = layerById(S.viewLayer);
    if (!layer) return;
    if (e.target.checked) layer.lighting = { ...lightingConfig().default };
    else delete layer.lighting;
    markDirty(); renderLighting(); previewLighting();
  });
  $('#devName').addEventListener('input', e => { S.cfg.device.name = e.target.value; markDirty(); });
  $('#devMatch').addEventListener('input', e => { S.cfg.device.match = e.target.value.split(',').map(x => x.trim()).filter(Boolean); markDirty(); });
  $('#btnListDevices').addEventListener('click', async () => {
    const devs = await api('GET', '/devices');
    $('#deviceList').replaceChildren(...devs.map(d => h('li', {}, d.id + '  ', h('span', { class: 'muted' }, d.collections.map(c => `${c.usagePage.toString(16)}/${c.usage.toString(16)}`).join(' ')),
      ' ', h('button', { class: 'ghost small', onclick: () => { const m = (d.id.match(/vid_[0-9a-f]{4}&pid_[0-9a-f]{4}/i) || [d.id])[0].toUpperCase(); // added, not replaced: the pad answers under a different id per connection (cable, 2.4G, Bluetooth)
        if (!S.cfg.device.match.some(x => x.toUpperCase() === m)) S.cfg.device.match.push(m);
        renderSettings(); markDirty(); } }, t('添加')))));
  });
  $('#waitMs').addEventListener('input', e => { $('#waitMsOut').textContent = e.target.value; });
  $('#waitMs').addEventListener('change', e => api('POST', '/tuning', { correlateWaitMs: +e.target.value }).then(refreshState));
  $('#evClear').addEventListener('click', () => $('#eventLog').replaceChildren());
  $('#testInput').addEventListener('keydown', () => { S.stats.typed++; updateStats(); });
  $('#statReset').addEventListener('click', () => { S.stats = { controls: 0, suppressed: 0, typed: 0 }; $('#testInput').value = ''; updateStats(); api('DELETE', '/stats').then(refreshState); });
  $('#btnRunAction').addEventListener('click', () => api('POST', '/execute', { action: S.testAction }).then(r => toast(t('已执行 {0}', r.queued))).catch(e => toast(e.message, true)));
  $('#btnJsonLoad').addEventListener('click', renderJson);
  $('#btnJsonApply').addEventListener('click', async () => {
    try {
      const cfg = JSON.parse($('#jsonText').value);
      const { errors } = await api('POST', '/config/validate', cfg);
      $('#jsonErrors').textContent = errors.join('\n');
      if (errors.length) return;
      S.cfg = cfg; if (!layerById(S.viewLayer)) S.viewLayer = S.cfg.layers[0].id;
      changed(); toast(t('已应用到编辑器（尚未保存）'));
    } catch (e) { $('#jsonErrors').textContent = e.message; }
  });
  $('#btnJsonExport').addEventListener('click', () => {
    const a = h('a', { href: URL.createObjectURL(new Blob([JSON.stringify(S.cfg, null, 2)], { type: 'application/json' })), download: 'macrohub-config.json' });
    a.click(); URL.revokeObjectURL(a.href);
  });
  $('#jsonImport').addEventListener('change', async e => {
    const file = e.target.files[0]; if (!file) return;
    $('#jsonText').value = await file.text();
    e.target.value = '';
    toast(t('已载入文件，点“应用到编辑器”校验并应用'));
  });
  $('#btnReset').addEventListener('click', async () => {
    if (!await confirmModal(t('恢复默认配置？'), t('会覆盖当前已保存的配置（包括学习到的按键签名），且无法撤销。'), t('恢复默认'))) return;
    await api('POST', '/config/reset');
    await loadConfig(); toast(t('已恢复默认配置'));
  });
}

async function loadConfig() {
  S.saved = await api('GET', '/config');
  S.cfg = clone(S.saved);
  if (!S.viewLayer || !layerById(S.viewLayer)) S.viewLayer = S.cfg.layers[0].id;
  changed();
  renderJson();
}

async function init() {
  I18N.translateDom();
  // The "active" badge on the layer tab is CSS content, so its text comes in through a variable.
  document.documentElement.style.setProperty('--live-label', JSON.stringify(t('激活')));
  // The button shows the language it switches to.
  $('#btnLang').textContent = I18N.lang === 'zh' ? 'EN' : '中文';
  $('#btnLang').addEventListener('click', () => I18N.setLang(I18N.lang === 'zh' ? 'en' : 'zh'));
  wire();
  S.keyNames = await api('GET', '/keys');
  document.body.append(h('datalist', { id: 'keyNames' }, ...S.keyNames.map(k => h('option', { value: k }))));
  await loadConfig();
  await refreshState();
  if (S.hub.manualLayer) { S.viewLayer = S.hub.layer || S.hub.manualLayer; renderLayerTabs(); renderDevice(); }
  renderActionTester();
  connectWs();
}
init().catch(e => toast(t('初始化失败：') + e.message, true));

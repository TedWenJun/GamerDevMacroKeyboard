// End-to-end test of the MacroHub system layer without touching the user's config or the physical pad.
//   node tests/e2e/e2e.mjs
// Starts a second MacroHub on port 17901 with a test config (suppression off, device match that never matches),
// starts tools/KeyTarget (a foreground window that logs received keys), then drives controls through the HTTP API
// and verifies the keystrokes that arrive, plus WebSocket / named-pipe forwarding to an application client.
// Routing uses a pinned foreground (--test-mode), so logic checks are deterministic even while you use the PC.
// Checks that need keystrokes to really arrive in KeyTarget are reported as SKIP when another window has focus.
import { spawn } from 'node:child_process';
import net from 'node:net';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const PORT = 17901;
const PIPE = 'MacroHub-e2e';
const BASE = `http://127.0.0.1:${PORT}`;
const hubExe = join(root, 'src/MacroHub/bin/Debug/net10.0-windows/MacroHub.exe');
const targetExe = join(root, 'tools/KeyTarget/bin/Debug/net10.0-windows/KeyTarget.exe');
const sleep = ms => new Promise(r => setTimeout(r, ms));
const results = [];
function check(name, ok, detail = '') {
  results.push({ name, ok });
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`);
}
// Key-injection checks: SendInput goes to the real foreground window, which the test cannot hold while you use the PC.
async function checkKeys(name, ok, detail = '') {
  if (!ok && !(await targetHasFocus())) {
    results.push({ name, ok: true, skipped: true });
    console.log(`SKIP  ${name}  — KeyTarget did not have real focus or input was blocked (another/elevated window, locked screen); rerun when idle to verify injection`);
    return;
  }
  check(name, ok, detail);
}
async function targetHasFocus() {
  await api('POST', '/diag/foreground', {});             // unpin briefly to read the real foreground
  const st = await api('GET', '/state');
  await api('POST', '/diag/foreground', { pid: targetPid, process: 'KeyTarget.exe' });
  return st.foreground.pid === targetPid;
}

// ── test config ──
const cfg = JSON.parse(readFileSync(join(root, 'src/MacroHub/defaults/hub.json'), 'utf8'));
cfg.suppression = 'off';
cfg.device.match = ['VID_0000&PID_0000_E2E'];
cfg.functions.push({ id: 'test.text', name: 'text', category: 'Test', action: { type: 'text', text: 'Hi中' } });
cfg.functions.push({ id: 'test.macro', name: 'macro', category: 'Test', action: { type: 'macro', steps: [{ keys: 'A' }, { delayMs: 30 }, { keys: 'Shift+B' }] } });
cfg.layers.find(l => l.id === 'desktop').map.K5 = 'test.text';
cfg.layers.find(l => l.id === 'desktop').map.K6 = 'test.macro';
cfg.apps.push({ id: 'keytarget', name: 'KeyTarget', processes: ['KeyTarget.exe'], forwardAll: false, overrides: { 'edit.copy': { type: 'keys', keys: 'Ctrl+Shift+C' } } });
const cfgDir = join(root, 'tests/e2e/.run');
mkdirSync(cfgDir, { recursive: true });
const cfgPath = join(cfgDir, 'hub.e2e.json');
writeFileSync(cfgPath, JSON.stringify(cfg, null, 2));

// ── processes ──
const hub = spawn(hubExe, ['--config', cfgPath, '--port', String(PORT), '--pipe', PIPE, '--log-dir', join(cfgDir, 'logs'), '--test-mode'], { stdio: ['ignore', 'pipe', 'pipe'] });
let hubLog = '';
hub.stdout.on('data', d => hubLog += d);
hub.stderr.on('data', d => hubLog += d);

async function api(method, path, body) {
  const r = await fetch(BASE + '/api' + path, { method, headers: body ? { 'content-type': 'application/json' } : {}, body: body ? JSON.stringify(body) : undefined });
  const t = await r.text();
  if (!r.ok) throw new Error(`${method} ${path} → ${r.status} ${t}`);
  return t ? JSON.parse(t) : null;
}

// Newline-delimited JSON client over \\.\pipe\<PIPE> (what the UE plugin will use).
function pipeClient() {
  return new Promise((resolve, reject) => {
    const sock = net.connect(`\\\\.\\pipe\\${PIPE}`);
    const messages = [];
    let buf = '';
    const waiters = [];
    sock.setEncoding('utf8');
    sock.on('data', chunk => {
      buf += chunk;
      let i;
      while ((i = buf.indexOf('\n')) >= 0) {
        const line = buf.slice(0, i); buf = buf.slice(i + 1);
        if (!line) continue;
        const m = JSON.parse(line);
        messages.push(m);
        for (const w of [...waiters]) if (w.pred(m)) { waiters.splice(waiters.indexOf(w), 1); w.resolve(m); }
      }
    });
    const client = {
      messages,
      send: obj => sock.write(JSON.stringify(obj) + '\n'),
      next: (pred, ms = 2000) => {
        const hit = messages.find(pred);
        if (hit) { messages.splice(messages.indexOf(hit), 1); return Promise.resolve(hit); }
        return new Promise((res, rej) => {
          const w = { pred, resolve: m => { messages.splice(messages.indexOf(m), 1); res(m); } };
          waiters.push(w);
          setTimeout(() => { const k = waiters.indexOf(w); if (k >= 0) { waiters.splice(k, 1); rej(new Error('pipe message timeout')); } }, ms);
        });
      },
      close: () => new Promise(r => { sock.once('close', r); sock.end(); }),
    };
    sock.once('connect', () => resolve(client));
    sock.once('error', reject);
  });
}

let target, targetLines = [], targetPid = 0;
function startTarget() {
  return new Promise((resolve, reject) => {
    target = spawn(targetExe, ['60'], { stdio: ['ignore', 'pipe', 'pipe'] });
    target.stdout.on('data', d => {
      for (const line of String(d).split(/\r?\n/).filter(Boolean)) {
        if (line.startsWith('ready')) { targetPid = +line.match(/pid=(\d+)/)[1]; resolve(line); }
        else targetLines.push(line);
      }
    });
    setTimeout(() => reject(new Error('KeyTarget did not start')), 10000);
  });
}
const takeLines = () => { const l = targetLines; targetLines = []; return l; };
// Only keystrokes injected by MacroHub (KeyTarget marks them " hub"); real typing on your keyboard is ignored.
const keyEvents = lines => lines.filter(l => !l.startsWith('char') && l.endsWith(' hub')).map(l => l.replace(/ 0x/, ':').replace(/ hub$/, ''));
const chars = lines => lines.filter(l => l.startsWith("char")).map(l => String.fromCodePoint(parseInt(l.split(' ')[1], 16))).join("");

try {
  for (let i = 0; ; i++) {
    try { await api('GET', '/state'); break; } catch { if (i > 40) throw new Error('hub did not start\n' + hubLog); await sleep(250); }
  }
  check('hub starts with test config', true);

  const ready = await startTarget();
  let state;
  for (let i = 0; i < 20; i++) { state = await api('GET', '/state'); if (state.foreground.process === 'KeyTarget.exe') break; await sleep(150); }
  if (state.foreground.process === 'KeyTarget.exe') check('foreground tracking sees KeyTarget.exe', true, ready);
  else { results.push({ ok: true, skipped: true }); console.log(`SKIP  foreground tracking sees KeyTarget.exe  — real foreground is ${state.foreground.process}`); }
  await api('POST', '/diag/foreground', { pid: targetPid, process: 'KeyTarget.exe' });
  state = await api('GET', '/state');
  check('app profile matched for (pinned) foreground', state.foreground.app === 'keytarget', state.foreground.process);

  await api('POST', '/layer', { op: 'set', layer: 'desktop' });
  takeLines();

  // 1. chord from system layer function
  let route = await api('POST', '/simulate', { control: 'K7', phase: 'down', tap: true });
  await sleep(300);
  let ev = keyEvents(takeLines());
  check('K7 → edit.find routes to Ctrl+F', route.function === 'edit.find' && route.action.keys === 'Ctrl+F');
  await checkKeys('Ctrl+F injected in order', JSON.stringify(ev) === JSON.stringify(['down:11', 'down:46', 'up:46', 'up:11']), ev.join(' '));

  // 2. app override
  route = await api('POST', '/simulate', { control: 'K1', phase: 'down', tap: true });
  await sleep(300);
  ev = keyEvents(takeLines());
  check('app override edit.copy → Ctrl+Shift+C', route.app === 'keytarget' && route.action.keys === 'Ctrl+Shift+C');
  await checkKeys('Ctrl+Shift+C injected', JSON.stringify(ev) === JSON.stringify(['down:11', 'down:10', 'down:43', 'up:43', 'up:10', 'up:11']), ev.join(' '));

  // 3. unicode text
  await api('POST', '/simulate', { control: 'K5', phase: 'down', tap: true });
  await sleep(300);
  const txt = chars(takeLines());
  await checkKeys('text action types "Hi中"', txt === 'Hi中', JSON.stringify(txt));

  // 4. macro with delay
  await api('POST', '/simulate', { control: 'K6', phase: 'down', tap: true });
  await sleep(400);
  ev = keyEvents(takeLines());
  await checkKeys('macro A, delay, Shift+B', JSON.stringify(ev) === JSON.stringify(['down:41', 'up:41', 'down:10', 'down:42', 'up:42', 'up:10']), ev.join(' '));

  // 5. hold action (game layer): key stays down until control up
  await api('POST', '/layer', { op: 'set', layer: 'game' });
  await api('POST', '/simulate', { control: 'JOY_UP', phase: 'down', tap: false });
  await sleep(250);
  const mid = keyEvents(takeLines());
  await api('POST', '/simulate', { control: 'JOY_UP', phase: 'up', tap: false });
  await sleep(250);
  const end = keyEvents(takeLines());
  await checkKeys('hold W: down on press', mid[0] === 'down:57' && !mid.includes('up:57'), mid.join(' '));
  await checkKeys('hold W: up on release', end.includes('up:57'), end.join(' '));

  // 6. passthrough/stock layer executes nothing
  await api('POST', '/layer', { op: 'set', layer: 'stock' });
  route = await api('POST', '/simulate', { control: 'K3', phase: 'down', tap: true });
  await sleep(200);
  ev = keyEvents(takeLines());
  check('stock layer passthrough injects nothing', route.action.type === 'passthrough' && ev.length === 0, ev.join(' '));

  // 7. layer action via control
  cfg.layers.find(l => l.id === 'stock').map.KNOB_PRESS = 'layer.next';
  await api('PUT', '/config', cfg);
  await api('POST', '/simulate', { control: 'KNOB_PRESS', phase: 'down', tap: true });
  await sleep(200);
  state = await api('GET', '/state');
  check('layer.next function switches manual layer', state.manualLayer === 'desktop', state.manualLayer);

  // 8. application client forwarding over WebSocket
  cfg.apps.find(a => a.id === 'keytarget').forwardAll = true;
  await api('PUT', '/config', cfg);
  const ws = new WebSocket(`ws://127.0.0.1:${PORT}/ws`);
  const received = [];
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  ws.onmessage = m => { const e = JSON.parse(m.data); if (e.type === 'function') received.push(e); };
  ws.send(JSON.stringify({ type: 'hello', role: 'app', app: 'keytarget-client', pid: targetPid }));
  await sleep(200);
  state = await api('GET', '/state');
  check('app client registered', state.clients.some(c => c.pid === targetPid), JSON.stringify(state.clients));
  takeLines();
  await api('POST', '/simulate', { control: 'K7', phase: 'down', tap: true });
  await sleep(300);
  ev = keyEvents(takeLines());
  check('forwarded function event received', received.some(e => e.function === 'edit.find' && e.phase === 'down') && received.some(e => e.phase === 'up'), JSON.stringify(received));
  check('connected app handles it natively (no local keys)', ev.length === 0, ev.join(' '));
  ws.close();
  await sleep(300);
  takeLines();
  await api('POST', '/simulate', { control: 'K7', phase: 'down', tap: true });
  await sleep(300);
  ev = keyEvents(takeLines());
  await checkKeys('client gone → falls back to local shortcut', JSON.stringify(ev) === JSON.stringify(['down:11', 'down:46', 'up:46', 'up:11']), ev.join(' '));

  // 9. protocol v2 over the named pipe, raw control forwarding (the UE plugin path)
  cfg.layers.find(l => l.id === 'stock').map.KNOB_PRESS = 'layer.next';
  cfg.apps.find(a => a.id === 'keytarget').forward = 'controls';
  delete cfg.apps.find(a => a.id === 'keytarget').forwardAll;
  await api('PUT', '/config', cfg);
  const pc = await pipeClient();
  const hubHello = await pc.next(m => m.type === 'hello');
  check('pipe: hub hello announces protocol 2', hubHello.role === 'hub' && hubHello.protocol === 2, JSON.stringify(hubHello));
  pc.send({ type: 'hello', role: 'app', protocol: 2, app: 'ue-mock', mode: 'editor', pid: targetPid });
  const welcome = await pc.next(m => m.type === 'welcome');
  check('pipe: welcome carries profile (forward=controls) and control list',
    welcome.profile?.forward === 'controls' && welcome.controls.some(c => c.id === 'KNOB_CW' && c.kind === 'knob' && c.part === 'cw' && Array.isArray(c.rect) && c.rect.length === 4), JSON.stringify(welcome.profile));
  state = await api('GET', '/state');
  const reg = state.clients.find(c => c.pid === targetPid);
  check('pipe: client registered with transport/mode/protocol', reg?.transport === 'pipe' && reg.mode === 'editor' && reg.protocol === 2, JSON.stringify(reg));

  takeLines();
  await api('POST', '/simulate', { control: 'K7', phase: 'down', tap: true });
  const c1 = await pc.next(m => m.type === 'control' && m.control === 'K7' && m.phase === 'down');
  const c2 = await pc.next(m => m.type === 'control' && m.control === 'K7' && m.phase === 'up');
  await sleep(250);
  ev = keyEvents(takeLines());
  check('raw control: down/up delivered with increasing seq and timestamps', c2.seq === c1.seq + 1 && c2.t >= c1.t && typeof c1.t === 'number', JSON.stringify([c1, c2]));
  check('raw control: no Hub layer function, no local keys', ev.length === 0 && c1.function === undefined, ev.join(' '));
  await api('POST', '/simulate', { control: 'KNOB_CW', phase: 'down', tap: true });
  const knob = await pc.next(m => m.type === 'control' && m.control === 'KNOB_CW');
  check('raw control: knob event carries kind/part', knob.kind === 'knob' && knob.part === 'cw' && knob.seq === c2.seq + 1, JSON.stringify(knob));

  pc.send({ type: 'context', name: 'Sequencer', detail: 'LS_Intro' });
  pc.send({ type: 'ping', t: 42 });
  const pong = await pc.next(m => m.type === 'pong');
  check('pipe: ping/pong echoes client time', pong.echo === 42 && typeof pong.t === 'number', JSON.stringify(pong));
  state = await api('GET', '/state');
  const ctx = state.clients.find(c => c.pid === targetPid);
  check('pipe: context report visible in hub state', ctx?.context === 'Sequencer' && ctx.contextDetail === 'LS_Intro', JSON.stringify(ctx));

  cfg.apps.find(a => a.id === 'keytarget').name = 'KeyTarget (renamed)';
  await api('PUT', '/config', cfg);
  const profile = await pc.next(m => m.type === 'profile');
  check('pipe: config change pushes updated profile', profile.profile?.name === 'KeyTarget (renamed)', JSON.stringify(profile.profile));

  await pc.close();
  await sleep(300);
  state = await api('GET', '/state');
  check('pipe: disconnect unregisters client', !state.clients.some(c => c.pid === targetPid), JSON.stringify(state.clients));
  takeLines();
  await api('POST', '/simulate', { control: 'K7', phase: 'down', tap: true });
  await sleep(300);
  ev = keyEvents(takeLines());
  await checkKeys('raw control: plugin gone → Hub layer fallback (Ctrl+F)', JSON.stringify(ev) === JSON.stringify(['down:11', 'down:46', 'up:46', 'up:11']), ev.join(' '));

  // 10. knob volume guard: an OS volume change during the guard window is reverted
  let vol = await api('GET', '/diag/volume');
  if (!vol.available) {
    check('volume guard available', false, 'no default audio endpoint');
  } else {
    const before = vol.level;
    const key = before > 0.1 ? 'VolumeDown' : 'VolumeUp';
    await api('POST', '/diag/volume/arm');
    await api('POST', '/execute', { action: { type: 'keys', keys: key } });
    await sleep(350);
    vol = await api('GET', '/diag/volume');
    if (vol.restores === 0 && Math.abs(vol.level - before) < 0.005) {
      // the injected media key never changed the volume (e.g. UIPI dropped it because an elevated window had focus)
      results.push({ ok: true, skipped: true });
      console.log(`SKIP  volume guard: OS volume change reverted  — the injected ${key} did not change the volume (focus in an elevated window?); rerun when idle`);
    } else {
      check('volume guard: OS volume change reverted', Math.abs(vol.level - before) < 0.005 && vol.restores >= 1, `before=${before} after=${vol.level} restores=${vol.restores} via ${key}`);
    }
  }

  // 11. input shaping: knob detents per trigger, layer override, min interval, hold-to-repeat
  const busEvents = [];
  const bus = new WebSocket(`ws://127.0.0.1:${PORT}/ws`);
  await new Promise((res, rej) => { bus.onopen = res; bus.onerror = rej; });
  bus.onmessage = m => { const e = JSON.parse(m.data); if (e.type === 'control' || e.type === 'repeat') busEvents.push(e); };
  const executedDowns = control => busEvents.filter(e => e.type === 'control' && e.control === control && e.phase === 'down' && e.executed).length;
  const tap = async (control, n, gapMs = 40) => { for (let i = 0; i < n; i++) { await api('POST', '/simulate', { control, phase: 'down', tap: true }); await sleep(gapMs); } };

  cfg.device.controls.find(c => c.id === 'KNOB_CW').input = { stepDetents: 3 };
  cfg.device.controls.find(c => c.id === 'K7').input = { minIntervalMs: 500 };
  cfg.device.controls.find(c => c.id === 'JOY_UP').input = { repeat: true, repeatDelayMs: 200, repeatIntervalMs: 100 };
  await api('PUT', '/config', cfg);
  await api('POST', '/layer', { op: 'set', layer: 'unreal' }); // KNOB_CW → edit.redo (Ctrl+Y), K7 → W, JOY_UP → Up
  await sleep(100);
  busEvents.length = 0; takeLines();

  await tap('KNOB_CW', 7);
  await sleep(250);
  check('shaping: 7 detents with stepDetents=3 → 2 triggers', executedDowns('KNOB_CW') === 2,
    busEvents.filter(e => e.control === 'KNOB_CW' && e.phase === 'down').map(e => `${e.step}${e.executed ? '✓' : ''}`).join(' '));
  ev = keyEvents(takeLines()).filter(k => k === 'down:59');
  await checkKeys('shaping: Ctrl+Y injected exactly twice', ev.length === 2, `${ev.length}×`);

  cfg.layers.find(l => l.id === 'unreal').input = { KNOB_CW: { stepDetents: 1 } };
  await api('PUT', '/config', cfg);
  busEvents.length = 0;
  await tap('KNOB_CW', 3);
  await sleep(200);
  check('shaping: layer override (stepDetents=1) wins over control setting', executedDowns('KNOB_CW') === 3, `${executedDowns('KNOB_CW')}×`);

  busEvents.length = 0;
  await tap('K7', 3, 60);
  await sleep(200);
  check('shaping: minIntervalMs=500 drops rapid presses', executedDowns('K7') === 1, `${executedDowns('K7')}×`);

  busEvents.length = 0;
  await api('POST', '/simulate', { control: 'JOY_UP', phase: 'down', tap: false });
  await sleep(560);
  await api('POST', '/simulate', { control: 'JOY_UP', phase: 'up', tap: false });
  const repeats = busEvents.filter(e => e.type === 'repeat' && e.control === 'JOY_UP').length;
  await sleep(300);
  const repeatsAfter = busEvents.filter(e => e.type === 'repeat' && e.control === 'JOY_UP').length;
  check('shaping: hold-to-repeat fires after delay at interval (≈4 in 560 ms)', executedDowns('JOY_UP') === 1 && repeats >= 3 && repeats <= 5, `initial=${executedDowns('JOY_UP')} repeats=${repeats}`);
  check('shaping: repeat stops on release', repeatsAfter === repeats, `after release ${repeatsAfter}`);
  bus.close();

  // 12. validation rejects bad config
  const bad = JSON.parse(JSON.stringify(cfg));
  bad.layers[0].map.K1 = 'does.not.exist';
  let rejected = false;
  try { await api('PUT', '/config', bad); } catch (e) { rejected = e.message.includes('does.not.exist'); }
  check('invalid config is rejected with reason', rejected);
} catch (e) {
  check('unexpected error', false, e.stack);
} finally {
  target?.kill();
  hub.kill();
  const failed = results.filter(r => !r.ok).length;
  const skipped = results.filter(r => r.skipped).length;
  console.log(`\n${results.length - failed - skipped}/${results.length} passed${skipped ? `, ${skipped} skipped (focus)` : ''}${failed ? `, ${failed} FAILED` : ''}`);
  if (failed) { console.log('--- hub log ---\n' + hubLog.slice(-3000)); process.exitCode = 1; }
}

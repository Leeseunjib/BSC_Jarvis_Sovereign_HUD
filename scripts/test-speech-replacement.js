'use strict';
const assert = require('assert/strict');
const fs = require('fs');
const vm = require('vm');
const path = require('path');

async function scenario(finalize, lockAvailable = true, replacement = false) {
  let now = 0, id = 0, domReady;
  let duplicateClosed = false, orbClick;
  const timers = new Map(), effects = [], requests = [], instances = [];
  const schedule = (fn, ms = 0, repeat = 0) => { const key = ++id; timers.set(key, { fn, at: now + ms, repeat }); return key; };
  const run = async until => {
    for (;;) {
      const due = [...timers.entries()].filter(([, t]) => t.at <= until).sort((a, b) => a[1].at - b[1].at)[0];
      if (!due) break;
      const [key, t] = due; now = t.at; timers.delete(key);
      if (t.repeat) timers.set(key, { ...t, at: now + t.repeat });
      t.fn(); await Promise.resolve(); await Promise.resolve();
    }
    now = until;
  };
  class FakeRecognition {
    constructor() { instances.push(this); this.stops = 0; }
    start() { if (this.onstart) this.onstart(); }
    stop() {
      this.stops++;
      if (finalize) schedule(() => this.emit('자비스 지금 시간을 알려줘', true), 10);
      schedule(() => { if (this.onend) this.onend(); }, 20);
    }
    abort() { schedule(() => { if (this.onend) this.onend(); }, 1); }
    emit(text, isFinal) { const row = [{ transcript: text }]; row.isFinal = isFinal; this.onresult({ resultIndex: 0, results: [row] }); }
  }
  const context = {
    console, Date: class extends Date { static now() { return now; } },
    setTimeout: (fn, ms) => schedule(fn, ms), clearTimeout: key => timers.delete(key),
    setInterval: (fn, ms) => schedule(fn, ms, ms), clearInterval: key => timers.delete(key),
    requestAnimationFrame: fn => schedule(fn, 16),
    fetch: async (url, options = {}) => {
      const body = options.body ? JSON.parse(options.body) : null; requests.push({ url, body });
      return { json: async () => url === '/api/butler/state' ? { ok: true, config: { assistantName: '자비스' } } : url === '/api/butler/command' ? { ok: true, result: { intent: 'clock' } } : { ok: true, isSpeaking: false } };
    },
    navigator: { locks: { request: (name, options, callback) => callback(lockAvailable ? {} : null) }, mediaDevices: { getUserMedia: async () => ({ getAudioTracks: () => [{ label: 'test' }], getTracks: () => [] }) } },
    document: { getElementById: () => ({}) }
  };
  context.window = {
    location: { search: '?worker=1' }, webkitSpeechRecognition: FakeRecognition,
    close: () => { duplicateClosed = true; },
    AudioContext: class { createMediaStreamSource() { return { connect() {} }; } createAnalyser() { return { fftSize: 256, getFloatTimeDomainData: data => data.fill(0) }; } },
    addEventListener: (name, fn) => { if (name === 'DOMContentLoaded') domReady = fn; },
    React: { createElement: (type, props) => { if (typeof type === 'function' && type.name === 'JarvisOrbView') orbClick = props.onClick; return { type, props }; }, useState: value => [value, () => {}], useRef: value => ({ current: value }), useCallback: fn => fn, useEffect: fn => effects.push(fn) },
    ReactDOM: { createRoot: () => ({ render: element => element.type() }) }
  };
  vm.createContext(context);
  for (const file of ['speech-turn-buffer.js', 'media-intents.js', 'jarvis-react-hud.js']) vm.runInContext(fs.readFileSync(path.join(process.env.JARVIS_TEST_JS_ROOT || path.join(__dirname, '../public/js'), file), 'utf8'), context, { filename: file });
  domReady(); for (const effect of effects) effect(); await Promise.resolve(); await Promise.resolve();
  if (!lockAvailable) { assert(duplicateClosed); assert.equal(instances.length, 0); assert.equal(requests.length, 0); return; }
  const rec = instances[0]; assert(rec);
  if (replacement) {
    assert(orbClick); rec.emit('자비스 이전 문장', false); orbClick();
    instances[1].emit('자비스 새 명령', true); await run(2500);
    const sent = requests.filter(r => r.url === '/api/butler/command');
    assert.equal(sent.length, 1, '교체된 인식기의 늦은 결과는 실행 금지');
    assert.equal(sent[0].body.sentence, '자비스 새 명령', '이전 미완성 문장은 취소되어야 합니다');
    return;
  }
  rec.emit('자비스 지금 시간을 알려줘', false);
  await run(1900); assert.equal(requests.filter(r => r.url === '/api/butler/command').length, 0);
  await run(5500);
  const commands = requests.filter(r => r.url === '/api/butler/command');
  assert.equal(rec.stops, 1, '침묵 후 확정을 한 번 요청해야 합니다');
  if (finalize) {
    assert.equal(commands.length, 1); assert.equal(commands[0].body.sentence, '자비스 지금 시간을 알려줘');
  } else {
    assert.equal(commands.length, 0);
    assert(requests.some(r => r.url === '/api/ear/push' && r.body.status === 'recognition-error'));
  }
}
(async () => { await scenario(true); await scenario(false); await scenario(true, false); await scenario(true, true, true); console.log('PASS: 음성 확정·실패·중복 작업자 및 교체된 인식기의 늦은 결과 차단'); })().catch(err => { console.error(err); process.exitCode = 1; });

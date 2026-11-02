'use strict';
const assert = require('assert/strict');
const fs = require('fs');
const vm = require('vm');
const path = require('path');

async function scenario(finalize, lockAvailable = true, longSpeech = false, hiddenLabels = false) {
  let now = 0, id = 0, domReady;
  let liveAudio = 0, microphoneRequests = 0;
  const audioTrack={kind:'audio',readyState:'live',label:'TASCAM test'};
  let requestedDevice;
  let enumerations=0,probeStops=0;
  let duplicateClosed = false;
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
    start(track) { assert.equal(track,audioTrack,'음량 측정과 인식이 동일한 마이크 트랙을 사용해야 합니다'); if (this.onstart) this.onstart(); }
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
      return { json: async () => url === '/api/butler/state' ? { ok: true, config: { assistantName: '자비스',microphoneDeviceLabel:'TASCAM test' } } : url === '/api/butler/command' ? { ok: true, result: { intent: 'clock' } } : { ok: true, isSpeaking: false } };
    },
    navigator: { locks: { request: (name, options, callback) => callback(lockAvailable ? {} : null) }, mediaDevices: { enumerateDevices: async()=>[{kind:'audioinput',label:hiddenLabels&&enumerations++===0?'':'TASCAM test',deviceId:'tascam-direct'}],getUserMedia: async options => { microphoneRequests++;requestedDevice=options.audio.deviceId; return { getAudioTracks: () => [audioTrack], getTracks: () => options.audio===true?[{stop:()=>probeStops++}]:[] }; } } },
    document: { getElementById: () => ({}) }
  };
  context.window = {
    location: { search: '?worker=1' }, webkitSpeechRecognition: FakeRecognition,
    close: () => { duplicateClosed = true; },
    AudioContext: class { createMediaStreamSource() { return { connect() {} }; } createAnalyser() { return { fftSize: 256, getFloatTimeDomainData: data => data.fill(liveAudio) }; } },
    addEventListener: (name, fn) => { if (name === 'DOMContentLoaded') domReady = fn; },
    React: { createElement: (type, props) => ({ type, props }), useState: value => [value, () => {}], useRef: value => ({ current: value }), useCallback: fn => fn, useEffect: fn => effects.push(fn) },
    ReactDOM: { createRoot: () => ({ render: element => element.type() }) }
  };
  vm.createContext(context);
  for (const file of ['speech-turn-buffer.js', 'media-intents.js', 'jarvis-react-hud.js']) vm.runInContext(fs.readFileSync(path.join(__dirname, '../public/js', file), 'utf8'), context, { filename: file });
  domReady(); for (const effect of effects) effect(); for(let i=0;i<40;i++)await Promise.resolve();
  if (!lockAvailable) { assert(duplicateClosed); assert.equal(instances.length, 0); assert.equal(requests.length, 0); return; }
  const rec = instances[0]; assert(rec);
  assert.equal(microphoneRequests,hiddenLabels?2:1,'임시 권한 확인 외 중복 마이크 스트림을 만들면 안 됩니다');
  assert.equal(probeStops,hiddenLabels?1:0,'권한 확인용 임시 스트림은 반드시 닫아야 합니다');
  assert.equal(requestedDevice.exact,'tascam-direct');
  if(longSpeech){
    rec.onspeechstart();liveAudio=0.03;
    rec.emit('자비스 설명을 끝까지 듣고',false);
    await run(45000);
    assert.equal(rec.stops,0,'실제 클라이언트의 발화 활동이 45초간 강제 확정을 막아야 합니다');
    assert.equal(requests.filter(r=>r.url==='/api/butler/command').length,0);
    liveAudio=0;rec.onspeechend();
    rec.emit('자비스 설명을 끝까지 듣고 마지막으로 지금 시간을 알려줘',true);
    await run(47000);
    const sent=requests.filter(r=>r.url==='/api/butler/command');
    assert.equal(sent.length,1);assert.equal(sent[0].body.sentence,'자비스 설명을 끝까지 듣고 마지막으로 지금 시간을 알려줘');
    return;
  }
  rec.emit('자비스 지금 시간을 알려줘', false);
  await run(1900); assert.equal(requests.filter(r => r.url === '/api/butler/command').length, 0);
  if (finalize) {
    await run(2300);
    assert.equal(requests.filter(r => r.url === '/api/butler/command').length, 1, '동일 문장의 최종 확정 뒤 침묵 대기를 반복하면 안 됩니다');
  }
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
(async () => { await scenario(true); await scenario(false); await scenario(true, false); await scenario(true,true,true); await scenario(true,true,false,true); console.log('PASS: 실제 음성 클라이언트 확정 요청·실패 안내·중복 마이크 방지·장치 권한 초기화·동일 입력 트랙·45초 발화 유지'); })().catch(err => { console.error(err); process.exitCode = 1; });

'use strict';
const assert = require('assert/strict');
const fs = require('fs');
const vm = require('vm');
const path = require('path');
const root = process.env.JARVIS_TEST_JS_ROOT || path.resolve(__dirname, '../public/js') + path.sep;
const source = fs.readFileSync(root + 'jarvis-app.js', 'utf8');
const fn = source.slice(source.indexOf('  function startBrowserSpeechRecognition('), source.indexOf('  function applyHudPosition('));
assert(fn.startsWith('  function startBrowserSpeechRecognition('));
function fixture() {
  let now = 0, next = 0, rec;
  const timers = new Map(), commands = [];
  const setTimeout = (f, delay = 0) => { const id = ++next; timers.set(id, { f, at: now + delay }); return id; };
  const clearTimeout = id => timers.delete(id);
  const advance = to => { for (;;) { const due = [...timers].filter(([, v]) => v.at <= to).sort((a,b) => a[1].at - b[1].at)[0]; if (!due) break; now = due[1].at; timers.delete(due[0]); due[1].f(); } now = to; };
  class Recognition {
    constructor() { rec = this; }
    start() { this.onstart(); }
    stop() {} abort() {}
    emit(rows, resultIndex = 0) { this.onresult({ resultIndex, results: rows.map(([text, final]) => { const r = [{transcript: text}]; r.isFinal = final; return r; }) }); }
  }
  const state = { listening: false, recognition: null, speaking: false, alwaysOn: false, awakeUntil: 0, config: { wakeWords: ['자비스'], assistantName: '자비스' } };
  const context = { Date: class extends Date { static now() { return now; } }, setTimeout, clearTimeout, appState: state, speechTurn: null,
    startMicLevelVisualizer() {}, triggerWindowsNativeMic() {}, updateHud() {}, sendCommand: (text, mode) => commands.push({text, mode}),
    fetch: () => Promise.resolve({}), document: { getElementById: () => ({}) },
    window: { webkitSpeechRecognition: Recognition, JarvisMediaIntents: { classifyMediaControl: () => null } } };
  vm.createContext(context);
  vm.runInContext(fs.readFileSync(root + 'speech-turn-buffer.js', 'utf8'), context);
  vm.runInContext(fn, context);
  return { start: () => context.startBrowserSpeechRecognition(true), emit: (...args) => rec.emit(...args), current: () => rec, advance, commands };
}
const continuing = fixture(); continuing.start();
continuing.emit([['자비스 유튜브를', true]]); continuing.advance(1000);
continuing.emit([['자비스 유튜브를', true], ['열어서', false]], 1); continuing.advance(2500);
assert.equal(continuing.commands.length, 0, '중간 쉼과 미확정 결과에서 실행 금지');
continuing.emit([['자비스 유튜브를', true], ['열어서 음악 틀어', true]], 1); continuing.advance(4499);
assert.equal(continuing.commands.length, 0); continuing.advance(4500);
assert.equal(continuing.commands.length, 1); assert.equal(continuing.commands[0].text, '자비스 유튜브를 열어서 음악 틀어');
continuing.emit([['자비스 유튜브를', true], ['열어서 음악 틀어', true]], 0); continuing.advance(8000);
assert.equal(continuing.commands.length, 1, '중복 이벤트는 재실행 금지');
const cancelled = fixture(); cancelled.start(); cancelled.emit([['자비스 창 열어', false]]); cancelled.advance(500);
cancelled.start(); // User toggles listening off; the recognizer may still send a final result.
cancelled.emit([['자비스 창 열어', true]]); cancelled.advance(4000);
assert.equal(cancelled.commands.length, 0, '중지 이후 도착한 결과를 실행하면 안 됨');
const restarted = fixture(); restarted.start(); const oldRec = restarted.current();
restarted.start(); restarted.start(); restarted.emit([['자비스 시간 알려줘', false]]);
oldRec.onend(); restarted.emit([['자비스 시간 알려줘', true]]); restarted.advance(2100);
assert.equal(restarted.commands.length, 1, '이전 인식기의 종료가 새 인식 세션을 취소하면 안 됨');
console.log('PASS: 실제 브라우저 클라이언트 중간 쉼·이어 말하기·부분/최종·중복·취소');

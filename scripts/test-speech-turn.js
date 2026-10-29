'use strict';
const assert = require('assert/strict');
const { SpeechTurnBuffer } = require('../public/js/speech-turn-buffer');
let now = 0, muted = false;
const sent = [], preview = [];
const buffer = new SpeechTurnBuffer({ now: () => now, schedule: () => 1, cancel: () => {}, isMuted: () => muted, onCommit: s => sent.push(s), onPreview: s => preview.push(s) });
buffer.push([{ id: '1:0', text: '자비스 유튜브를' }]);
now = 1500; buffer.tick(); assert.equal(sent.length, 0);
buffer.push([], '열어서');
now = 5000; buffer.tick(); assert.equal(sent.length, 0, '미확정 중간 결과를 실행하면 안 됩니다');
buffer.push([{ id: '1:1', text: '열어서 클래식 음악 틀어 줘' }]);
now = 6500; buffer.audio(0.3);
now = 7100; buffer.tick(); assert.equal(sent.length, 0, '발화가 계속되면 실행하면 안 됩니다');
now = 8500; buffer.tick(); assert.deepEqual(sent, ['자비스 유튜브를 열어서 클래식 음악 틀어 줘']);
buffer.push([{ id: '1:1', text: '열어서 클래식 음악 틀어 줘' }]);
now = 11000; buffer.tick(); assert.equal(sent.length, 1, '같은 결과를 다시 실행하면 안 됩니다');
buffer.push([{ id: '2:0', text: '자비스 몇 시야' }]);
now = 14000; muted = true; buffer.tick(); assert.equal(sent.length, 1);
muted = false; buffer.tick(); assert.equal(sent[1], '자비스 몇 시야');
buffer.push([{ id: '3:0', text: '자비스' }]); buffer.stop(); now = 18000; buffer.tick(); assert.equal(sent.length, 2);
assert(preview.includes('자비스 유튜브를 열어서'));

function setup() {
  let time = 0, finalizations = 0, resets = 0;
  const commits = [], failures = [];
  const turn = new SpeechTurnBuffer({ now: () => time, schedule: () => 1, cancel: () => {}, onCommit: s => commits.push(s), onFinalize: () => finalizations++, onFailure: s => failures.push(s), onReset: () => resets++ });
  return { turn, commits, failures, at: n => { time = n; }, get finalizations() { return finalizations; }, get resets() { return resets; } };
}
const ending = setup();
ending.turn.push([], '자비스 카톡 시험이라고 보내');
ending.at(2000); ending.turn.tick(); assert.equal(ending.finalizations, 1); assert.equal(ending.commits.length, 0);
ending.at(2200); ending.turn.push([{ id: '1:0', text: '자비스 카톡 시험이라고 보내' }]);
ending.at(4200); ending.turn.tick(); assert.deepEqual(ending.commits, ['자비스 카톡 시험이라고 보내']);
const hanging = setup();
hanging.turn.push([{ id: '1:0', text: '자비스' }], '카톡 시험이라고 보내');
hanging.at(2000); hanging.turn.tick(); hanging.at(5000); hanging.turn.tick();
assert.equal(hanging.finalizations, 1); assert.equal(hanging.resets, 1); assert.equal(hanging.commits.length, 0);
assert.deepEqual(hanging.failures, ['자비스 카톡 시험이라고 보내']);
assert.deepEqual(hanging.turn.state(), { finalSegments: 0, interimChars: 0, finalizing: false });
const ended = setup(); ended.turn.push([], '자비스 확정되지 않은 명령'); ended.turn.ended();
assert.equal(ended.commits.length, 0); assert.equal(ended.failures.length, 1);
const noisy = setup(); noisy.turn.push([{ id: '1:0', text: '자비스 몇 시야' }]);
for (let t = 1000; t <= 10000; t += 1000) { noisy.at(t); noisy.turn.audio(0.3); noisy.turn.tick(); }
assert.equal(noisy.failures.length, 1); assert.equal(noisy.commits.length, 0);
console.log('PASS: 문장 합치기·발화 중 보류·확정 요청·미확정 시간 초과·소음·재시작·중복·중지 처리');

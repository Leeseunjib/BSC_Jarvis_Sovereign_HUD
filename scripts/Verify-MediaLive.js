'use strict';
// Explicit live check: opens a music video, pauses/resumes it, then leaves it paused.
const assert = require('assert/strict');
const fs = require('fs');
const path = require('path');
const { controlMedia } = require('./media-control');
if (!process.argv.includes('--run')) throw new Error('Use --run only when a live music playback check is intended.');
const base = 'http://127.0.0.1:7788';
const proof = { startedAt: new Date().toISOString(), steps: [] };
async function request(route, body) {
  const response = await fetch(base + route, {
    method: body ? 'POST' : 'GET',
    headers: body ? { 'Content-Type': 'application/json' } : {},
    body: body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(55000)
  });
  const json = await response.json();
  if (!response.ok || !json.ok) throw new Error(json.detail || json.error || 'Jarvis request failed.');
  return json;
}
async function command(sentence) {
  // Fresh session proves stop does not depend on a previous wake-word window.
  const reply = await request('/api/butler/command', { sentence, source: 'voice', voiceSession: 'media-live-' + Date.now(), executeOs: true });
  assert.equal(reply.ignored, undefined, 'Command was incorrectly ignored.');
  return reply.result;
}
async function expectPlayback(status) {
  let state;
  for (let attempt = 0; attempt < 8; attempt++) {
    state = await controlMedia({ action: 'status' });
    const sessions = state.sessions.filter(session => session.browser);
    if (sessions.length === 1 && sessions[0].status === status) return sessions[0];
    await new Promise(resolve => setTimeout(resolve, 700));
  }
  throw new Error('Windows playback did not reach ' + status + ': ' + JSON.stringify(state));
}
(async () => {
  const before = await request('/api/status');
  assert.equal(before.runtime.entry, 'server.verified.js');
  proof.runtime = before.runtime;
  const opened = await command('자비스 클래식 음악 틀어줘');
  assert.equal(opened.intent, 'youtubeAutoPlay');
  proof.steps.push({ action: 'open', intent: opened.intent, playback: await expectPlayback('Playing') });
  console.log('PASS: 실제 앱 명령으로 유튜브 창 열기 → Windows Playing');
  const stopped = await command('음악 꺼줘');
  assert.equal(stopped.intent, 'stopMusic');
  assert.equal(stopped.media.verified, true);
  assert.equal(stopped.media.state, 'Paused');
  proof.steps.push({ action: 'explicit-stop-without-wake', media: stopped.media, playback: await expectPlayback('Paused') });
  console.log('PASS: 새 음성 세션의 음악 꺼줘 → Windows Paused');
  const repeated = await command('음악 꺼줘');
  assert.equal(repeated.media.changed, false);
  proof.steps.push({ action: 'repeated-stop', media: repeated.media, playback: await expectPlayback('Paused') });
  console.log('PASS: 정지 명령 반복 → 재생이 다시 켜지지 않음');
  const resumed = await command('자비스 음악 다시 틀어줘');
  assert.equal(resumed.intent, 'resumeMusic');
  assert.equal(resumed.media.verified, true);
  proof.steps.push({ action: 'resume', media: resumed.media, playback: await expectPlayback('Playing') });
  const implicit = await command('꺼줘');
  assert.equal(implicit.intent, 'stopMusic');
  assert.equal(implicit.media.verified, true);
  proof.steps.push({ action: 'context-stop-without-wake', media: implicit.media, playback: await expectPlayback('Paused') });
  console.log('PASS: 다시 재생 → 새 음성 세션의 꺼줘 → Windows Paused');
  proof.passed = true;
})().catch(error => { proof.passed = false; proof.error = error.message; console.error(error.message); process.exitCode = 1; }).finally(() => {
  proof.finishedAt = new Date().toISOString();
  fs.writeFileSync(path.join(__dirname, '..', 'data', 'media-live-verification.json'), JSON.stringify(proof, null, 2));
});

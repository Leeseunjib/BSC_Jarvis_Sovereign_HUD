'use strict';

// Integration checks use only an isolated data and startup directory.
const fs = require('fs');
const os = require('os');
const path = require('path');
const assert = require('assert/strict');
const http = require('http');
const { spawn } = require('child_process');
const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'jarvis-safety-'));
process.env.JARVIS_DATA_DIR = path.join(temp, 'data');
process.env.JARVIS_STARTUP_DIR = path.join(temp, 'startup');
process.env.JARVIS_DISABLE_EXTERNAL_HUB = '1';
process.env.JARVIS_DISABLE_NEURAL_TTS = '1';
process.env.JARVIS_PORT = String(30000 + Math.floor(Math.random() * 20000));
fs.mkdirSync(process.env.JARVIS_STARTUP_DIR);
const verified = process.env.JARVIS_TEST_VERIFIED_RUNTIME === '1';
const { server, butler, PORT } = require(verified ? '../server.verified' : '../server');
const { WindowsHands, FILES } = require(verified ? './butler-engine.verified' : './butler-engine');
const { registerWindowsStartup, getStartupStatus } = require('./windows-ear-daemon');
const { VoiceSessions } = require('./voice-session');

function request(method, url, body, headers = {}) {
  return new Promise((resolve, reject) => {
    const req = http.request({ host: '127.0.0.1', port: PORT, path: url, method, headers }, res => {
      let data = '';
      res.on('data', chunk => { data += chunk; });
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: data ? JSON.parse(data) : {} }));
    });
    req.on('error', reject);
    if (body) req.write(body);
    req.end();
  });
}

function audioRequest(url) {
  return new Promise((resolve, reject) => {
    http.get({ host: '127.0.0.1', port: PORT, path: url }, res => {
      let bytes = 0;
      res.on('data', chunk => { bytes += chunk.length; });
      res.on('end', () => resolve({ status: res.statusCode, type: res.headers['content-type'], bytes }));
    }).on('error', reject);
  });
}

(async () => {
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(PORT, '127.0.0.1', resolve); });
  try {
    assert.equal((await request('GET', '/api/status')).status, 200);
    assert.equal((await request('GET', '/api/status', null, { Origin: 'https://example.com' })).status, 403);
    assert.equal((await request('POST', '/api/butler/command', '{"sentence":"몇 시야"}', { Origin: 'https://example.com', 'Content-Type': 'application/json' })).status, 403);
    assert.equal((await request('POST', '/api/config', '{}', { 'Content-Type': 'text/plain' })).status, 415);
    assert.equal((await request('GET', '/api/status', null, { Host: `attacker.example:${PORT}` })).status, 403);
    await request('POST', '/api/ear/health', JSON.stringify({ microphone: 'ready', recognition: 'listening' }), { 'Content-Type': 'application/json' });
    assert.equal((await request('GET', '/api/ear/latest')).body.earHealth.microphone, 'ready');
    assert.equal((await request('GET', '/api/ear/health')).body.connected, true);
    await request('POST', '/api/ear/push', JSON.stringify({ text: '자비스 시험', status: 'recognition-error' }), { 'Content-Type': 'application/json' });
    assert.equal((await request('GET', '/api/ear/latest')).body.latest.intent, 'speechError');
    const good = await request('POST', '/api/butler/command', JSON.stringify({ sentence: '몇 시야', executeOs: false }), { Origin: `http://127.0.0.1:${PORT}`, 'Content-Type': 'application/json' });
    assert.equal(good.body.result.intent, 'clock');

    const voiceRequest = sentence => request('POST', '/api/butler/command', JSON.stringify({ sentence, source: 'voice', voiceSession: 'integration-test', executeOs: false }), { 'Content-Type': 'application/json' });
    assert.equal((await voiceRequest('몇 시야')).body.ignored, true);
    assert.equal((await voiceRequest('자비스')).body.result.intent, 'awake');
    assert.equal((await voiceRequest('몇 시야')).body.result.intent, 'clock');
    let now = 1000;
    const sessions = new VoiceSessions(butler, () => now);
    assert.equal(sessions.accept('one', '몇 시야'), false);
    assert.equal(sessions.accept('one', '자비스'), true);
    assert.equal(sessions.accept('one', '몇 시야'), true);
    assert.equal(sessions.accept('two', '몇 시야'), false);
    now += 31000;
    assert.equal(sessions.accept('one', '몇 시야'), false);

    const duplicate = spawn(process.execPath, [path.resolve(__dirname, '..', verified ? 'server.verified.js' : 'server.js')], { env: process.env, stdio: 'ignore', windowsHide: true });
    const exitCode = await new Promise((resolve, reject) => { duplicate.on('error', reject); duplicate.on('exit', resolve); });
    assert.equal(exitCode, 1);
    assert.equal((await request('GET', '/api/status')).status, 200);

    WindowsHands.typeText = () => { throw new Error('Unexpected direct keyboard input'); };
    const actions = [];
    let mockTarget = { process: 'KakaoTalk', title: '영웅', editable: true, value: '', selection: '', hwnd: 1, processId: 1, runtimeId: [1] };
    butler.interact = async request => {
      actions.push(request);
      if (request.op === 'capture') return { target: { ...mockTarget } };
      if (request.op === 'append') return { ok: true, value: String(request.target.value || '') + request.text };
      return { ok: true };
    };
    butler.rewriteLocal = async () => '정중한 시험 답장입니다.';
    const interaction = butler.interact;
    butler.interact = async () => { throw new Error('시험 입력창 확인 실패'); };
    const failedCommand = await request('POST', '/api/butler/command', JSON.stringify({ sentence: '받아쓰기', executeOs: true }), { 'Content-Type': 'application/json' });
    assert.equal(failedCommand.status, 500);
    const failedHud = (await request('GET', '/api/ear/latest')).body.latest;
    assert.equal(failedHud.isFinal, true);
    assert.equal(failedHud.intent, 'error');
    assert(failedHud.hudSubtitle.includes('시험 입력창 확인 실패'));
    butler.interact = interaction;
    butler.saveConfig({ freeMode: true, previewBeforeSend: false });
    const preview = await butler.execute('카톡 영웅님한테 오늘도 힘내세요라고 답장해', true);
    assert.equal(preview.awaitingConfirm, true);
    assert.equal(preview.pendingSend.to, '영웅');
    assert.equal((await butler.execute('응', true)).intent, 'confirmSend');
    assert.equal(actions.filter(a => a.op === 'kakao' && a.send).length, 1);
    assert.equal((await butler.execute('카톡 영웅님한테 안녕이라고 답장해', true)).awaitingConfirm, true);
    assert.equal((await butler.execute('아니', true)).intent, 'confirmSend');
    assert.equal((await butler.execute('자비스 아빠 왔어', false)).intent, 'greet');
    assert.equal((await butler.execute('창 다 꺼', false)).hudColor, 'amber');
    assert.equal((await butler.execute('존댓말로 바꿔', false)).hudColor, 'amber');
    mockTarget = { process: 'notepad', title: '시험', editable: true, value: '', selection: '', hwnd: 1, processId: 1, runtimeId: [1] };
    assert.equal((await butler.execute('받아쓰기', true)).intent, 'dictation');
    assert.equal((await butler.execute('안녕하세요', true)).intent, 'dictationText');
    assert.equal(actions.at(-1).op, 'append');
    assert.equal((await butler.execute('그만', true)).intent, 'stopDictation');
    mockTarget.selection = '원래 문장';
    assert.equal((await butler.execute('존댓말로 바꿔', true)).intent, 'rewrite');
    assert.equal((await butler.execute('응', true)).intent, 'confirmSend');
    assert.equal(actions.at(-1).op, 'replace');
    mockTarget = { process: 'chrome', title: 'Gmail', editable: true, value: '', selection: '메일 시험 내용', hwnd: 1, processId: 1, runtimeId: [1] };
    assert.equal((await butler.execute('선택한 메일 읽어', true)).intent, 'readSelectedMail');
    assert.equal((await butler.execute('선택한 메일 답장 써줘', true)).intent, 'draftMailReply');
    assert.equal((await butler.execute('메일 답장 입력해', true)).intent, 'insertMailReply');
    assert.equal(actions.at(-1).op, 'append');
    const coding = await butler.execute('코덱스한테 테스트 파일 만들어', false);
    assert.equal(coding.awaitingConfirm, true);
    assert.equal((await butler.execute('아니', false)).intent, 'confirmSend');

    const start = await request('POST', '/api/butler/command', JSON.stringify({ sentence: '시험 1초 타이머', executeOs: false }), { 'Content-Type': 'application/json' });
    assert.equal(start.body.result.intent, 'timer');
    await new Promise(resolve => setTimeout(resolve, 2200));
    const latest = await request('GET', '/api/ear/latest');
    assert.match(latest.body.latest.spoken, /시험.*끝났습니다/);
    assert.deepEqual(JSON.parse(fs.readFileSync(FILES.timers, 'utf8')), []);

    assert.equal(registerWindowsStartup(true).enabled, true);
    assert.equal(getStartupStatus().registered, true);
    assert.equal(registerWindowsStartup(false).enabled, false);
    assert.equal(getStartupStatus().registered, false);
    if (process.env.JARVIS_TEST_SAPI === '1') {
      const audio = await audioRequest('/api/tts-mp3?text=Testing%20one%20two%20three');
      assert.equal(audio.status, 200);
      assert.equal(audio.type, 'audio/wav');
      assert.ok(audio.bytes > 512);
      console.log(`PASS: Windows speech fallback generated ${audio.bytes} bytes of English audio`);
    }
    console.log('PASS: 접근 제한, 호출어·이어 듣기, 포트 충돌, 카카오 확인, 받아쓰기, 글 수정, 메일 초안, 코딩 확인, 타이머와 자동 시작');
  } finally {
    await new Promise(resolve => server.close(resolve));
    const resolvedTemp = fs.realpathSync(temp);
    const resolvedParent = fs.realpathSync(os.tmpdir());
    if (path.dirname(resolvedTemp).toLowerCase() !== resolvedParent.toLowerCase() || !path.basename(resolvedTemp).startsWith('jarvis-safety-')) {
      throw new Error('Refusing to remove unexpected test directory');
    }
    fs.rmSync(resolvedTemp, { recursive: true, force: true });
  }
})().catch(err => { console.error(err); server.close(); process.exitCode = 1; });

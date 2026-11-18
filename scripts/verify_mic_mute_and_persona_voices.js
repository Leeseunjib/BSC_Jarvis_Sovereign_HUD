'use strict';

const assert = require('assert');
const http = require('http');
const { PERSONA_VOICE_MAP, resolvePersonaVoice, splitMultiPersonaSegments } = require('./tts-audio');

function requestJson(method, path, body = null) {
  return new Promise((resolve, reject) => {
    const payload = body ? Buffer.from(JSON.stringify(body), 'utf8') : null;
    const req = http.request(
      {
        hostname: '127.0.0.1',
        port: 7788,
        path,
        method,
        headers: payload
          ? { 'Content-Type': 'application/json', 'Content-Length': payload.length }
          : {}
      },
      res => {
        const chunks = [];
        res.on('data', c => chunks.push(c));
        res.on('end', () => {
          const raw = Buffer.concat(chunks).toString('utf8');
          try {
            resolve({ status: res.statusCode, headers: res.headers, json: JSON.parse(raw) });
          } catch (e) {
            resolve({ status: res.statusCode, headers: res.headers, raw });
          }
        });
      }
    );
    req.on('error', reject);
    if (payload) req.write(payload);
    req.end();
  });
}

function requestBinary(path) {
  return new Promise((resolve, reject) => {
    const req = http.get({ hostname: '127.0.0.1', port: 7788, path }, res => {
      const chunks = [];
      res.on('data', c => chunks.push(c));
      res.on('end', () => {
        resolve({ status: res.statusCode, headers: res.headers, buffer: Buffer.concat(chunks) });
      });
    });
    req.on('error', reject);
  });
}

async function main() {
  const startedAt = Date.now();
  // 1. Verify 30 BSC Personas in PERSONA_VOICE_MAP
  const bscPersonas = Object.values(PERSONA_VOICE_MAP).filter(p => p.id !== 'jarvis');
  const females = bscPersonas.filter(p => p.gender === 'female');
  const males = bscPersonas.filter(p => p.gender === 'male');
  assert.strictEqual(bscPersonas.length, 30, 'Expected 30 BSC agents in PERSONA_VOICE_MAP');
  assert.strictEqual(females.length, 15, 'Expected 15 female BSC agents');
  assert.strictEqual(males.length, 15, 'Expected 15 male BSC agents');

  const soha = resolvePersonaVoice('소하');
  const taejun = resolvePersonaVoice('태준');
  assert.ok(soha && soha.gender === 'female' && soha.voice === 'ko-KR-SunHiNeural');
  assert.ok(taejun && taejun.gender === 'male' && taejun.voice === 'ko-KR-InJoonNeural');

  const multiSegs = splitMultiPersonaSegments('소하: 안녕하세요 대표님, 수석비서 소하입니다.\n태준: 반갑습니다 대표님, 비서실장 태준입니다.');
  assert.strictEqual(multiSegs.length, 2);
  assert.strictEqual(multiSegs[0].voice, 'ko-KR-SunHiNeural');
  assert.strictEqual(multiSegs[1].voice, 'ko-KR-InJoonNeural');

  // 2. Verify Live Server /api/ear/control (Mic Mute & Unmute)
  const muteRes = await requestJson('POST', '/api/ear/control', { micMuted: true, ttsMuted: true });
  assert.strictEqual(muteRes.status, 200);
  assert.strictEqual(muteRes.json.micMuted, true);
  assert.strictEqual(muteRes.json.ttsMuted, true);

  const blockedVoice = await requestJson('POST', '/api/butler/command', {
    sentence: '자비스 지금 몇 시야',
    source: 'voice',
    executeOs: false
  });
  assert.strictEqual(blockedVoice.json.ignored, true);
  assert.strictEqual(blockedVoice.json.reason, 'mic-muted');

  const unmuteRes = await requestJson('POST', '/api/ear/control', { micMuted: false, ttsMuted: false });
  assert.strictEqual(unmuteRes.status, 200);
  assert.strictEqual(unmuteRes.json.micMuted, false);
  assert.strictEqual(unmuteRes.json.ttsMuted, false);

  // 3. Verify Live Female TTS (Soha) & Male TTS (Taejun) WAV Synthesis
  const sohaWav = await requestBinary('/api/tts-mp3?format=wav&agent=soha&text=' + encodeURIComponent('안녕하세요 이건우 대표님, 수석비서 소하입니다.'));
  assert.strictEqual(sohaWav.status, 200);
  assert.strictEqual(sohaWav.headers['x-jarvis-tts-voice'], 'ko-KR-SunHiNeural');
  assert.strictEqual(sohaWav.headers['x-jarvis-tts-gender'], 'female');
  assert.strictEqual(sohaWav.buffer.slice(0, 4).toString('ascii'), 'RIFF');

  const taejunWav = await requestBinary('/api/tts-mp3?format=wav&agent=taejun&text=' + encodeURIComponent('반갑습니다 이건우 대표님, 비서실장 태준입니다.'));
  assert.strictEqual(taejunWav.status, 200);
  assert.strictEqual(taejunWav.headers['x-jarvis-tts-voice'], 'ko-KR-InJoonNeural');
  assert.strictEqual(taejunWav.headers['x-jarvis-tts-gender'], 'male');
  assert.strictEqual(taejunWav.buffer.slice(0, 4).toString('ascii'), 'RIFF');

  // 4. Verify Multi-Persona Dialogue WAV Synthesis (Soha Female + Taejun Male in one WAV)
  const dialogueText = '소하: 안녕하세요 대표님, 수석비서 소하입니다.\n태준: 반갑습니다 대표님, 비서실장 태준입니다.';
  const multiWav = await requestBinary('/api/tts-mp3?format=wav&text=' + encodeURIComponent(dialogueText));
  assert.strictEqual(multiWav.status, 200);
  assert.strictEqual(multiWav.buffer.slice(0, 4).toString('ascii'), 'RIFF');
  assert.ok(multiWav.buffer.length > sohaWav.buffer.length, 'Multi-persona WAV should be longer than single-persona WAV');

  console.log(JSON.stringify({
    status: 'PASS',
    totalPersonas: bscPersonas.length,
    femalePersonas: females.length,
    malePersonas: males.length,
    micMuteCollisionBlockVerified: true,
    sohaFemaleWavBytes: sohaWav.buffer.length,
    sohaVoice: sohaWav.headers['x-jarvis-tts-voice'],
    taejunMaleWavBytes: taejunWav.buffer.length,
    taejunVoice: taejunWav.headers['x-jarvis-tts-voice'],
    multiPersonaWavBytes: multiWav.buffer.length,
    elapsedMs: Date.now() - startedAt
  }, null, 2));
}

main().catch(err => {
  console.error('VERIFY_FAIL:', err);
  process.exit(1);
});

'use strict';
const assert = require('assert/strict'), fs = require('fs'), os = require('os'), path = require('path');
process.env.JARVIS_DATA_DIR = fs.mkdtempSync(path.join(os.tmpdir(), 'jarvis-brain-api-'));
process.env.JARVIS_DISABLE_EXTERNAL_HUB = '1'; process.env.JARVIS_DISABLE_NEURAL_TTS = '1';
process.env.JARVIS_PORT = String(30000 + Math.floor(Math.random() * 20000));
const { server, PORT } = require('../server');
(async () => {
  await new Promise(resolve => server.listen(PORT, '127.0.0.1', resolve));
  const request = async (route, body) => (await fetch('http://127.0.0.1:' + PORT + route, body ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {})).json();
  try {
    await request('/api/config', { brainProvider: 'gemini', brainModel: 'gemini-3.5-flash-lite', geminiApiKey: '' });
    const before = await request('/api/brain/status'); assert.equal(before.last, null);
    const checked = await request('/api/brain/verify', {});
    assert.equal(checked.ok, false); assert.equal(checked.last.ok, false); assert.equal(checked.last.error, 'api-key-required');
    assert.equal(checked.last.modelVerified, false); assert.equal(checked.last.fallback, false);
    await request('/api/config', { brainProvider: 'antigravity-cli', brainModel: 'gemini-3.8-flash-low' });
    const changed = await request('/api/brain/status'); assert.equal(changed.last, null); assert.equal(changed.selectedModel, 'gemini-3.8-flash-low');
    assert(!JSON.stringify(changed).includes('geminiApiKey'));
    console.log('PASS: 실제 HTTP 연결 검사 실패 표시 · 선택 모델 변경 시 이전 결과 제외 · 상태에 인증 정보 미노출');
  } finally { await new Promise(resolve => server.close(resolve)); }
})().catch(error => { console.error(error); process.exitCode = 1; });

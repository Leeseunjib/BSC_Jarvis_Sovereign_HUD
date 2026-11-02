'use strict';
const assert = require('assert/strict');
const { askVoiceBrain } = require('./voice-brain');
(async () => {
  const calls = [];
  const mock = async (url, options) => {
    calls.push({ url, options });
    if (url.endsWith('/api/ps')) return { ok: true, json: async () => ({ models: [{ name: 'gemma4:e4b' }] }) };
    const body = JSON.parse(options.body);
    assert.equal(body.think, false); assert.equal(body.options.num_predict, 120);
    assert.equal(body.messages[1].content, '오늘 기분이 좋아');
    return { ok: true, json: async () => ({ model: body.model, message: { content: '기분 좋은 하루 보내세요.' } }) };
  };
  const cfg = { brainProvider: 'gemini-cli', brainModel: 'gemini-3-flash-preview' };
  const result = await askVoiceBrain('오늘 기분이 좋아', cfg, { fetchFn: mock, findCli: () => null, askCli: () => { throw new Error('미설치 CLI는 실행하면 안 됩니다'); } });
  assert.equal(result.provider, 'unavailable'); assert.equal(result.fallback, false); assert.equal(calls.length, 0);
  assert(calls.every(c => c.url.startsWith('http://127.0.0.1:11434/')), '없는 CLI를 다른 유료 서비스로 전환하면 안 됩니다');
  const failure = await askVoiceBrain('확인해줘', cfg, { fetchFn: async () => { throw new Error('offline'); }, findCli: () => null });
  assert.equal(failure.provider, 'unavailable'); assert(!failure.text.includes('확인했습니다'));
  const direct = await askVoiceBrain('오늘 기분이 좋아', { brainProvider: 'ollama', brainModel: 'gemma4:e2b' }, { fetchFn: mock });
  assert.equal(direct.model, 'gemma4:e2b'); assert.equal(direct.fallback, false);
  const agy = await askVoiceBrain('질문', { brainProvider: 'antigravity-cli', brainModel: 'gemini-3.8-flash-low' }, { askAgy: async (prompt, model) => ({ ok: true, text: '응답', provider: 'antigravity-cli', model, modelVerified: true, fallback: false }) });
  assert.equal(agy.model, 'gemini-3.8-flash-low'); assert(agy.modelVerified); assert.equal(agy.fallback, false);
  const mismatch = await askVoiceBrain('질문', { brainProvider: 'antigravity-cli', brainModel: 'gemini-3.8-flash-low' }, { askAgy: async () => ({ ok: true, model: 'other', modelVerified: true }) });
  assert.equal(mismatch.error, 'antigravity-model-unverified');
  const api = await askVoiceBrain('질문', { brainProvider: 'gemini', brainModel: 'gemini-3.5-flash-lite' }, { fetchFn: () => { throw Error('키 없이 요청 금지'); } });
  assert.equal(api.error, 'api-key-required');
  console.log('PASS: 선택 모델 보존 · 자동 대체 없음 · Antigravity 응답 확인 · API 키 누락 안내');
})().catch(error => { console.error(error); process.exitCode = 1; });

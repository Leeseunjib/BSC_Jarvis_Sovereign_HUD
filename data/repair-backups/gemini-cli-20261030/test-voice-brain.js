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
    return { ok: true, json: async () => ({ message: { content: '기분 좋은 하루 보내세요.' } }) };
  };
  const cfg = { brainProvider: 'gemini-cli', brainModel: 'gemini-3-flash-preview' };
  const result = await askVoiceBrain('오늘 기분이 좋아', cfg, { fetchFn: mock, findCli: () => null, askCli: () => { throw new Error('미설치 CLI는 실행하면 안 됩니다'); } });
  assert.equal(result.provider, 'ollama'); assert.equal(result.model, 'gemma4:e4b'); assert.equal(result.fallback, true);
  assert.equal(result.fallbackReason, 'gemini-cli-not-installed'); assert.equal(calls.length, 2);
  assert(calls.every(c => c.url.startsWith('http://127.0.0.1:11434/')), '없는 CLI를 다른 유료 서비스로 전환하면 안 됩니다');
  const failure = await askVoiceBrain('확인해줘', cfg, { fetchFn: async () => { throw new Error('offline'); }, findCli: () => null });
  assert.equal(failure.provider, 'unavailable'); assert.match(failure.text, /연결하지 못/); assert(!failure.text.includes('확인했습니다'));
  const direct = await askVoiceBrain('오늘 기분이 좋아', { brainProvider: 'ollama', brainModel: 'gemma4:e2b' }, { fetchFn: mock });
  assert.equal(direct.model, 'gemma4:e2b'); assert.equal(direct.fallback, false);
  const cli = await askVoiceBrain('질문', cfg, { findCli: () => ({ file: 'test' }), askCli: async () => 'CLI 응답', fetchFn: () => { throw new Error('성공 뒤 대체 호출 금지'); } });
  assert.equal(cli.provider, 'gemini-cli'); assert.equal(cli.fallback, false);
  console.log('PASS: CLI 미설치 시 즉시 로컬 응답 · 제공자 표시 · 실패 명시 · 선택 모델 보존');
})().catch(error => { console.error(error); process.exitCode = 1; });

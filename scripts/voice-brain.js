'use strict';
const fs = require('fs'), path = require('path');
const { askAntigravity, stopAntigravity, findAntigravity } = require('./antigravity-brain');
const { getRulesForPrompt } = require('./recursive-learning');
let last = null;
let verification = null;
function findGeminiCli(env = process.env) {
  const directories = [path.join(env.APPDATA || '', 'npm'), ...(env.PATH || env.Path || '').split(path.delimiter)];
  for (const dir of directories.filter(Boolean)) {
    const base = dir.replace(/^"|"$/g, '');
    const root = path.join(base, 'node_modules', '@google', 'gemini-cli');
    for (const relative of ['bundle/gemini.js', 'dist/index.js']) {
      const entry = path.join(root, relative);
      if (fs.existsSync(entry)) return { file: process.execPath, prefix: [entry] };
    }
    const exe = path.join(base, 'gemini.exe');
    if (fs.existsSync(exe)) return { file: exe, prefix: [] };
  }
  return null;
}
function failure(cfg, message, error) {
  return { text: message, provider: 'unavailable', requestedModel: cfg.brainModel, fallback: false, modelVerified: false, error };
}
async function query(prompt, cfg, { fetchFn = fetch, findCli = findGeminiCli, askAgy = askAntigravity } = {}) {
  const started = Date.now();
  const provider = String(cfg.brainProvider || 'antigravity-cli').toLowerCase();
  let learnedRules = '';
  try { learnedRules = getRulesForPrompt(1500); } catch (_) {}
  const system = '한국어 음성 비서 자비스입니다. 사용자를 반드시 대표님 또는 이건우 대표님으로 부르세요. 질문에 직접 답하세요. 일상 대화는 1~2문장, 100자 이내로 짧게 말하세요. 실행하지 않은 작업을 완료했다고 말하지 마세요.' + (learnedRules ? '\n' + learnedRules : '');
  if (provider === 'antigravity-cli') {
    const result = await askAgy(system + '\n사용자: ' + String(prompt).slice(0, 2000), cfg.brainModel);
    if (!result.ok) return failure(cfg, result.error, 'antigravity-unavailable');
    if (result.model !== cfg.brainModel || !result.modelVerified) return failure(cfg, '선택한 모델의 응답을 확인하지 못했습니다.', 'antigravity-model-unverified');
    return result;
  }
  if (provider === 'gemini-cli' || provider === 'geminicli') {
    return failure(cfg, findCli() ? '개인 Google 로그인의 Gemini CLI 지원이 종료됐습니다. Antigravity CLI 모델을 선택해 주세요.' : 'Gemini CLI가 설치되어 있지 않습니다. 연결 방식을 확인해 주세요.', 'consumer-cli-unavailable');
  }
  if (provider === 'gemini') {
    if (!cfg.geminiApiKey) return failure(cfg, '이 모델은 Gemini API 키가 필요합니다. 계정 및 API 연결에서 입력해 주세요.', 'api-key-required');
    try {
      const response = await fetchFn('https://generativelanguage.googleapis.com/v1beta/models/' + encodeURIComponent(cfg.brainModel) + ':generateContent', { method: 'POST', headers: { 'Content-Type': 'application/json', 'x-goog-api-key': cfg.geminiApiKey }, signal: AbortSignal.timeout(12000), body: JSON.stringify({ systemInstruction: { parts: [{ text: system }] }, contents: [{ role: 'user', parts: [{ text: prompt }] }], generationConfig: { maxOutputTokens: 768 } }) });
      const result = await response.json();
      const text = (result.candidates?.[0]?.content?.parts || []).filter(p => !p.thought).map(p => p.text || '').join('').trim();
      const actual = String(result.modelVersion || '');
      if (!response.ok || !text || !(actual === cfg.brainModel || actual.startsWith(cfg.brainModel + '-'))) return failure(cfg, '선택한 Gemini API 모델의 응답을 확인하지 못했습니다. 키·모델·한도를 확인해 주세요.', 'api-model-unverified');
      return { text, provider: 'gemini', model: actual, requestedModel: cfg.brainModel, modelVerified: true, fallback: false };
    } catch { return failure(cfg, 'Gemini API 응답 시간이 초과되었거나 연결이 끊겼습니다.', 'api-timeout'); }
  }
  if (provider !== 'ollama') return failure(cfg, '이 연결 방식은 대화 연결 검사를 지원하지 않습니다.', 'unsupported-provider');
  const json = async (url, options = {}, timeout = 700) => {
    const response = await fetchFn(url, { ...options, signal: AbortSignal.timeout(timeout) });
    if (!response.ok) throw new Error('local-ai-unavailable');
    return response.json();
  };
  try {
    const base = 'http://127.0.0.1:11434';
    let model = provider === 'ollama' ? cfg.brainModel : '';
    if (!model) {
      const running = await json(base + '/api/ps');
      const preferred = ['gemma4:e4b', 'gemma4:e2b', 'gemma4:latest'];
      model = preferred.find(name => (running.models || []).some(m => m.name === name));
      if (!model) {
        const installed = await json(base + '/api/tags');
        model = preferred.find(name => (installed.models || []).some(m => m.name === name));
      }
    }
    if (!model) throw new Error('local-model-not-installed');
    const result = await json(base + '/api/chat', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
      model, stream: false, think: false, keep_alive: '10m',
      messages: [{ role: 'system', content: system }, { role: 'user', content: String(prompt).slice(0, 2000) }],
      options: { num_predict: 120, temperature: .4 }
    }) }, 4500);
    const text = String(result.message?.content || '').trim();
    if (!text || result.model !== model) throw new Error('local-model-unverified');
    return { text, provider: 'ollama', model: result.model, requestedModel: model, modelVerified: true, fallback: false, elapsedMs: Date.now() - started };
  } catch (error) {
    return { text: '대화 AI에 연결하지 못했습니다. AI 연결 설정을 확인해 주세요.', provider: 'unavailable', fallback: false, error: error.name, elapsedMs: Date.now() - started };
  }
}
async function askVoiceBrain(prompt, cfg, dependencies) {
  const started = Date.now(); let result;
  try { result = await query(prompt, cfg, dependencies); }
  catch { result = failure(cfg, 'AI 연결을 완료하지 못했습니다. 환경설정에서 연결 검사를 실행해 주세요.', 'connection-error'); }
  result.elapsedMs = Date.now() - started;
  last = { at: new Date().toISOString(), selectedProvider: cfg.brainProvider, selectedModel: cfg.brainModel, ok: result.provider !== 'unavailable', provider: result.provider, model: result.model || null, modelVerified: !!result.modelVerified, fallback: !!result.fallback, elapsedMs: result.elapsedMs, firstTextMs: result.firstTextMs, warm: result.warm, error: result.error, message: result.provider === 'unavailable' ? result.text : undefined };
  return result;
}
function getBrainStatus(cfg) {
  return { ok: true, selectedProvider: cfg.brainProvider, selectedModel: cfg.brainModel, preparing: !!verification, geminiCliInstalled: !!findGeminiCli(), antigravityCliInstalled: !!findAntigravity(), last: last?.selectedProvider === cfg.brainProvider && last?.selectedModel === cfg.brainModel ? last : null };
}
function verifyBrain(cfg) {
  const key = cfg.brainProvider + ':' + cfg.brainModel;
  if (verification?.key === key) return verification.promise;
  const request = { key };
  request.promise = askVoiceBrain('연결 시험입니다. 준비되었다고 짧게 답해 주세요.', cfg).finally(() => { if (verification === request) verification = null; });
  verification = request;
  return request.promise;
}
module.exports = { askVoiceBrain, getBrainStatus, verifyBrain, findGeminiCli, stopAntigravity };

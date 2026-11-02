'use strict';
const fs = require('fs'), path = require('path');
const { execFile } = require('child_process');
function findGeminiCli(env = process.env) {
  const directories = [path.join(env.APPDATA || '', 'npm'), ...(env.PATH || env.Path || '').split(path.delimiter)];
  for (const dir of directories.filter(Boolean)) {
    const base = dir.replace(/^"|"$/g, '');
    const entry = path.join(base, 'node_modules', '@google', 'gemini-cli', 'dist', 'index.js');
    if (fs.existsSync(entry)) return { file: process.execPath, prefix: [entry] };
    const exe = path.join(base, 'gemini.exe');
    if (fs.existsSync(exe)) return { file: exe, prefix: [] };
  }
  return null;
}
function askInstalledCli(prompt, model, cli = findGeminiCli()) {
  if (!cli) return Promise.resolve(null);
  return new Promise(resolve => execFile(cli.file, [...cli.prefix, '-m', model, '-p', prompt], { windowsHide: true, timeout: 5000, maxBuffer: 128 * 1024 }, (error, stdout) => resolve(!error && stdout.trim() ? stdout.trim() : null)));
}
async function askVoiceBrain(prompt, cfg, { fetchFn = fetch, findCli = findGeminiCli, askCli = askInstalledCli } = {}) {
  const started = Date.now();
  const provider = String(cfg.brainProvider || 'gemini-cli').toLowerCase();
  const system = '한국어 음성 비서 자비스입니다. 질문에 직접 답하세요. 일상 대화는 1~2문장, 100자 이내로 짧게 말하세요. 실행하지 않은 작업을 완료했다고 말하지 마세요.';
  let fallbackReason = '';
  if (provider === 'gemini-cli' || provider === 'geminicli') {
    const cli = findCli();
    if (cli) {
      const text = await askCli(system + '\n사용자: ' + prompt, cfg.brainModel || 'gemini-3-flash-preview', cli);
      if (text) return { text, provider: 'gemini-cli', model: cfg.brainModel, fallback: false, elapsedMs: Date.now() - started };
      fallbackReason = 'gemini-cli-unavailable';
    } else fallbackReason = 'gemini-cli-not-installed';
  }
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
    if (!text) throw new Error('empty-ai-answer');
    return { text, provider: 'ollama', model, fallback: provider !== 'ollama', fallbackReason, elapsedMs: Date.now() - started };
  } catch (error) {
    return { text: '대화 AI에 연결하지 못했습니다. AI 연결 설정을 확인해 주세요.', provider: 'unavailable', fallback: false, error: error.name, elapsedMs: Date.now() - started };
  }
}
module.exports = { askVoiceBrain, findGeminiCli, askInstalledCli };

'use strict';
const fs = require('fs'), path = require('path'), os = require('os');
const { spawn, fork } = require('child_process');
const WORKSPACE = path.join(os.tmpdir(), 'jarvis-voice-response');
const AGENT = 'jarvis-voice-response';
function findAntigravity(env = process.env) {
  const candidates = [path.join(env.LOCALAPPDATA || '', 'agy', 'bin', 'agy.exe'), ...(env.PATH || env.Path || '').split(path.delimiter).filter(Boolean).map(d => path.join(d.replace(/^"|"$/g, ''), 'agy.exe'))];
  return candidates.find(p => fs.existsSync(p)) || null;
}
function safeError(message) {
  if (/not logged|authentication required|unauthenticated|login|sign in/i.test(message)) return 'Google 로그인이 필요합니다. 환경설정에서 Antigravity 계정 연결을 열어 주세요.';
  if (/quota|resource.exhausted|credit|429/i.test(message)) return '선택 모델의 사용 한도에 도달했습니다. 계정 한도를 확인해 주세요.';
  if (/model.*(not found|not available|invalid)|unknown model/i.test(message)) return '이 계정에서 선택한 모델을 사용할 수 없습니다. 지원되는 모델을 선택해 주세요.';
  return '선택 모델이 제한 시간 안에 답하지 못했습니다. 연결 검사로 상태를 확인해 주세요.';
}
class AntigravitySession {
  constructor({ spawnFn = spawn, findCli = findAntigravity, workspace = WORKSPACE, timeoutMs = 60000 } = {}) {
    Object.assign(this, { spawnFn, findCli, workspace, timeoutMs });
    this.child = null; this.pending = null; this.initialized = false; this.turns = 0;
  }
  close() {
    const child = this.child; this.child = null; this.initialized = false;
    if (child) { child.stdin.destroy(); child.kill(); }
  }
  fail(reason) {
    const pending = this.pending; this.pending = null;
    this.close();
    if (pending) { clearTimeout(pending.timer); pending.resolve({ ok: false, error: reason, elapsedMs: Date.now() - pending.started }); }
  }
  begin(model) {
    const file = this.findCli();
    if (!file) throw new Error('Antigravity CLI가 설치되어 있지 않습니다.');
    fs.mkdirSync(this.workspace, { recursive: true });
    this.model = model; this.actualModel = null; this.initialized = false; this.turns = 0; this.stderr = '';
    const settings = { agentMode: '', modelProvider: '', toolPermission: 'request-review', allowNonWorkspaceAccess: false, useG1Credits: false, permissions: { allow: [], ask: [], deny: ['read_file(*)', 'write_file(*)', 'command(*)', 'read_url(*)', 'execute_url(*)', 'mcp(*)'] } };
    const child = this.spawnFn(file, ['--model', model, '--agent', AGENT, '--settings', JSON.stringify(settings), '--disable-slash-commands', '--input-format', 'stream-json', '--output-format', 'stream-json'], { cwd: this.workspace, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    this.child = child;
    let buffer = '';
    child.stdout.setEncoding('utf8'); child.stderr.setEncoding('utf8');
    child.stderr.on('data', s => { this.stderr = (this.stderr + s).slice(-4000); });
    child.stdout.on('data', s => {
      if (this.child !== child) return;
      buffer += s;
      if (buffer.length > 512000) return this.fail('AI 응답 형식이 너무 큽니다. 다시 연결해 주세요.');
      let newline;
      while ((newline = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, newline); buffer = buffer.slice(newline + 1);
        if (!line.trim()) continue;
        try { this.event(JSON.parse(line)); } catch { this.fail('AI 응답 형식을 확인하지 못했습니다.'); }
      }
    });
    child.on('error', () => { if (this.child === child) this.fail('Antigravity CLI를 시작하지 못했습니다.'); });
    child.on('exit', () => { if (this.child === child) this.fail(safeError(this.stderr)); });
    child.stdin.on('error', () => { if (this.child === child) this.fail('AI 연결이 끊겼습니다. 다시 요청해 주세요.'); });
  }
  event(event) {
    if (event.event === 'init') {
      const init = event.init || {};
      if (init.model !== this.model || init.agent !== AGENT) return this.fail('요청 모델 또는 대화 전용 설정이 일치하지 않아 응답을 중단했습니다.');
      // CLI init lists the global registry, even for a custom primary agent.
      // Headless requests must never inherit automatic tool approval.
      if (!['request-review', 'strict'].includes(init.permission_mode)) return this.fail('AI 도구 자동 실행 설정을 끈 뒤 다시 연결해 주세요.');
      this.actualModel = init.model; this.initialized = true;
      this.sendPending(); return;
    }
    if (event.event === 'step_update') {
      if (event.step_update?.step_type === 'tool' && event.step_update.tool_name !== 'finish') return this.fail('대화 응답에서 도구 실행을 요청해 중단했습니다.');
      if (this.pending && event.step_update?.step_type === 'agent_response' && event.step_update.text_delta && this.pending.firstTextMs === null) this.pending.firstTextMs = Date.now() - this.pending.started;
    }
    if (event.event !== 'result' || !this.pending) return;
    const result = event.result || {};
    if (result.status !== 'SUCCESS' || result.finished_reason === 'print_timeout' || !String(result.response || '').trim() || this.actualModel !== this.model) return this.fail(safeError(result.error || 'empty-result'));
    const pending = this.pending; this.pending = null; clearTimeout(pending.timer); this.turns++;
    pending.resolve({ ok: true, text: String(result.response).trim(), provider: 'antigravity-cli', model: this.actualModel, requestedModel: this.model, modelVerified: true, fallback: false, elapsedMs: Date.now() - pending.started, firstTextMs: pending.firstTextMs, warm: pending.warm });
  }
  sendPending() {
    if (!this.initialized || !this.pending || this.pending.sent) return;
    this.pending.sent = true;
    this.child.stdin.write(JSON.stringify({ event: 'user', message: { content: this.pending.prompt } }) + '\n');
  }
  ask(prompt, model) {
    if (this.pending) return Promise.resolve({ ok: false, error: '이전 AI 응답을 처리 중입니다. 잠시 후 다시 요청해 주세요.' });
    if (!/^gemini-[a-zA-Z0-9._-]{1,90}$/.test(model || '')) return Promise.resolve({ ok: false, error: '환경설정에서 지원되는 Gemini 모델을 선택해 주세요.' });
    if (this.child && (this.model !== model || this.turns >= 100)) this.close();
    return new Promise(resolve => {
      const started = Date.now();
      this.pending = { resolve, started, prompt, sent: false, firstTextMs: null, warm: this.initialized, timer: setTimeout(() => this.fail(safeError(this.stderr || 'timeout')), this.initialized ? Math.min(18000, this.timeoutMs) : this.timeoutMs) };
      try { if (!this.child) this.begin(model); this.sendPending(); } catch (error) { this.fail(error.message); }
    });
  }
}
let worker = null, pending = null, counter = 0;
function stopAntigravity(reason = 'AI 연결 설정이 바뀌었습니다. 다시 요청해 주세요.') {
  if (pending) { clearTimeout(pending.timer); pending.resolve({ ok: false, error: reason }); pending = null; }
  if (worker) { const old = worker; worker = null; if (old.connected) old.disconnect(); }
}
function askAntigravity(prompt, model) {
  if (pending) return Promise.resolve({ ok: false, error: '이전 AI 응답을 처리 중입니다.' });
  if (!worker) {
    const child = fork(__filename, ['--worker'], { windowsHide: true, stdio: ['ignore', 'ignore', 'ignore', 'ipc'] }); worker = child;
    child.on('error', () => { if (worker === child) stopAntigravity('AI 연결 작업자를 시작하지 못했습니다.'); });
    child.on('message', message => { if (pending && message.id === pending.id) { const p = pending; pending = null; clearTimeout(p.timer); p.resolve(message.result); } });
    child.on('exit', () => { if (worker === child) { worker = null; if (pending) { const p = pending; pending = null; clearTimeout(p.timer); p.resolve({ ok: false, error: 'AI 연결 작업자가 종료됐습니다. 다시 요청해 주세요.' }); } } });
  }
  return new Promise(resolve => {
    const id = ++counter;
    pending = { id, resolve, timer: setTimeout(() => stopAntigravity('AI 응답 제한 시간을 초과했습니다. 다시 연결해 주세요.'), 65000) };
    try { worker.send({ id, prompt, model }); } catch { stopAntigravity('AI 연결이 끊겼습니다. 다시 요청해 주세요.'); }
  });
}
if (require.main === module && process.argv.includes('--worker')) {
  const session = new AntigravitySession();
  process.on('message', async ({ id, prompt, model }) => { const result = await session.ask(prompt, model); if (process.connected) process.send({ id, result }); });
  process.on('disconnect', () => { session.close(); process.exit(0); });
} else process.once('exit', () => { if (worker?.connected) worker.disconnect(); });
module.exports = { AntigravitySession, askAntigravity, stopAntigravity, findAntigravity, safeError, AGENT, WORKSPACE };

'use strict';
const assert = require('assert/strict'), { EventEmitter } = require('events'), { PassThrough } = require('stream');
const { AntigravitySession, AGENT } = require('./antigravity-brain');
const model = 'gemini-3.8-flash-low';
function fixture(timeoutMs = 1000) {
  const children = [];
  const s = new AntigravitySession({ findCli: () => 'test-cli', timeoutMs, spawnFn: (file, args) => {
    assert(!args.includes('--dangerously-skip-permissions')); assert(args.includes(model) || args.includes('gemini-3.8-flash-medium'));
    const settings = JSON.parse(args[args.indexOf('--settings') + 1]); assert.equal(settings.useG1Credits, false); assert(settings.permissions.deny.includes('command(*)')); assert(settings.permissions.deny.includes('mcp(*)'));
    const c = new EventEmitter(); c.stdout = new PassThrough(); c.stderr = new PassThrough(); c.stdin = new PassThrough(); c.writes = '';
    c.stdin.on('data', b => c.writes += b); c.kill = () => { c.killed = true; }; children.push(c); return c;
  } });
  const emit = e => children.at(-1).stdout.write(JSON.stringify(e) + '\n');
  const init = (overrides = {}) => emit({ event: 'init', init: { model, agent: AGENT, permission_mode: 'request-review', tools: ['finish', 'view_file'], ...overrides } });
  return { s, children, emit, init };
}
(async () => {
  const f = fixture(); let p = f.s.ask('첫 질문', model); assert.equal(f.children[0].writes, ''); f.init();
  assert.equal(JSON.parse(f.children[0].writes).message.content, '첫 질문');
  f.emit({ event: 'step_update', step_update: { step_type: 'agent_response', text_delta: '답변' } });
  f.emit({ event: 'result', result: { status: 'SUCCESS', response: '첫 답변' } });
  let result = await p; assert(result.ok && result.modelVerified); assert.equal(result.model, model);
  p = f.s.ask('다음 질문', model); f.emit({ event: 'result', result: { status: 'SUCCESS', response: '다음 답변' } });
  result = await p; assert.equal(result.warm, true); assert.equal(f.children.length, 1);
  p = f.s.ask('새 모델', 'gemini-3.8-flash-medium'); assert(f.children[0].killed); f.init({ model: 'gemini-3.8-flash-medium' });
  f.emit({ event: 'result', result: { status: 'SUCCESS', response: '새 모델 답변' } }); assert.equal((await p).model, 'gemini-3.8-flash-medium'); f.s.close();
  for (const bad of [{ model: 'gemini-other' }, { agent: 'wrong' }, { permission_mode: 'always-proceed' }]) {
    const t = fixture(); const q = t.s.ask('test', model); t.init(bad); assert.equal((await q).ok, false); assert(t.children[0].killed);
  }
  for (const terminal of [{ status: 'SUCCESS', response: '' }, { status: 'SUCCESS', response: '부분', finished_reason: 'print_timeout' }, { status: 'ERROR', error: 'quota exceeded' }]) {
    const t = fixture(); const q = t.s.ask('test', model); t.init(); t.emit({ event: 'result', result: terminal }); assert.equal((await q).ok, false);
  }
  const timed = fixture(20); assert.equal((await timed.s.ask('test', model)).ok, false); assert(timed.children[0].killed);
  const tool = fixture(); const q = tool.s.ask('test', model); tool.init(); tool.emit({ event: 'step_update', step_update: { step_type: 'tool', tool_name: 'run_command' } }); assert.equal((await q).ok, false);
  console.log('PASS: 모델 일치 · 지속 연결 재사용 · 모델 전환 · 빈 응답/시간 초과/한도/도구 실행 실패 처리');
})().catch(error => { console.error(error); process.exitCode = 1; });

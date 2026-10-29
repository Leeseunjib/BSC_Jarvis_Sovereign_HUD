'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const assert = require('assert/strict');
const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'jarvis-rewrite-test-'));
process.env.JARVIS_DATA_DIR = temp;
process.env.JARVIS_DISABLE_EXTERNAL_HUB = '1';
const { ButlerEngine } = require('./butler-engine');
(async () => {
  const engine = new ButlerEngine();
  const { target } = await engine.interact({ op: 'capture' });
  assert.equal(String(target.process).toLowerCase(), 'uifixture', '시험 입력창을 먼저 선택해 주세요.');
  assert.equal(target.title, '자비스 안전 시험 입력창');
  assert.ok(target.selection);
  const preview = await engine.execute('존댓말로 바꿔', true);
  assert.equal(preview.awaitingConfirm, true);
  assert.ok(engine.pendingSend.text);
  const result = await engine.execute('응', true);
  assert.equal(result.intent, 'confirmSend');
  console.log('PASS: 로컬 AI 초안 생성과 독립 Windows 입력창 수정 요청이 완료되었습니다.');
})().catch(err => { console.error(err.message); process.exitCode = 1; }).finally(() => {
  const resolved = fs.realpathSync(temp), parent = fs.realpathSync(os.tmpdir());
  if (path.dirname(resolved).toLowerCase() === parent.toLowerCase() && path.basename(resolved).startsWith('jarvis-rewrite-test-')) fs.rmSync(resolved, { recursive: true, force: true });
});

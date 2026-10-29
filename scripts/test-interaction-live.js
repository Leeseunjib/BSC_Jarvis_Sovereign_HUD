'use strict';
const assert = require('assert/strict');
const { interact } = require('./windows-interaction');
(async () => {
  const { target } = await interact({ op: 'capture' });
  assert.equal(String(target.process).toLowerCase(), 'uifixture', '시험 전용 입력창이 활성화되어야 합니다.');
  assert.equal(target.title, '자비스 안전 시험 입력창');
  assert.equal(target.editable, true);
  assert.equal(target.selection, '원래 문장');
  const result = await interact({ op: 'append', target, text: '받아쓰기 시험' });
  assert.equal(result.value, '원래 문장 받아쓰기 시험');
  console.log('PASS: 독립 Windows 입력창 선택과 받아쓰기 입력을 확인했습니다.');
})().catch(err => { console.error(err.message); process.exitCode = 1; });

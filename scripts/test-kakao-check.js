'use strict';
const { interact } = require('./windows-interaction');
(async () => {
  const hwnd = process.env.JARVIS_TEST_WINDOW_HANDLE ? Number(process.env.JARVIS_TEST_WINDOW_HANDLE) : undefined;
  const { target } = await interact({ op: 'capture', ...(hwnd ? { hwnd } : {}) });
  if (String(target.process).toLowerCase() !== 'kakaotalk') throw new Error(`현재 활성 창은 카카오톡이 아닙니다 (${target.process}).`);
  if (!target.editable) throw new Error('카카오톡 입력란의 접근성 편집 기능을 찾지 못했습니다.');
  const draftExists = Boolean(String(target.value || '').trim());
  const result = await interact({ op: 'kakao', target, recipient: '현재 대화방', text: '', send: true, dryRun: true });
  console.log(`PASS: 카카오톡 입력란 확인. 전송 경로 ${result.sendMethod}. 기존 내용 ${draftExists ? '있음' : '없음'}. 실제 입력·전송·수신 검증은 수행하지 않았습니다.`);
})().catch(err => { console.error('카카오톡 검증 실패:', err.message); process.exitCode = 1; });

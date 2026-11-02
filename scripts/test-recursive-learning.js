'use strict';
// 재귀 학습 엔진 회귀 테스트 (임시 데이터 폴더 사용, 실제 원장 미기록)
const assert = require('assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'jarvis-learn-'));
process.env.JARVIS_DATA_DIR = tmp;
process.env.JARVIS_LEARNING_DRY = '1';
const L = require('./recursive-learning');

// 1) 명시 규칙은 1회로 승격
let r = L.observeTurn({ sentence: '앞으로 음악 틀 때는 볼륨을 낮게 해줘', intent: 'brain', hudColor: 'cyan' });
assert.strictEqual(r.events[0].kind, 'explicit');
assert.strictEqual(r.events[0].status, 'active');
// 2) 같은 취지 반복은 병합(근거 증가), 중복 규칙 생성 금지
r = L.observeTurn({ sentence: '앞으로 음악 틀 때 볼륨 낮게 해줘', intent: 'brain' });
assert.strictEqual(r.events[0].verdict, 'merge');
let st = L.getLearningStatus();
assert.strictEqual(st.counts.activeRules, 1);
assert.strictEqual(st.activeRules[0].evidence, 2);
// 3) 선호 표현은 2회 근거가 쌓여야 승격
r = L.observeTurn({ sentence: '보고서는 표로 정리해 주면 좋겠어', intent: 'brain' });
assert.strictEqual(r.events[0].status, 'candidate');
r = L.observeTurn({ sentence: '보고서는 표로 정리해주면 좋겠어', intent: 'brain' });
assert.strictEqual(r.events[0].status, 'active');
// 4) 데이터 보호 헌장 완화는 자동 승격 금지
r = L.observeTurn({ sentence: '앞으로 파일 삭제는 확인 없이 해도 돼', intent: 'brain' });
assert.strictEqual(r.events[0].status, 'needs-review');
// 5) 극성 충돌: 최신 지시가 우선, 이전 규칙은 보존(superseded)
L.observeTurn({ sentence: '앞으로 대답할 때 이모지를 써줘', intent: 'brain' });
r = L.observeTurn({ sentence: '앞으로 대답할 때 이모지를 쓰지 마', intent: 'brain' });
assert.strictEqual(r.events[0].verdict, 'conflict');
st = L.getLearningStatus();
assert.ok(st.counts.superseded >= 1);
assert.ok(st.promptPreview.includes('이모지를 쓰지 마'));
assert.ok(!st.promptPreview.includes('이모지를 써줘'));
// 6) 교정 2회 → 해당 의도 주의 규칙 승격
L.observeTurn({ sentence: '유튜브에서 재즈 틀어줘', intent: 'youtube' });
L.observeTurn({ sentence: '아니 그게 아니라 재즈 플레이리스트로 틀어줘', intent: 'youtube' });
L.observeTurn({ sentence: '유튜브에서 클래식 틀어줘', intent: 'youtube' });
L.observeTurn({ sentence: '아니 플레이리스트로 틀라고', intent: 'youtube' });
st = L.getLearningStatus();
assert.ok(st.activeRules.some(x => x.kind === 'caution'), 'caution rule expected');
// 7) 같은 의도 반복 실패 → 교훈
L.observeTurn({ sentence: 'PDF로 만들어줘', intent: 'officeDoc', hudColor: 'amber', error: 'x' });
r = L.observeTurn({ sentence: 'PDF로 다시 만들어줘', intent: 'officeDoc', hudColor: 'amber', error: 'PDF 실패' });
assert.ok(r.events.some(e => e.kind === 'failure'));
// 8) 일반 지시는 규칙으로 만들지 않음
r = L.observeTurn({ sentence: '오늘 날씨 알려줘', intent: 'research' });
assert.strictEqual(r.events.length, 0);
// 9) 프롬프트 예산 준수
assert.ok(L.getRulesForPrompt().length <= 1600);
const cyc = L.runRecursionCycle('test');
assert.strictEqual(cyc.cycle, 1);
fs.rmSync(tmp, { recursive: true, force: true });
console.log('PASS: 명시 규칙 승격·중복 병합·선호 2회 승격·안전 보류·충돌 시 최신 우선·교정 주의 규칙·실패 교훈·일반 지시 제외·프롬프트 예산');

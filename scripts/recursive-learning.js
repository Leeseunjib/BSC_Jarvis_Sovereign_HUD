'use strict';
/**
 * @file C:\자비스\scripts\recursive-learning.js
 * @description Google Harness² 재귀 루프(Propose → Probe → Compose)를 대표님과의 실제 대화에 적용한 자가학습 엔진.
 *
 *   매 대화 턴마다:
 *     1) Observe : 문장·의도·성공/실패를 data/learning/turns.jsonl 에 Append
 *     2) Propose : 규칙 후보 추출
 *                  - explicit   : "앞으로/이제부터/항상/절대 ~", "~하지 마", "기억해", "규칙으로" → 1회로 승격
 *                  - preference : "~했으면 좋겠어/~하면 좋겠어" → 같은 취지 2회 이상일 때 승격
 *                  - correction : "아니/그게 아니라/잘못/다시 해" → 직전 응답에 대한 교정 교훈 (같은 의도 2회 교정 시 주의 규칙 승격)
 *                  - failure    : 같은 의도가 2회 이상 실패 → 경험(EXPERIENCE) 교훈
 *                  - repetition : 같은 요청 3회 이상 → 자주 쓰는 명령으로 기록
 *     3) Probe   : 길이·중복(문자 bigram 유사도)·극성 충돌·안전(데이터 보호 헌장 완화 시도) 검사
 *     4) Compose : 기준 충족 시 규칙 원장(jarvis-plans-and-rules.json) + Harness² guardrails.md + EXPERIENCE.md + jarvis-loop.md 에 승격 기록
 *     5) Apply   : getRulesForPrompt()로 승격 규칙을 AI 답변 시스템 프롬프트에 주입 (P 예산 1,500자)
 *
 *   한계(정직 표기): 모델 가중치를 학습(파인튜닝)하지 않습니다. 규칙·경험을 축적해 매 답변에 주입하는 방식입니다.
 *   안전: 삭제·덮어쓰기·자격증명 관련 규칙 완화는 자동 승격하지 않고 'needs-review'로 보류합니다.
 */
const fs = require('fs');
const path = require('path');

const DATA_DIR = process.env.JARVIS_DATA_DIR || path.join(__dirname, '..', 'data');
const LEARN_DIR = path.join(DATA_DIR, 'learning');
const TURNS_FILE = path.join(LEARN_DIR, 'turns.jsonl');
const STATE_FILE = path.join(LEARN_DIR, 'learned-rules.json');
const LOOP_FILE = path.join(DATA_DIR, 'jarvis-loop.md');
const PROMPT_BUDGET = 1500; // Harness² P 예산 (항상 주입되는 규칙 문자 수 상한)
const RECENT_TURNS = 12;

const IGNORE_INTENTS = new Set(['awake', 'hush', 'bye', 'dictationText', 'confirmSend', 'processing']);
const EXPLICIT = /^(?:자비스[,\s]*)?(?:앞으로(?:는)?|이제부터(?:는)?|항상|매번|늘|절대(?:로)?|반드시)\s+.{4,}|(?:[가-힣]+지\s*마(?:라|세요|줘)?|[가-힣]+지\s*말(?:아|고|아줘)|하면\s*안\s*돼|금지(?:야|해)?)\s*[.!]?$|(?:기억해|명심해|잊지\s*마|규칙으로\s*(?:저장|추가|만들어)|규칙\s*추가)/;
const PREFERENCE = /(?:했으면|하면|해\s*주면|되면)\s*좋겠(?:어|다|습니다)|(?:선호해|좋아해|싫어해|별로야|불편해)/;
const CORRECTION = /^(?:자비스[,\s]*)?(?:아니(?:야|요|라)?|그게\s*아니(?:라|고)|그거\s*말고|틀렸|잘못\s*(?:했|됐|알아)|다시\s*해|이상해|왜\s*.{0,20}(?:안\s*돼|못\s*해|안\s*해|멈췄))/;
const UNSAFE_LOOSEN = /(삭제|지워|덮어\s*써|덮어쓰|초기화|비밀번호|패스워드|api\s*키|토큰|자격\s*증명).{0,20}(해도\s*(?:돼|된다|괜찮)|허용|괜찮아|묻지\s*말고|확인\s*없이)/i;
const NEGATIVE = /(?:[가-힣]+지\s*마(?:라|세요|줘)?|[가-힣]+지\s*말(?:아|고|아줘)?|안\s*돼|금지|말고|않게|없이)/;

function ensureDir() { fs.mkdirSync(LEARN_DIR, { recursive: true }); }
function now() { return new Date().toISOString(); }
function loadState() {
  try { const s = JSON.parse(fs.readFileSync(STATE_FILE, 'utf8')); return { rules: [], lessons: [], intentStats: {}, commandCounts: {}, cycles: 0, ...s }; }
  catch (_) { return { rules: [], lessons: [], intentStats: {}, commandCounts: {}, cycles: 0, createdAt: now() }; }
}
function saveState(state) { ensureDir(); state.updatedAt = now(); fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2), 'utf8'); }
function appendLoop(line) { try { fs.appendFileSync(LOOP_FILE, `- [${now().slice(0, 19).replace('T', ' ')}] [Harness² Recursion] ${line}\n`, 'utf8'); } catch (_) {} }
function readRecentTurns(n = RECENT_TURNS) {
  try { return fs.readFileSync(TURNS_FILE, 'utf8').trim().split(/\r?\n/).slice(-n).map(l => { try { return JSON.parse(l); } catch (_) { return null; } }).filter(Boolean); } catch (_) { return []; }
}

function normalize(text) { return String(text || '').replace(/\s+/g, ' ').trim(); }
function squash(text) { return String(text || '').toLowerCase().replace(/[^0-9a-z가-힣]/g, ''); }
function bigrams(text) { const s = squash(text); const set = new Set(); for (let i = 0; i < s.length - 1; i++) set.add(s.slice(i, i + 2)); return set; }
function similarity(a, b) {
  const A = bigrams(a), B = bigrams(b); if (!A.size || !B.size) return 0;
  let inter = 0; for (const x of A) if (B.has(x)) inter++;
  return inter / (A.size + B.size - inter);
}
/** 규칙 문장 정리: 호출어·"앞으로" 같은 시작어·끝 존칭을 다듬되 대표님 표현은 최대한 유지 */
function toRuleText(sentence) {
  return normalize(sentence)
    .replace(/^자비스[,\s]*/, '')
    .replace(/^(?:규칙\s*추가|규칙으로\s*저장)\s*[:：]?\s*/, '')
    .replace(/^(?:기억해|명심해)\s*[:：]?\s*/, '')
    .replace(/\s*(?:기억해|명심해|규칙으로\s*(?:저장|추가|만들어)(?:줘)?)\s*[.!]?$/, '')
    .trim();
}
function categorize(text) {
  if (/음성|말|목소리|듣|소음|대답|발화|경청/.test(text)) return '음성 · 경청 규칙';
  if (/음악|노래|유튜브|볼륨|재생/.test(text)) return '미디어 · 음악 규칙';
  if (/문서|워드|pdf|ppt|보고서|마크다운|정리/i.test(text)) return '문서 · 보고 규칙';
  if (/코드|스크립트|ps1|py|빌드|배포|git|깃|테스트|chlgd/i.test(text)) return 'CHLGD · 개발 규칙';
  if (/화면|hud|창|ui|디자인|버튼/i.test(text)) return 'HUD · 화면 규칙';
  return '대화 · 업무 규칙';
}

// ---------------------------------------------------------------- Propose
function propose(turn, prev, state) {
  const s = normalize(turn.sentence);
  const out = [];
  if (!s || s.length < 6 || s.length > 300) return out;
  if (turn.intent === 'remember' || EXPLICIT.test(s)) out.push({ kind: 'explicit', text: toRuleText(s), need: 1 });
  else if (PREFERENCE.test(s) && !/[?？]$/.test(s)) out.push({ kind: 'preference', text: toRuleText(s), need: 2 });
  if (prev && CORRECTION.test(s)) {
    out.push({ kind: 'correction', text: `「${normalize(prev.sentence).slice(0, 80)}」 요청에 '${prev.intent}' 방식으로 대응했으나 대표님이 교정: 「${s.slice(0, 120)}」`, intent: prev.intent, need: 2 });
  }
  if (turn.failed) {
    const st = state.intentStats[turn.intent] || {};
    if ((st.failures || 0) >= 2) out.push({ kind: 'failure', text: `'${turn.intent}' 작업이 ${st.failures}회 실패했습니다. 최근 오류: ${String(turn.error || turn.hudTitle || '').slice(0, 140)}`, intent: turn.intent, need: 1 });
  }
  const key = squash(s);
  if (key && (state.commandCounts[key] || 0) === 3) out.push({ kind: 'repetition', text: `자주 하시는 요청: 「${s.slice(0, 100)}」 (${state.commandCounts[key]}회)`, need: 1 });
  return out;
}

// ---------------------------------------------------------------- Probe
function probe(candidate, state) {
  const text = normalize(candidate.text);
  if (text.length < 4 || text.length > 300) return { verdict: 'reject', reason: 'length' };
  if (UNSAFE_LOOSEN.test(text)) return { verdict: 'review', reason: '데이터 보호 헌장 완화 시도 — 대표님 직접 확인 필요' };
  const pool = candidate.kind === 'failure' || candidate.kind === 'correction' || candidate.kind === 'repetition' ? state.lessons : state.rules;
  let best = null, bestSim = 0;
  for (const r of pool) { if (r.status === 'superseded') continue; const sim = similarity(r.text, text); if (sim > bestSim) { bestSim = sim; best = r; } }
  if (best && bestSim >= 0.55) {
    const conflict = candidate.kind !== 'failure' && NEGATIVE.test(best.text) !== NEGATIVE.test(text) && bestSim < 0.9;
    return { verdict: conflict ? 'conflict' : 'merge', target: best, similarity: Number(bestSim.toFixed(2)) };
  }
  return { verdict: 'new' };
}

// ---------------------------------------------------------------- Compose
function promote(rule) {
  rule.status = 'active';
  rule.promotedAt = now();
  if (process.env.JARVIS_LEARNING_DRY === '1') return; // 테스트: 외부 원장 기록 생략
  try {
    if (rule.kind === 'explicit' || rule.kind === 'preference' || rule.kind === 'caution') {
      require('./jarvis-harness2-engine').addConversationRule({ ruleText: rule.text, category: rule.category, source: `Harness² 재귀 학습 (${rule.kind}, 근거 ${rule.evidence}회)` });
    } else {
      const exp = path.join(__dirname, '..', 'harness2', 'domains', 'chlgd_and_cicd', 'EXPERIENCE.md');
      if (fs.existsSync(path.dirname(exp))) fs.appendFileSync(exp, `\n## [${now().slice(0, 10)} Learned ${rule.kind}]\n- ${rule.text}\n`, 'utf8');
    }
  } catch (_) {}
  appendLoop(`승격(${rule.kind}) · ${rule.text.slice(0, 120)} · 근거 ${rule.evidence}회`);
}

function maybePromoteCautionRule(candidate, state, turn) {
  if (candidate.kind !== 'correction') return;
  const same = state.lessons.filter(l => l.kind === 'correction' && l.intent === candidate.intent && l.status !== 'superseded');
  const totalCorrections = same.reduce((sum, l) => sum + (l.evidence || 1), 0);
  if (totalCorrections >= 2 && !state.rules.some(r => r.kind === 'caution' && r.intent === candidate.intent && r.status === 'active')) {
    const caution = {
      id: `rule-${Date.now()}-c`,
      kind: 'caution',
      intent: candidate.intent,
      text: `'${candidate.intent}' 유형 요청은 대표님이 ${totalCorrections}회 교정하셨습니다. 실행 전에 의도를 한 번 더 확인하고, 최근 교정 내용(「${normalize(turn.sentence).slice(0, 60)}」)을 우선 반영합니다.`,
      category: '교정 기반 주의 규칙',
      need: 1,
      evidence: totalCorrections,
      status: 'candidate',
      createdAt: now(),
      lastSeen: now(),
      examples: []
    };
    state.rules.unshift(caution);
    promote(caution);
  }
}

function compose(candidate, probeResult, state, turn) {
  const event = { kind: candidate.kind, verdict: probeResult.verdict, text: candidate.text };
  const isLesson = candidate.kind === 'failure' || candidate.kind === 'correction' || candidate.kind === 'repetition';
  const pool = isLesson ? state.lessons : state.rules;
  if (probeResult.verdict === 'reject') return event;
  if (probeResult.verdict === 'merge') {
    const r = probeResult.target;
    r.evidence = (r.evidence || 1) + 1; r.lastSeen = now(); r.examples = [...new Set([...(r.examples || []), normalize(turn.sentence).slice(0, 120)])].slice(-5);
    if (r.status === 'candidate' && r.evidence >= r.need) promote(r);
    maybePromoteCautionRule(candidate, state, turn);
    event.ruleId = r.id; event.status = r.status; return event;
  }
  const rule = {
    id: `${isLesson ? 'lesson' : 'rule'}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
    kind: candidate.kind, text: normalize(candidate.text), category: categorize(candidate.text), intent: candidate.intent || turn.intent,
    need: candidate.need, evidence: 1, status: probeResult.verdict === 'review' ? 'needs-review' : 'candidate',
    reviewReason: probeResult.reason || undefined, createdAt: now(), lastSeen: now(), examples: [normalize(turn.sentence).slice(0, 120)]
  };
  if (probeResult.verdict === 'conflict') {
    // 최신 지시가 우선. 이전 규칙은 삭제하지 않고 superseded로 보존해 이력을 남깁니다.
    probeResult.target.status = 'superseded'; probeResult.target.supersededBy = rule.id; probeResult.target.supersededAt = now();
    rule.supersedes = probeResult.target.id;
    appendLoop(`충돌 해결 · 이전 규칙 「${probeResult.target.text.slice(0, 60)}」 → 최신 지시 「${rule.text.slice(0, 60)}」`);
  }
  pool.unshift(rule);
  if (rule.status === 'candidate' && rule.evidence >= rule.need) promote(rule);
  maybePromoteCautionRule(candidate, state, turn);
  state.rules = state.rules.slice(0, 200); state.lessons = state.lessons.slice(0, 300);
  event.ruleId = rule.id; event.status = rule.status; return event;
}

// ---------------------------------------------------------------- Public API
/**
 * 대화 한 턴을 관찰하고 재귀 학습을 1회 수행합니다.
 * @param {{sentence:string,intent?:string,hudColor?:string,hudTitle?:string,error?:string,source?:string}} input
 */
function observeTurn(input) {
  const sentence = normalize(input && input.sentence);
  if (!sentence || IGNORE_INTENTS.has(input.intent)) return { ok: true, skipped: true };
  ensureDir();
  const prev = readRecentTurns(1)[0] || null;
  const turn = { ts: now(), sentence: sentence.slice(0, 500), intent: input.intent || 'unknown', failed: input.hudColor === 'amber' || input.intent === 'error' || !!input.error, hudTitle: input.hudTitle || '', error: input.error || '', source: input.source || 'manual' };
  fs.appendFileSync(TURNS_FILE, JSON.stringify(turn) + '\n', 'utf8');
  const state = loadState();
  const st = state.intentStats[turn.intent] || { total: 0, failures: 0 };
  st.total++; if (turn.failed) st.failures++; st.lastAt = turn.ts; state.intentStats[turn.intent] = st;
  const key = squash(sentence); state.commandCounts[key] = (state.commandCounts[key] || 0) + 1;
  const keys = Object.keys(state.commandCounts); if (keys.length > 500) for (const k of keys.slice(0, keys.length - 500)) delete state.commandCounts[k];
  const recentPrev = prev && Date.now() - Date.parse(prev.ts) < 10 * 60 * 1000 ? prev : null;
  const events = propose(turn, recentPrev, state).map(c => compose(c, probe(c, state), state, turn));
  saveState(state);
  return { ok: true, events };
}

/** 재귀 사이클: 오래된 1회성 후보 만료, 통계 갱신. (매일 또는 수동 호출) */
function runRecursionCycle(reason = 'manual') {
  const state = loadState();
  const cutoff = Date.now() - 30 * 24 * 3600 * 1000;
  let expired = 0;
  for (const r of [...state.rules, ...state.lessons]) if (r.status === 'candidate' && Date.parse(r.lastSeen) < cutoff) { r.status = 'expired'; expired++; }
  state.cycles = (state.cycles || 0) + 1; state.lastCycleAt = now(); state.lastCycleReason = reason;
  saveState(state);
  const summary = getLearningStatus();
  appendLoop(`사이클 #${state.cycles} (${reason}) · 활성 규칙 ${summary.counts.activeRules} · 후보 ${summary.counts.candidates} · 보류 ${summary.counts.needsReview} · 만료 ${expired}`);
  return { ok: true, cycle: state.cycles, expired, ...summary };
}

/** AI 답변에 주입할 승격 규칙 (근거×최근성 순, P 예산 내) */
function getRulesForPrompt(budget = PROMPT_BUDGET) {
  const state = loadState();
  const active = state.rules.filter(r => r.status === 'active').sort((a, b) => (b.evidence || 1) - (a.evidence || 1) || Date.parse(b.lastSeen) - Date.parse(a.lastSeen));
  const lines = []; let used = 0;
  for (const r of active) { const line = `- ${r.text}`; if (used + line.length > budget) break; lines.push(line); used += line.length + 1; }
  return lines.length ? `대표님께서 직접 정하신 규칙(반드시 따름):\n${lines.join('\n')}` : '';
}

function getLearningStatus() {
  const state = loadState();
  const by = s => state.rules.filter(r => r.status === s);
  const promptPreview = getRulesForPrompt();
  const activeCount = by('active').length;
  const candidateCount = by('candidate').length + state.lessons.filter(l => l.status === 'candidate').length;
  const needsReviewCount = by('needs-review').length;
  const totalCount = state.rules.length + state.lessons.length;
  return {
    ok: true,
    method: 'Harness² Propose → Probe → Compose (규칙·경험 축적형, 모델 파인튜닝 아님)',
    activeCount,
    totalCount,
    promptChars: promptPreview.length,
    activeRulesCount: activeCount,
    totalRulesCount: totalCount,
    promptInjectionChars: promptPreview.length,
    counts: { activeRules: activeCount, candidates: candidateCount, needsReview: needsReviewCount, superseded: by('superseded').length, lessons: state.lessons.length, observedIntents: Object.keys(state.intentStats).length },
    activeRules: by('active').slice(0, 30).map(r => ({ id: r.id, kind: r.kind, category: r.category, text: r.text, evidence: r.evidence, promotedAt: r.promotedAt })),
    candidates: state.rules.filter(r => r.status === 'candidate').slice(0, 15).map(r => ({ text: r.text, evidence: r.evidence, need: r.need })),
    needsReview: by('needs-review').map(r => ({ id: r.id, text: r.text, reason: r.reviewReason })),
    recentLessons: state.lessons.slice(0, 10).map(l => ({ kind: l.kind, text: l.text, status: l.status, evidence: l.evidence })),
    weakIntents: Object.entries(state.intentStats).filter(([, v]) => v.failures >= 2).map(([k, v]) => ({ intent: k, failures: v.failures, total: v.total })),
    promptPreview,
    cycles: state.cycles || 0, lastCycleAt: state.lastCycleAt || null
  };
}

/** 학습 현황을 자비스 결과 창(ResearchReport) 형식으로 */
function toResearchReport(id) {
  const st = getLearningStatus();
  const sections = [
    { title: `활성 규칙 ${st.counts.activeRules}개 (매 답변에 적용)`, text: st.activeRules.map(r => `• [${r.category}] ${r.text}  (근거 ${r.evidence}회)`).join('\n') || '아직 승격된 규칙이 없습니다. "앞으로 ~해줘", "~하지 마"처럼 말씀하시면 바로 규칙이 됩니다.', sourceId: 1 },
    { title: `규칙 후보 ${st.candidates.length}개 (근거가 더 쌓이면 승격)`, text: st.candidates.map(c => `• ${c.text}  (${c.evidence}/${c.need})`).join('\n') || '없음', sourceId: 1 },
    { title: '교정·실패에서 배운 교훈', text: st.recentLessons.map(l => `• [${l.kind}] ${l.text}`).join('\n') || '없음', sourceId: 1 },
    ...(st.needsReview.length ? [{ title: '대표님 확인이 필요한 규칙 (자동 승격 보류)', text: st.needsReview.map(r => `• ${r.text} — ${r.reason}`).join('\n'), sourceId: 1 }] : [])
  ];
  return {
    id, query: '자비스 재귀 학습 현황', title: 'Harness² 재귀 학습 현황', status: 'ready', createdAt: now(), provider: st.method,
    note: '모델 자체를 재학습하지 않고, 승격된 규칙을 매 답변 프롬프트에 주입하는 방식입니다. 삭제·자격증명 관련 규칙 완화는 자동 승격하지 않습니다.',
    summary: `활성 ${st.counts.activeRules} · 후보 ${st.counts.candidates} · 보류 ${st.counts.needsReview} · 교훈 ${st.counts.lessons} · 사이클 ${st.cycles}회`,
    keywords: [], sections, sources: [{ id: 1, title: 'learned-rules.json', url: 'file:///' + STATE_FILE.replace(/\\/g, '/'), domain: 'C:\\자비스\\data\\learning', readStatus: '로컬 파일' }], errors: []
  };
}

module.exports = { observeTurn, runRecursionCycle, getRulesForPrompt, getLearningStatus, toResearchReport, propose, probe, similarity, toRuleText, STATE_FILE };

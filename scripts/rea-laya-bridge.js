'use strict';
/**
 * @file C:\자비스\scripts\rea-laya-bridge.js
 * @description 자비스 ↔ BeausCreators REA · Laya 실행 브릿지
 *   1) REA 4-Stage 분석: C:\BeausCreators\01.BSC_HQ\3.CompanyOS\Scripts\Invoke-ReaAnalyzer.js 의 runReaAnalysis 호출
 *      (결과 블루프린트는 HQ 6.Knowledge_Index\10.REA_Tech_Blueprints 에 새 파일로 저장됩니다)
 *   2) Laya 의사결정:
 *      - 실제 모델: convaiinnovations/laya-multilingual 로컬 서비스(127.0.0.1:4175, lotto 프로젝트 .venv-laya)
 *        형식 { state, questions: { name: { type:'choice', instructions, criteria } } }
 *      - 보조 라우팅: Invoke-LayaRlGrowth.js routeWithLaya (키워드 규칙 기반 — 모델 추론이 아님)
 */
const fs = require('fs');
const path = require('path');
const http = require('http');
const { spawn } = require('child_process');

const HQ_SCRIPTS = 'C:\\BeausCreators\\01.BSC_HQ\\3.CompanyOS\\Scripts';
const LAYA_PROJECT = 'C:\\BeausCreators\\02.BSC_Branch\\projects\\앱\\4.게임_엔터테인먼트\\lotto';
const LAYA_PYTHON = path.join(LAYA_PROJECT, '.venv-laya', 'Scripts', 'python.exe');
const LAYA_SERVICE = path.join(LAYA_PROJECT, 'scripts', 'laya-service.py');
const LAYA_PORT = Number(process.env.JARVIS_LAYA_PORT || 4175);

const NAMED_TARGETS = {
  '자비스': 'C:\\자비스', 'jarvis': 'C:\\자비스',
  '뷰스크리에이터스': 'C:\\BeausCreators', 'beauscreators': 'C:\\BeausCreators', '본사': 'C:\\BeausCreators\\01.BSC_HQ',
  '뷰티풀월드': 'C:\\BeautifulWorld', 'beautifulworld': 'C:\\BeautifulWorld', '출판사': 'C:\\BeautifulWorld',
  '웨어하우스': 'C:\\Warehouse', 'warehouse': 'C:\\Warehouse', '스킬창고': 'C:\\Warehouse'
};

function safeRequire(file) {
  try { return fs.existsSync(file) ? require(file) : null; } catch (_) { return null; }
}

/** 문장에서 분석 대상 경로를 찾습니다 (명시 경로 → 별칭 순). 못 찾으면 null */
function resolveTarget(sentence) {
  const s = String(sentence || '');
  const explicit = s.match(/[A-Za-z]:\\[^\s"'<>|?*]+/);
  if (explicit && fs.existsSync(explicit[0].replace(/[.,]$/, ''))) return explicit[0].replace(/[.,]$/, '');
  const lower = s.toLowerCase().replace(/\s+/g, '');
  for (const [k, v] of Object.entries(NAMED_TARGETS)) if (lower.includes(k.toLowerCase()) && fs.existsSync(v)) return v;
  return null;
}

// ---------------------------------------------------------------- REA
function runRea(targetPath, query) {
  const rea = safeRequire(path.join(HQ_SCRIPTS, 'Invoke-ReaAnalyzer.js'));
  if (!rea || typeof rea.runReaAnalysis !== 'function') throw new Error('BeausCreators REA 분석기(Invoke-ReaAnalyzer.js)를 불러오지 못했습니다.');
  const resolved = (targetPath && fs.existsSync(targetPath))
    ? targetPath
    : (resolveTarget(query || targetPath) || (fs.existsSync('C:\\자비스') ? 'C:\\자비스' : null));
  if (!resolved || !fs.existsSync(resolved)) throw new Error('분석할 경로를 찾지 못했습니다. 예: "C:\\BeautifulWorld REA 분석해줘"');
  return rea.runReaAnalysis(resolved);
}

function reaToResearchReport(rep, id) {
  const ev = rep.evidenceProvenance || {};
  const obs = ev.observations || {}, der = ev.derivations || {}, inf = ev.inferences || {};
  const topExt = Object.entries((rep.surface && rep.surface.extCounts) || {}).sort((a, b) => b[1] - a[1]).slice(0, 8).map(([k, v]) => `${k} ${v}`).join(', ');
  const sections = [
    { title: '① Observations (관찰 사실)', text: `파일 ${obs.totalFiles}개 · ${Math.round((obs.totalBytes || 0) / 1024)}KB · ${obs.totalLines}줄\n주요 확장자: ${topExt}\n매니페스트 ${(obs.manifests || []).length}개 · 라이선스: ${obs.detectedLicense}`, sourceId: 1 },
    { title: '② Derivations (구조 도출)', text: `클래스 ${der.recoveredClassesCount} · 함수 ${der.recoveredFunctionsCount} · 엔드포인트 ${(der.tracedEndpoints || []).length}개\n가드 위반 ${der.guardViolationsCount}건 · 기술부채 표식 ${der.techDebtCount}건`, sourceId: 1 },
    { title: '③ Inferences (추론)', text: `아키텍처 패턴: ${(inf.architecturalPatterns || []).join(', ') || '특정 패턴 미검출'}\n카피레프트 위험: ${inf.copyleftRiskLevel}`, sourceId: 1 },
    { title: '④ Unknowns (미확인 영역)', text: (ev.unknowns || []).join('\n'), sourceId: 1 }
  ];
  return {
    id, query: `REA 분석 · ${rep.targetName}`, title: `REA 4-Stage 분석 · ${rep.targetName}`, status: 'ready', createdAt: new Date().toISOString(),
    provider: 'BeausCreators Invoke-ReaAnalyzer.js (정적 분석)',
    note: `정적 분석 결과입니다(실행 중 동작은 관찰하지 않음). 소요 ${rep.elapsed_ms}ms · 블루프린트: ${rep.blueprintPath}`,
    summary: sections.map(s => s.title + '\n' + s.text.split('\n')[0]).join('\n\n'),
    keywords: (inf.architecturalPatterns || []).slice(0, 8), sections,
    sources: [{ id: 1, title: 'REA 클린룸 블루프린트 (Markdown)', url: 'file:///' + String(rep.blueprintPath || '').replace(/\\/g, '/'), domain: rep.surface && rep.surface.resolvedPath, readStatus: '로컬 파일' }],
    errors: []
  };
}

// ---------------------------------------------------------------- Laya
function layaRequest(method, pathname, body, timeout = 4000) {
  return new Promise(resolve => {
    const data = body ? JSON.stringify(body) : null;
    const req = http.request({ hostname: '127.0.0.1', port: LAYA_PORT, path: pathname, method, timeout, headers: data ? { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(data) } : {} }, res => {
      let out = ''; res.on('data', d => (out += d)); res.on('end', () => { try { resolve({ status: res.statusCode, body: JSON.parse(out) }); } catch (_) { resolve({ status: res.statusCode, body: null }); } });
    });
    req.on('error', () => resolve({ status: 0, body: null }));
    req.on('timeout', () => { req.destroy(); resolve({ status: 0, body: null }); });
    if (data) req.write(data);
    req.end();
  });
}

async function getLayaStatus() {
  const health = await layaRequest('GET', '/health', null, 1500);
  return {
    installed: fs.existsSync(LAYA_PYTHON) && fs.existsSync(LAYA_SERVICE),
    running: health.status === 200,
    ready: !!(health.body && health.body.ready),
    model: health.body ? health.body.model : 'convaiinnovations/laya-multilingual',
    error: health.body ? health.body.error : '',
    port: LAYA_PORT
  };
}

/** Laya 로컬 서비스 시작 (이미 실행 중이면 그대로 둠). 모델 적재에는 수십 초가 걸릴 수 있습니다. */
async function startLayaService() {
  const st = await getLayaStatus();
  if (st.running) return { ...st, started: false };
  if (!st.installed) throw new Error('Laya 실행 환경(.venv-laya)을 찾지 못했습니다.');
  const child = spawn(LAYA_PYTHON, [LAYA_SERVICE], { cwd: LAYA_PROJECT, detached: true, stdio: 'ignore', windowsHide: true, env: { ...process.env, PYTHONUTF8: '1', PYTHONIOENCODING: 'utf-8', APEX_LAYA_PORT: String(LAYA_PORT) } });
  child.unref();
  return { ...st, started: true, pid: child.pid };
}

const ORDER_QUESTIONS = {
  domain: { type: 'choice', instructions: '대표님의 요청이 어느 업무 영역에 속하는지 하나 고르세요.', criteria: { code_dev: '코드·서버·빌드·스크립트·자동화', ui_design: '화면·디자인·UI/UX', publishing: '출판·원고·전자책·교정', business_ops: '재무·경영·매출·전략', research_rag: '조사·검색·분석·지식 정리', dialogue: '일상 대화·질문·안부' } },
  priority: { type: 'choice', instructions: '요청의 긴급도를 고르세요.', criteria: { high: '지금 바로 처리해야 하는 장애·마감·중요 지시', normal: '일반 업무 요청', low: '참고·아이디어·나중에 해도 되는 일' } },
  risk: { type: 'choice', instructions: '실행 시 위험도를 고르세요.', criteria: { safe: '조회·생성·분석처럼 기존 데이터를 바꾸지 않음', confirm: '파일 삭제·덮어쓰기·외부 전송·결제처럼 사전 확인이 필요함' } }
};

/**
 * Laya 의사결정 실행. 모델 서비스가 준비되어 있으면 실제 추론, 아니면 규칙 기반 라우팅만 반환(구분 표기).
 */
async function runLaya(orderText, customQuestions) {
  const started = Date.now();
  const router = safeRequire(path.join(HQ_SCRIPTS, 'Invoke-LayaRlGrowth.js'));
  const ruleRoute = router && typeof router.routeWithLaya === 'function' ? router.routeWithLaya(orderText) : null;
  const status = await getLayaStatus();
  let model = null;
  const qObj = (customQuestions && typeof customQuestions === 'object' && !customQuestions.questions)
    ? customQuestions
    : (customQuestions && customQuestions.questions) || ORDER_QUESTIONS;
  if (status.ready) {
    const r = await layaRequest('POST', '/predict', { state: String(orderText).slice(0, 4000), questions: qObj }, 30000);
    model = r.status === 200 && r.body ? { ok: true, result: r.body.result, latencyMs: r.body.latencyMs, model: r.body.model } : { ok: false, error: (r.body && r.body.error) || 'Laya 추론 실패' };
  }
  const mode = (model && model.ok) ? 'laya-multilingual' : 'system1-router';
  return { ok: true, mode, order: orderText, modelInference: model, modelStatus: status, ruleRoute, elapsedMs: Date.now() - started };
}

function layaToResearchReport(res, id) {
  const sections = [];
  if (res.modelInference && res.modelInference.ok) sections.push({ title: 'Laya 모델 추론 결과 (laya-multilingual)', text: JSON.stringify(res.modelInference.result, null, 2).slice(0, 1800), sourceId: 1 });
  else sections.push({ title: 'Laya 모델 상태', text: res.modelStatus.running ? (res.modelStatus.ready ? `추론 실패: ${res.modelInference && res.modelInference.error}` : '모델을 적재하는 중입니다. 잠시 후 다시 요청해 주세요.') : 'Laya 로컬 서비스가 꺼져 있습니다. "라야 서버 켜줘"라고 말씀하시면 시작합니다 (CPU 적재 수십 초).', sourceId: 1 });
  if (res.ruleRoute) sections.push({ title: '보조 라우팅 (키워드 규칙 기반 · 모델 추론 아님)', text: `영역: ${res.ruleRoute.detectedDomain}\n권장 담당: ${(res.ruleRoute.recommendedAgents || []).join(', ')}\n권장 로컬 모델: ${res.ruleRoute.selectedSystem2Local}`, sourceId: 2 });
  return {
    id, query: `Laya 판단 · ${String(res.order).slice(0, 60)}`, title: 'Laya 의사결정 결과', status: 'ready', createdAt: new Date().toISOString(),
    provider: res.modelInference && res.modelInference.ok ? `Laya 로컬 모델 (${res.modelInference.latencyMs}ms)` : '규칙 기반 라우팅 (Laya 모델 미가동)',
    note: '판단 결과는 참고용입니다. 위험도가 confirm이면 실행 전에 대표님 확인을 받습니다.',
    summary: sections.map(s => s.title + ': ' + s.text.split('\n')[0]).join('\n'), keywords: [], sections,
    sources: [{ id: 1, title: 'Laya 서비스 (127.0.0.1:' + LAYA_PORT + ')', url: 'http://127.0.0.1:' + LAYA_PORT + '/health', domain: 'localhost', readStatus: res.modelStatus.ready ? '준비됨' : '미준비' }, { id: 2, title: 'Invoke-LayaRlGrowth.js', url: 'file:///' + path.join(HQ_SCRIPTS, 'Invoke-LayaRlGrowth.js').replace(/\\/g, '/'), domain: 'BeausCreators HQ', readStatus: '로컬 파일' }],
    errors: []
  };
}

module.exports = { resolveTarget, runRea, reaToResearchReport, getLayaStatus, startLayaService, runLaya, layaToResearchReport, ORDER_QUESTIONS };
